using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Simbolo;
using Simbolo.Backend;

class Program
{
    private static void Main(string[] args)
    {
        
        File.Move(Path.Combine(Directory.GetCurrentDirectory(), "Simbolo.pdb"), "simbolo123123.nope", true);
        var symbolicate = args.Any(a => a == "--symbolicate");
        var verbose = args.Any(a => a == "--verbose");
        var raw = args.Any(a => a == "--raw");
        var frames = new List<FrameInfo>();
        try
        {
            _ = new Example();
        }
        catch (Exception e)
        {
            var info = Client.GetStackTraceInformation(e);
            Console.WriteLine("Custom:");
            Console.WriteLine(info.ToString());
            Console.WriteLine("Original:");
            Console.WriteLine(e.StackTrace);

            using var symbolicateStackTrace = new SymbolicateStackTrace(new SymbolicateOptions
            {
                SymbolsPath = "."
            });

            var symbolicated = symbolicateStackTrace.Symbolicate(info);
            Console.WriteLine("Symbolicated:");
            Console.WriteLine(symbolicated.ToString());

            var path = typeof(Program).Assembly.Location;
            foreach (var frame in new StackTrace(e, true).GetFrames())
            {
                var method = frame.GetMethod();
                if (method is null)
                {
                    // Log this out?!
                    continue;
                }
                var frameInfo = new FrameInfo
                {
                    Mvid = method.Module.ModuleVersionId,
                    Method = method.Name,
                    ILOffset = frame.GetILOffset(),
                };

                frames.Add(frameInfo);

                if (raw)
                {
                    Console.WriteLine($"{path} {frameInfo.Method} {frameInfo.Mvid} {frameInfo.ILOffset}");
                }

                if (verbose)
                {
                    PrintFrame(frame);
                }
            }

            if (verbose)
            {
                Console.WriteLine($"Exception.ToString:\n{e}");
            }

            if (symbolicate)
            {
                Console.WriteLine($"Symbolicating frames with symbol path: {path}");
                foreach (var location in Symbolicate.SymbolicateFrames(path, frames))
                {
                    Console.WriteLine(location);
                }
            }
        }
    }

    private static void PrintFrame(StackFrame frame)
    {
        Console.WriteLine();

        Console.Write($"ToString: {frame}");
        Console.WriteLine($"GetNativeOffset: {frame.GetNativeOffset()}");
        Console.WriteLine($"GetFileColumnNumber: {frame.GetFileColumnNumber()}");
        Console.WriteLine($"GetFileLineNumber: {frame.GetFileLineNumber()}");
        Console.WriteLine($"HasSource: {frame.HasSource()}");
        Console.WriteLine($"HasILOffset: {frame.HasILOffset()}");
        if (frame.HasILOffset())
        {
            Console.WriteLine($"GetILOffset: {frame.GetILOffset()}");
        }

        Console.WriteLine($"HasNativeImage: {frame.HasNativeImage()}");
        if (frame.HasNativeImage())
        {
            Console.WriteLine($"GetNativeOffset: {frame.GetNativeOffset()}");
            Console.WriteLine($"GetNativeImageBase: {frame.GetNativeImageBase()}");
            Console.WriteLine($"GetNativeIP: {frame.GetNativeIP()}");
        }

        Console.WriteLine();
    }
}