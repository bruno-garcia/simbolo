using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Simbolo
{
    public static class Client
    {
        public static StackTraceInformation GetStackTraceInformation(Exception exception)
        {
            var stackTrace = new StackTrace(exception, true);
            if (stackTrace.FrameCount == 0)
            {
                return new StackTraceInformation(Enumerable.Empty<StackFrameInformation>());
            }

            var frames = new List<StackFrameInformation>();
            foreach (var stackFrame in stackTrace.GetFrames())
            {
                frames.Add(GetFrameInformation(stackFrame));
            }

            // TODO:
            IEnumerable<DebugMeta>? debugMeta = null; 
            return new StackTraceInformation(frames, debugMeta);
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

            var methodSignature = $"{method.Name}({parameterListFormatted})";

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
                Method = methodSignature,
                LineNumber = lineNumber,
                ColumnNumber = columnNumber,
                TypeFullName = method.DeclaringType?.FullName,
                AssemblyFullName = method.DeclaringType?.Assembly.FullName
            };
        }

        internal static DebugMeta GetDebugMeta(PEReader peReader)
        {
            var checksums = new List<string>();

            var pdbChecksum = peReader.ReadDebugDirectory()
                .FirstOrDefault(d => d.Type == DebugDirectoryEntryType.PdbChecksum);
            var checksumData = peReader.ReadPdbChecksumDebugDirectoryData(pdbChecksum);
            var algorithm = checksumData.AlgorithmName;
            var builder = new StringBuilder();
            builder.Append(algorithm);
            builder.Append(':');
            foreach (var bytes in checksumData.Checksum)
            {
                builder.Append(bytes.ToString("x2"));
            }

            checksums.Add(builder.ToString());

            var codeView = peReader.ReadDebugDirectory()
                .FirstOrDefault(d => d.Type == DebugDirectoryEntryType.CodeView);

            var data = peReader.ReadCodeViewDebugDirectoryData(codeView);
            const ushort portableCodeViewVersionMagic = 0x504d;
            var isPortable = codeView.MinorVersion == portableCodeViewVersionMagic;

            var signature = data.Guid;
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
        public IEnumerable<DebugMeta>? DebugMetas { get; set; }

        public StackTraceInformation(
            IEnumerable<StackFrameInformation> stackFrameInformation,
            // Null if the stack trace is already complete
            IEnumerable<DebugMeta>? debugMetas = null)
        {
            StackFrameInformation = stackFrameInformation;
            DebugMetas = debugMetas;
        }

        public override string ToString()
        {
            return $"{nameof(StackFrameInformation)}: {string.Join(Environment.NewLine, StackFrameInformation)}, " +
                   $"{nameof(DebugMetas)}: {(DebugMetas != null ? string.Join(Environment.NewLine, DebugMetas) : "None")}";
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
            $"{nameof(ColumnNumber)}: {ColumnNumber}";
    }
    
    public class DebugMeta
    {
        public DebugMeta(string file, Guid moduleId, string type, Guid guid, int age, IReadOnlyList<string> checksums)
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
        public IReadOnlyList<string> Checksums { get; init; }

        public override string ToString() =>
            $"{nameof(File)}: {File}" +
            $"{nameof(ModuleId)}: {ModuleId}, " +
            $"{nameof(IsPortable)}: {IsPortable}, " +
            $"{nameof(Type)}: {Type}, " +
            $"{nameof(Guid)}: {Guid}, " +
            $"{nameof(Age)}: {Age}, " +
            $"{nameof(Checksums)}: {Environment.NewLine}{string.Join(Environment.NewLine, Checksums)}";
    }
}