using System;
using System.Collections.Generic;
using System.Linq;

namespace Simbolo
{
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