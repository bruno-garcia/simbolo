using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using Simbolo.StackFrameData;

namespace Simbolo.Backend
{
    public class SymbolicateOptions
    {
        public string? SymbolsPath { get; set; }
        // TODO:
        // Lookup directories could be a bitwise enum (use symbols path, append mvid, etc)

        // Running on a server we don't want this turned on
        public bool AttemptOriginalSymbolPath { get; set; } = true;
    }

    public class SymbolicateStackTrace : IDisposable
    {
        private readonly SymbolicateOptions _options;
        private readonly Dictionary<Guid, (MetadataReaderProvider provider, MetadataReader reader)?> _readerCache = new();

        public SymbolicateStackTrace(SymbolicateOptions options) => _options = options;

        public void Symbolicate(StackTraceInformation info)
        {
            for (int i = 0; i < info.StackFrameInformation.Count; i++)
            {
                var stackFrameInformation = info.StackFrameInformation[i];
                if (stackFrameInformation.LineNumber is null 
                    && stackFrameInformation.Mvid.HasValue &&
                    info.DebugMetas.TryGetValue(stackFrameInformation.Mvid.Value, out var debugMeta))
                {
                    info.StackFrameInformation[i] = Symbolicate(stackFrameInformation, debugMeta);
                } 
                else
                {
                    info.StackFrameInformation[i] = stackFrameInformation;
                }
            }
        }

        public StackFrameInformation Symbolicate(StackFrameInformation stackFrame, DebugMeta debugMeta)
        {
            // TODO: preconditions to lookup with ppdb
            // stackFrameInformation.MethodIndex is not null 
            // stackFrameInformation.Offset is not null 

            var reader = GetMetadataReader(debugMeta);
            if (reader is not null)
            {
                stackFrame = GetNewStackFrameInformation(stackFrame, reader);
            }
            return stackFrame;
        }

        private MetadataReader? GetMetadataReader(DebugMeta debugMeta)
        {
            if (_readerCache.TryGetValue(debugMeta.ModuleId, out var cachedReader))
            {
                return cachedReader?.reader;
            }

            IEnumerable<string> GetProbingPaths()
            {
                var file = Path.ChangeExtension(debugMeta.File, "pdb");

                if (_options.SymbolsPath is { } path)
                {
                    var fileName = Path.GetFileName(file);

                    // File under a folder named after the mvid
                    yield return Path.Combine(
                        path,
                        // mono builds moves the pdb/dll under a folder named with the mvid
                        debugMeta.ModuleId.ToString("n"),
                        fileName);

                    // File directly in the symbols path
                    yield return Path.Combine(path, fileName);
                }

                if (_options.AttemptOriginalSymbolPath)
                {
                    yield return file;
                }
            }

            foreach (var probingPath in GetProbingPaths())
            {
                Stream? SafeGetFileStream()
                {
                    try
                    {
                        Console.WriteLine("probingPath: " + probingPath);
                        return File.OpenRead(probingPath);
                    }
                    catch// (Exception e)
                    {
                        // Console.WriteLine(e); // TODO:
                        return null;
                    }
                }

                var fileStream = SafeGetFileStream();
                if (fileStream is not null)
                {
                    Console.WriteLine("Opening file at: " + ((FileStream) fileStream).Name);
                    var provider = MetadataReaderProvider.FromPortablePdbStream(fileStream);
                    var reader = provider.GetMetadataReader();
                    // Standalone debug metadata image doesn't contain Module table.
                    // var mvidHandle = reader.GetModuleDefinition().Mvid;
                    // var mvid = reader.GetGuid(mvidHandle);
                    //
                    // if (mvid != debugMeta.ModuleId)
                    // {
                    //     provider.Dispose();
                    // }

                    _readerCache.Add(debugMeta.ModuleId, (provider, reader));
                    return reader;

                    // TODO: found file, but the wrong one.
                }
            }

            _readerCache.TryAdd(debugMeta.ModuleId, null); // Avoid the same lookup
            return null;
        }
        private static StackFrameInformation GetNewStackFrameInformation(
            StackFrameInformation stackFrame,
            MetadataReader reader)
        {
            var tuple = GetLineColumnFile(reader, stackFrame.MethodIndex!.Value, stackFrame!.Offset!.Value);
            if (tuple is (int l, int c, string p) a)
            {
                stackFrame = new StackFrameInformation(
                    stackFrame.Method,
                    stackFrame.MethodIndex,
                    p,
                    stackFrame.Offset,
                    stackFrame.Mvid,
                    stackFrame.IsILOffset,
                    stackFrame.Aotid,
                    stackFrame.AssemblyFullName,
                    stackFrame.TypeFullName,
                    l,
                    c);
            }

            return stackFrame;
        }

        public static (int line, int column, string file)? GetLineColumnFile(MetadataReader reader, int token, int ilOffset)
        {
            var methodToken = MetadataTokens.Handle(token);

            if (methodToken.Kind != HandleKind.MethodDefinition)
            {
                return null;
            }

            var handle = ((MethodDefinitionHandle)methodToken).ToDebugInformationHandle();
            
            if (!handle.IsNil)
            {
                var methodDebugInfo = reader.GetMethodDebugInformation(handle);
                var sequencePoints = methodDebugInfo.GetSequencePoints();
                SequencePoint? bestPointSoFar = null;

                foreach (var point in sequencePoints)
                {
                    if (point.Offset > ilOffset)
                    {
                        break;
                    }

                    if (point.StartLine != SequencePoint.HiddenLine)
                    {
                        bestPointSoFar = point;
                    }
                }

                if (bestPointSoFar.HasValue)
                {
                    return (bestPointSoFar.Value.StartLine,
                        bestPointSoFar.Value.StartColumn,
                        reader.GetString(reader.GetDocument(bestPointSoFar.Value.Document).Name));
                }
            }

            return null;
        }

        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        private void ReleaseUnmanagedResources()
        {
            foreach (var tuple in _readerCache.Values)
            {
                tuple?.provider.Dispose();
            }
        }

        ~SymbolicateStackTrace()
        {
            ReleaseUnmanagedResources();
        }
    }
}