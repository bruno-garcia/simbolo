using System;

namespace Simbolo.StackFrameData
{
    public record StackFrameInformation
    {
        public int? MethodIndex { get; }
        public int? Offset { get; }
        public bool? IsILOffset { get; }
        public Guid? Mvid { get; }
        public string? Aotid { get; }
        public string? FileName { get; }
        public string? Method { get; }
        // "package"
        public string? AssemblyFullName { get; }
        // "module"
        public string? TypeFullName { get; }
        public int? LineNumber { get; }
        public int? ColumnNumber { get; }

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
            int? columnNumber)
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
            $"{nameof(ColumnNumber)}: {ColumnNumber}";
    }
}