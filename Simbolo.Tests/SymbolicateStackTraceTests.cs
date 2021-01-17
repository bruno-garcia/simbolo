using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Simbolo.Backend;
using Simbolo.StackFrameData;
using Xunit;

namespace Simbolo.Tests
{
    public class SymbolicateStackTraceTests
    {
        private class Fixture
        {
            public SymbolicateOptions Options { get; set; } = new();

            public SymbolicateStackTrace GetSut() => new(Options);
        }

        private readonly Fixture _fixture = new();

        [Fact]
        public void IntegrationTestWithExampleStackTrace()
        {
            // Hide the PDB so .NET can't find line numbers and file paths:
            var targetPdb = GetType().Assembly.Location.Replace(".Tests.dll", ".pdb");
            var pdbPath = Path.GetTempPath();
            File.Move(targetPdb, Path.Combine(pdbPath, Path.GetFileName(targetPdb)), true);

            StackTraceInformation info = null!;
            try
            {
                _ = new Example();
            }
            catch (Exception e)
            {
                info = Client.GetStackTraceInformation(e);
            }
            
            // The Simbolo.dll frames and also System.Private.CoreLib.pdb
            // Test lib isn't listed because line numbers are already available (pdb was found)
            Assert.Equal(2, info.DebugMetas.Count);
            AssertDebugMeta(info.DebugMetas);
            var simbolo = info.DebugMetas.Last();
            Assert.EndsWith("Simbolo.pdb", simbolo.Value.File);
            Assert.Single(simbolo.Value.Checksums);

            var corelib = info.DebugMetas.First();
            Assert.EndsWith("System.Private.CoreLib.pdb", corelib.Value.File);

            // Varies due to await Task
            Assert.True(info.StackFrameInformation.Count > 20);
            // No frame outside the test library should have line numbers
            Assert.Empty(info.StackFrameInformation.Where(i =>
                !i.Method.Contains($"{nameof(SymbolicateStackTraceTests)}")
                && i.LineNumber is not null
                && i.ColumnNumber is not null
                && i.FileName is not null));
            
            // Symbolicate the frames:
            
            // Test symbolication
            _fixture.Options.SymbolsPath = pdbPath;

            // With AttemptOriginalSymbolPath true, it would find it because the pdb we moved in the executing
            // directory is a copy of the actual pdb which is still in the Simbolo/bin directory,
            // and the path in the DebugMeta would lead us to find it.
            _fixture.Options.AttemptOriginalSymbolPath = false;
            var sut = _fixture.GetSut();
            sut.Symbolicate(info);

            var simboloFrames = info.StackFrameInformation.Where(f => f.Mvid == simbolo.Key).ToArray();
            // number of frames will change only if Example.cs changes 
            const int exampleFrames = 24;
            Assert.Equal(exampleFrames, simboloFrames.Length);
            Assert.Equal(exampleFrames, simboloFrames.Count(f => f.LineNumber is not null));
            Assert.Equal(exampleFrames, simboloFrames.Count(f => f.ColumnNumber is not null));
            Assert.Equal(exampleFrames, simboloFrames.Count(f => f.FileName is not null));
        }

        [Fact]
        public void Symbolicate_NoFrames_ReturnsEmptyInformation()
        {
            var sut = _fixture.GetSut();
            var frames = new StackTraceInformation(new List<StackFrameInformation>(),new Dictionary<Guid, DebugMeta>());
            sut.Symbolicate(frames);
            Assert.Empty(frames.DebugMetas);
            Assert.Empty(frames.StackFrameInformation);
        }

        private static void AssertDebugMeta(IDictionary<Guid, DebugMeta> debugMeta)
        {
            foreach (var meta in debugMeta)
            {
                // Key is mvid
                Assert.Equal(meta.Key, meta.Value.ModuleId);
                Assert.Equal("ppdb", meta.Value.Type);
                Assert.Equal(1, meta.Value.Age);
                Assert.True(meta.Value.IsPortable);
                Assert.NotEqual(Guid.Empty, meta.Value.Guid);
            }
        }
    }
}