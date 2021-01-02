using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Simbolo
{
    public static class Client
    {
        public static StackTraceInformation GetStackTraceInformation(Exception exception)
        {
            var stackTrace = new StackTrace(exception, true);
            if (stackTrace.FrameCount == 0)
            {
                return new StackTraceInformation();
            }

            var frames = new List<StackFrameInformation>();
            var debugMetas = new List<DebugMeta>();
            if (stackTrace.GetFrames() is { } stackFrames)
            {
                foreach (var stackFrame in stackFrames)
                {
                    var frame = GetFrameInformation(stackFrame);
                    if (frame is not null)
                    {
                        frames.Add(frame);
                        if (frame.LineNumber == null && GetDebugMeta(stackFrame) is { } debugMeta)
                        {
                            debugMetas.Add(debugMeta);
                        }
                    }
                }
            }

            return new StackTraceInformation(frames, debugMetas);
        }

        private static StackFrameInformation? GetFrameInformation(StackFrame stackFrame)
        {
            if (stackFrame.GetMethod() is not { } method)
            {
                return null;
            }
            
            // https://github.com/dotnet/runtime/blob/c985bdcec2a9190e733bcada413a193d5ff60c0d/src/libraries/System.Private.CoreLib/src/System/Diagnostics/StackTrace.cs#L225-L249
            var isAsync = false;
            var declaringType = method.DeclaringType;
            string originalMethodName = method.Name;
            var methodChanged = false;
            if (declaringType != null && declaringType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                isAsync = typeof(IAsyncStateMachine).IsAssignableFrom(declaringType);
                if (isAsync || typeof(IEnumerator).IsAssignableFrom(declaringType))
                {
                    methodChanged = TryResolveStateMachineMethod(ref method, out declaringType);
                }
            }

            // stackFrame.HasILOffset() throws NotImplemented on Mono 5.12
            var offset = stackFrame.GetILOffset();
            var isIlOffset = true;
            if (offset == 0)
            {
                isIlOffset = false;
                offset = stackFrame.GetNativeOffset();
            }

            int? lineNumber = stackFrame.GetFileLineNumber();
            if (lineNumber == 0)
            {
                lineNumber = null;
            }

            int? columnNumber = stackFrame.GetFileColumnNumber();
            if (columnNumber == 0)
            {
                columnNumber = null;
            }
            
            var methodName = method.Name;
            if (methodChanged)
            {
                // Append original method name e.g. +MoveNext()
                methodName = $"{methodName}+{originalMethodName}()";
            }
            
            List<Parameter>? parameters = null;
            try
            {
                foreach (var parameterInfo in method.GetParameters())
                {
                    parameters ??= new List<Parameter>();
                    var param = new Parameter(
                        parameterInfo.ParameterType.Name,
                        parameterInfo.Name);
                    parameters.Add(param);
                }
            }
            catch
            {
                // Can fail to GetParameters()
                // https://github.com/dotnet/runtime/blob/c985bdcec2a9190e733bcada413a193d5ff60c0d/src/libraries/System.Private.CoreLib/src/System/Diagnostics/StackTrace.cs#L274-L282
            }

            IEnumerable<string>? genericArguments = null;
            if (method.GetGenericArguments() is {Length: > 0} genArgs)
            {
                genericArguments = genArgs.Select(t => t.Name).ToArray();
            }

            return new StackFrameInformation(
                methodName,
                method.MetadataToken,
                stackFrame.GetFileName(),
                offset,
                method.Module.ModuleVersionId,
                isIlOffset,
                null,
                declaringType?.Assembly.FullName,
                declaringType?.FullName?.Replace("+", "."),
                lineNumber,
                columnNumber,
                parameters,
                genericArguments);
        }

        private static readonly ConcurrentDictionary<Assembly, Lazy<DebugMeta>> Cache = new();
        private static readonly DebugMeta Empty = new("", Guid.Empty, "", Guid.Empty, 0, null);

        private static DebugMeta? GetDebugMeta(StackFrame frame)
        {
            var asm = frame.GetMethod()?.DeclaringType?.Assembly;
            var location = asm?.Location;
            if (location is null)
            {
                // TODO: Logging
                return null;
            }

            var cachedDebugMeta = Cache.GetOrAdd(asm!, ValueFactory);
            return cachedDebugMeta.Value == Empty ? null : cachedDebugMeta.Value;
        }

        private static Lazy<DebugMeta> ValueFactory(Assembly asm) =>
            new(() =>
            {
                var location = asm.Location;
                if (!File.Exists(location))
                {
                    return Empty;
                }

                using var stream = File.OpenRead(location);
                var reader = new PEReader(stream);
                var debugMeta = GetDebugMeta(reader);
                return debugMeta ?? Empty;
            });

        private static DebugMeta? GetDebugMeta(PEReader peReader)
        {
            var codeView = peReader.ReadDebugDirectory()
                .FirstOrDefault(d => d.Type == DebugDirectoryEntryType.CodeView);
            if (codeView.Type == DebugDirectoryEntryType.Unknown)
            {
                return null;
            }
            
            // Framework's assemblies don't have pdb checksum. I.e: System.Private.CoreLib.pdb
            IEnumerable<string>? checksums = null;
            var pdbChecksum = peReader.ReadDebugDirectory()
                .FirstOrDefault(d => d.Type == DebugDirectoryEntryType.PdbChecksum);
            if (pdbChecksum.Type != DebugDirectoryEntryType.Unknown)
            {
    
                var checksumData = peReader.ReadPdbChecksumDebugDirectoryData(pdbChecksum);
                var algorithm = checksumData.AlgorithmName;
                var builder = new StringBuilder();
                builder.Append(algorithm);
                builder.Append(':');
                foreach (var bytes in checksumData.Checksum)
                {
                    builder.Append(bytes.ToString("x2"));
                }
                checksums = new[] {builder.ToString()};
            }


            var data = peReader.ReadCodeViewDebugDirectoryData(codeView);
            var isPortable = codeView.IsPortableCodeView;

            var signature = data.Guid; // TODO: Always the same as mvid??
            var age = data.Age;
            var file = data.Path;

            var metadataReader = peReader.GetMetadataReader();
            return new DebugMeta(
                file,
                metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid),
                isPortable ? "ppdb" : "pdb",
                signature,
                age,
                checksums);
        }
        
        // https://github.com/dotnet/runtime/blob/c985bdcec2a9190e733bcada413a193d5ff60c0d/src/libraries/System.Private.CoreLib/src/System/Diagnostics/StackTrace.cs#L375-L430
        private static bool TryResolveStateMachineMethod(ref MethodBase method, [NotNullWhen(true)] out Type declaringType)
        {
            if (method.DeclaringType is null)
            {
                declaringType = null!;
                return false;
            }
            declaringType = method.DeclaringType;

            var parentType = declaringType.DeclaringType;
            if (parentType is null)
            {
                return false;
            }

            static MethodInfo[]? GetDeclaredMethods(Type type) =>
                type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            var methods = GetDeclaredMethods(parentType);
            if (methods == null)
            {
                return false;
            }

            foreach (MethodInfo candidateMethod in methods)
            {
                var attributes = candidateMethod.GetCustomAttributes<StateMachineAttribute>(inherit: false);
                // ReSharper disable once ConditionIsAlwaysTrueOrFalse - Taken from CoreFX
                if (attributes is null)
                {
                    continue;
                }

                bool foundAttribute = false, foundIteratorAttribute = false;
                foreach (StateMachineAttribute asma in attributes)
                {
                    if (asma.StateMachineType == declaringType)
                    {
                        foundAttribute = true;
                        foundIteratorAttribute |= asma is IteratorStateMachineAttribute || asma is AsyncIteratorStateMachineAttribute;
                    }
                }

                if (foundAttribute)
                {
                    // If this is an iterator (sync or async), mark the iterator as changed, so it gets the + annotation
                    // of the original method. Non-iterator async state machines resolve directly to their builder methods
                    // so aren't marked as changed.
                    method = candidateMethod;
                    declaringType = candidateMethod.DeclaringType!;
                    return foundIteratorAttribute;
                }
            }

            return false;
        }
    }

    public class StackTraceInformation
    {
        public IEnumerable<StackFrameInformation> StackFrameInformation { get; }
        public IEnumerable<DebugMeta> DebugMetas { get; }

        public StackTraceInformation(
            IEnumerable<StackFrameInformation> stackFrameInformation,
            IEnumerable<DebugMeta> debugMetas)
        {
            StackFrameInformation = stackFrameInformation;
            DebugMetas = debugMetas;
        }
        
        internal StackTraceInformation()
        {
            StackFrameInformation = Enumerable.Empty<StackFrameInformation>();
            DebugMetas = Enumerable.Empty<DebugMeta>();
        }

        public override string ToString() => ToString("default");

        public string ToString(string format) =>
            format switch
            {
                "json" => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }),
                _ => ToStringDotnet(),
            };

        private string ToStringDotnet()
        {
            var builder = new StringBuilder(256);
            foreach (var info in StackFrameInformation)
            {
                if (info.Method is null)
                {
                    continue;
                }

                builder.Append("   at ");
                
                if (info.TypeFullName is not null)
                {
                    builder.Append(info.TypeFullName);
                    builder.Append('.');
                } 
                builder.Append(info.Method);
                // deal with the generic portion of the method
                if (info.GenericArguments?.ToArray() is {Length: >0} genericArguments)
                {
                    builder.Append('[');
                    var firstGenericArg = true;
                    foreach (var genericArgument in genericArguments)
                    {
                        if (firstGenericArg)
                        {
                            firstGenericArg = false;
                        }
                        else
                        {
                            builder.Append(',');
                        }
                        builder.Append(genericArgument);
                    }
                    builder.Append(']');
                }
                if (info.Parameters is null || !info.Parameters.Any())
                {
                    builder.Append("()");
                }
                else
                {
                    builder.Append('(');
                    var first = true;
                    foreach (var arg in info.Parameters)
                    {
                        if (!first)
                        {
                            builder.Append(", ");
                        }

                        first = false;
                        builder.Append(arg.TypeName);
                        builder.Append(' ');
                        builder.Append(arg.Name);
                    }
                    builder.Append(')');
                }
                if (info.FileName is not null)
                {
                    builder.Append(" in ");
                    builder.Append(info.FileName);
                } 
                else if (info.Mvid is not null)
                {
                    builder.Append(" in ");
                    // Mono format
                    builder.Append('<');
                    builder.AppendFormat(CultureInfo.InvariantCulture, "{0:n}", info.Mvid.Value);
                    if (info.Aotid is not null)
                    {
                        builder.Append('#');
                        builder.Append(info.Aotid);
                    }
                    builder.Append('>');
                }

                if (info.LineNumber is not null)
                {
                    builder.Append(":line ");
                    builder.Append(info.LineNumber);
                }

                if (info.ColumnNumber is not null)
                {
                    builder.Append(':');
                    builder.Append(info.ColumnNumber);
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }
    }

    public class StackFrameInformation
    {
        public int? MethodIndex { get; }
        public int? Offset { get; }
        public bool? IsILOffset { get; }
        public Guid? Mvid { get; }
        public string? Aotid { get; }
        public string? FileName { get; }
        public string? Method { get; }
        public string? AssemblyFullName { get; }
        public string? TypeFullName { get; }
        public int? LineNumber { get; }
        public int? ColumnNumber { get; }
        public IEnumerable<Parameter>? Parameters { get; }
        public IEnumerable<string>? GenericArguments { get; }

        public StackFrameInformation(
            string? method,
            int? methodIndex,
            string? fileName,
            int? offset,
            Guid? mvid,
            bool? isIlOffset,
            string? aotid,
            string? assemblyFullName,
            string? typeFullName,
            int? lineNumber,
            int? columnNumber,
            IEnumerable<Parameter>? parameters,
            IEnumerable<string>? genericArguments)
        {
            MethodIndex = methodIndex;
            Offset = offset;
            IsILOffset = isIlOffset;
            Mvid = mvid;
            Aotid = aotid;
            FileName = fileName;
            Method = method;
            AssemblyFullName = assemblyFullName;
            TypeFullName = typeFullName;
            LineNumber = lineNumber;
            ColumnNumber = columnNumber;
            Parameters = parameters;
            GenericArguments = genericArguments;
        }

        public override string ToString() =>
            $"{nameof(MethodIndex)}: {MethodIndex}, " +
            $"{nameof(Offset)}: {Offset}, " +
            $"{nameof(IsILOffset)}: {IsILOffset}, " +
            $"{nameof(Mvid)}: {Mvid}, " +
            $"{nameof(Aotid)}: {Aotid}, " +
            $"{nameof(FileName)}: {FileName}, " +
            $"{nameof(Method)}: {Method}, " +
            $"{nameof(AssemblyFullName)}: {AssemblyFullName}, " +
            $"{nameof(TypeFullName)}: {TypeFullName}, " +
            $"{nameof(LineNumber)}: {LineNumber}, " +
            $"{nameof(ColumnNumber)}: {ColumnNumber}, " +
            $"{nameof(GenericArguments)}: {string.Join(", ", GenericArguments ?? Enumerable.Empty<string>())}" + 
            $"{nameof(Parameters)}: {string.Join(", ", Parameters ?? Enumerable.Empty<Parameter>())}";
    }
    
    public class Parameter
    {
        public string? TypeName { get; }
        public string? Name { get; }

        public Parameter(string? typeName, string? name)
        {
            TypeName = typeName;
            Name = name;
        }
    }
    public class DebugMeta
    {
        public DebugMeta(string file, Guid moduleId, string type, Guid guid, int age, IEnumerable<string>? checksums)
        {
            File = file;
            ModuleId = moduleId;
            Type = type;
            Guid = guid;
            Age = age;
            Checksums = checksums;
        }

        public string File { get; }
        public Guid ModuleId { get; }
        public bool IsPortable => string.Equals(Type, "ppdb", StringComparison.InvariantCultureIgnoreCase);
        public string Type { get; }
        public Guid Guid { get; }
        public int Age { get; }
        public IEnumerable<string>? Checksums { get; }

        public override string ToString() =>
            $"{nameof(File)}: {File}, " +
            $"{nameof(ModuleId)}: {ModuleId}, " +
            $"{nameof(IsPortable)}: {IsPortable}, " +
            $"{nameof(Type)}: {Type}, " +
            $"{nameof(Guid)}: {Guid}, " +
            $"{nameof(Age)}: {Age}, " +
            $"{nameof(Checksums)}: {Environment.NewLine}{string.Join(Environment.NewLine, Checksums ?? Enumerable.Empty<string>())}";
    }
}
