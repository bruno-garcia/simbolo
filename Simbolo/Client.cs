using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
            foreach (var stackFrame in stackTrace.GetFrames())
            {
                var frame = GetFrameInformation(stackFrame);
                frames.Add(frame);

                if (frame.LineNumber == null && GetDebugMeta(stackFrame) is { } debugMeta)
                {
                    debugMetas.Add(debugMeta);
                }
            }

            return new StackTraceInformation(frames, debugMetas);
        }

        private static StackFrameInformation GetFrameInformation(StackFrame stackFrame)
        {
            if (stackFrame.GetMethod() is not { } method)
            {
                // TODO: Return what we have, def need debug_meta in this case
                return new StackFrameInformation();
            }
            
            var parameterListFormatted = string.Join(
                ", ",
                method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")
            );
            List<Parameter>? parameters = null;
            foreach (var parameterInfo in method.GetParameters())
            {
                parameters ??= new List<Parameter>();
                var param = new Parameter
                {
                    TypeName = parameterInfo.ParameterType.Name,
                    Name = parameterInfo.Name,
                };
                parameters.Add(param);
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
            
            return new StackFrameInformation
            {
                FileName = stackFrame.GetFileName(),
                MethodIndex = method.MetadataToken,
                Offset = offset,
                IsILOffset = isIlOffset,
                Method = method.Name,
                Parameters = parameters,
                LineNumber = lineNumber,
                ColumnNumber = columnNumber,
                AssemblyFullName = method.DeclaringType?.Assembly.FullName,
                TypeFullName = method.DeclaringType?.FullName
            };
        }

        private static readonly ConcurrentDictionary<string, DebugMeta> Cache = new();
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
            if (Cache.TryGetValue(location, out var cachedDebugMeta))
            {
                return cachedDebugMeta == Empty ? null : cachedDebugMeta;
            }

            lock (asm!) // We bail if asm or location is null
            {
                try
                {
                    if (!File.Exists(location))
                    {
                        // TODO: Logging
                        return null;
                    }

                    using var stream = File.OpenRead(location);
                    var reader = new PEReader(stream);
                    var debugMeta = GetDebugMeta(reader);
                    Cache.TryAdd(location, debugMeta ?? Empty);
                    return debugMeta;
                }
                catch
                {
                    Cache.TryAdd(location, Empty);
                    throw;
                }
            }
        }

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
            var builder = new StringBuilder();
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
                if (info.Parameters is null || !info.Parameters.Any())
                {
                    builder.Append("()");
                }
                else
                {
                    builder.Append('(');
                    foreach (var arg in info.Parameters)
                    {
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
                    builder.Append(info.FileName);
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
        public int? MethodIndex { get; init; }
        public int? Offset { get; init; }
        public bool? IsILOffset { get; init; }
        public string? Mvid { get; init; }
        public string? Aotid { get; init; }
        public string? FileName { get; init; }
        public string? Method { get; init; }
        public string? AssemblyFullName { get; init; }
        public string? TypeFullName { get; init; }
        public int? LineNumber { get; init; }
        public int? ColumnNumber { get; init; }
        public IEnumerable<Parameter>? Parameters { get; init; }

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
            $"{nameof(Parameters)}: {string.Join(", ", Parameters ?? Enumerable.Empty<Parameter>())}";
    }

    public class Parameter
    {
        public string? TypeName { get; init; }
        public string? Name { get; init; }
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
        public IEnumerable<string>? Checksums { get; init; }

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