using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using Simbolo.StackFrameData;

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

            var enhancedStackTrace = EnhancedStackTrace.GetFrames(exception);
            
            var frames = new List<StackFrameInformation>();
            var debugMetas = new Dictionary<Guid, DebugMeta>();
            foreach (var stackFrame in enhancedStackTrace)
            {
                var frame = GetFrameInformation(stackFrame);
                if (frame is not null)
                {
                    frames.Add(frame);
                    if (frame.LineNumber == null && GetDebugMeta(stackFrame) is { } debugMeta 
                                                 && !debugMetas.ContainsKey(debugMeta.ModuleId))
                    {
                        debugMetas[debugMeta.ModuleId] = debugMeta;
                    }
                }
            }

            return new StackTraceInformation(frames, debugMetas);
        }

        private static StackFrameInformation? GetFrameInformation(EnhancedStackFrame stackFrame)
        {
            if (stackFrame.GetMethod() is not { } method || method.Module.Assembly.IsDynamic)
            {
                return null;
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
            
            return new StackFrameInformation(
                stackFrame.ToString(),
                method.MetadataToken,
                EnhancedStackTrace.TryGetFullPath(stackFrame.GetFileName()),
                offset,
                method.Module.ModuleVersionId,
                isIlOffset,
                null,
                stackFrame.MethodInfo.DeclaringType?.Assembly.FullName,
                stackFrame.MethodInfo.DeclaringType?.FullName?.Replace("+", "."),
                lineNumber,
                columnNumber);
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
}
