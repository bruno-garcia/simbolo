using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Pdb;
using SequencePoint = Mono.Cecil.Cil.SequencePoint;

namespace Simbolo
{
    public static class Symbolicate
    {
        // symbolsPath to be replaced by a mvid -> path lookup
        // symbols should be cached in memory with LRU with mvid as key
        public static Location? SymbolicateFrame(string symbolsPath, FrameInfo frame)
        {
            var path = symbolsPath;
            var rp = new ReaderParameters
            {
                ReadSymbols = true,
                SymbolReaderProvider = new PdbReaderProvider()
            };

            var assemblyDefinition = Mono.Cecil.AssemblyDefinition.ReadAssembly(path, rp);
            var module = assemblyDefinition.Modules.FirstOrDefault(m => m.Mvid == frame.Mvid);
            if (module is null)
            {
                Console.WriteLine($"Can't find a module with id {frame.Mvid} in {assemblyDefinition.Name}");
                return null;
            }
            if (!module.HasSymbols)
            {
                Console.WriteLine($"Module {module.Mvid} does not have symbols");
                return null;
            }

            // TODO: Account for sub types
            var method = module.Types.SelectMany(t => t.Methods).FirstOrDefault(m => m.Name == frame.Method);
            if (method is null)
            {
                return null;
            }
            // TODO: Check signature to find overload

            SequencePoint sequencePoint = null!;
            foreach (var sp in method
                .DebugInformation
                .SequencePoints
                .OrderBy(s => s.Offset))
            {
                sequencePoint = sp;
                if (sp.Offset >= frame.ILOffset)
                {
                    break;
                }
            }

            if (sequencePoint is null)
            {
                throw new InvalidOperationException("No sequence point was found for offset: " + frame.ILOffset);
            }
            var location = new Location(
                sequencePoint.Document.Url,
                sequencePoint.StartLine,
                sequencePoint.StartColumn);

            return location;
        }

        public static IEnumerable<Location?> SymbolicateFrames(string symbolsPath, IEnumerable<FrameInfo> frames)
            => frames.Select(f => SymbolicateFrame(symbolsPath, f));
    }

    public readonly struct Location
    {
        public string File { get; }
        public int Line { get; }
        public int Column { get; }

        public Location(string file, int line, int column)
        {
            File = file;
            Line = line;
            Column = column;
        }

        public override string ToString() => $"{File} {Line}:{Column}";
    }

    public class FrameInfo
    {
        public Guid Mvid { get; set; }
        public string? Method { get; set; }
        public int ILOffset { get; set; }
    }
}

