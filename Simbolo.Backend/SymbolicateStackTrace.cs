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
        public bool AttemptOriginalSymbolPath { get; set; } = true;
    }

    public class SymbolicateStackTrace : IDisposable
    {
        private readonly SymbolicateOptions _options;
        private readonly Dictionary<Guid, (MetadataReaderProvider provider, MetadataReader reader)?> _readerCache = new();

        public SymbolicateStackTrace(SymbolicateOptions options) => _options = options;

        public StackTraceInformation Symbolicate(StackTraceInformation info)
        {
            var newFrames = new List<StackFrameInformation>();
            foreach (var stackFrameInformation in info.StackFrameInformation)
            {
                if (stackFrameInformation.LineNumber is null 
                    && stackFrameInformation.Mvid.HasValue &&
                    info.DebugMetas.TryGetValue(stackFrameInformation.Mvid.Value, out var debugMeta))
                {
                    newFrames.Add(Symbolicate(stackFrameInformation, debugMeta));
                } 
                else
                {
                    newFrames.Add(stackFrameInformation);    
                }
            }

            return new StackTraceInformation(newFrames, info.DebugMetas);
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
                if (_options.SymbolsPath is { } path)
                {
                    yield return Path.Combine(
                        path,
                        debugMeta.ModuleId.ToString("n"),
                        Path.ChangeExtension(Path.GetFileName(debugMeta.File),
                            "pdb"));
                }

                if (_options.AttemptOriginalSymbolPath)
                {
                    yield return debugMeta.File;
                }
            }

            foreach (var probingPath in GetProbingPaths())
            {
                Stream? SafeGetFileStream()
                {
                    try
                    {
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

            Debug.Assert(methodToken.Kind == HandleKind.MethodDefinition);

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