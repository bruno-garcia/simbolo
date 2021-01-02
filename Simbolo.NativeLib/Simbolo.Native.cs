using System;
using System.Runtime.InteropServices;
using Simbolo.Backend;

namespace Simbolo.NativeLib
{
    public class NativeSymbolicate
    {
        [UnmanagedCallersOnly(EntryPoint = "symbolicate")]
        public static IntPtr SymbolicateFrame(IntPtr symbolsPath, NativeFrameInfo nativeFrameInfo)
        {
            var path = Marshal.PtrToStringAnsi(symbolsPath);
            if (path is null)
            {
                throw new InvalidOperationException("Couldn't get the path from the native pointer");
            }
            var frameInfo = nativeFrameInfo.ToFrameInfo();

            var location = Symbolicate.SymbolicateFrame(path, frameInfo);
            if (!(location is {} loc))
            {
                return default;
            }

            // TODO: Who frees this?
            var pinnedRawData = GCHandle.Alloc(
                NativeLocation.FromLocation(loc),
                GCHandleType.Pinned);

            var pinnedRawDataPtr =
                pinnedRawData.AddrOfPinnedObject();
            return pinnedRawDataPtr;
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct NativeFrameInfo
    {
        public IntPtr Mvid { get; }
        public IntPtr Method { get; }
        public int ILOffset { get; }

        public FrameInfo ToFrameInfo()
        {
            var mvid = Marshal.PtrToStringAnsi(Mvid);
            if (mvid is null)
            {
                throw new InvalidOperationException("Failed to the native pointer to string mvid.");
            }
            return new FrameInfo
            {
                Method = Marshal.PtrToStringAnsi(Method),
                Mvid = new Guid(mvid),
                ILOffset = ILOffset
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct NativeLocation
    {
        public IntPtr File { get; }
        public int Line { get; }
        public int Column { get; }

        public NativeLocation(IntPtr file, int line, int column)
        {
            File = file;
            Line = line;
            Column = column;
        }

        public static NativeLocation FromLocation(in Location location)
            => new NativeLocation(Marshal.StringToHGlobalAnsi(location.File), location.Line, location.Column);
    }
}
