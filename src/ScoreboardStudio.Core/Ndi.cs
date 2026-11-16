using System.Reflection;
using System.Runtime.InteropServices;

namespace ScoreboardStudio.Core;

public sealed record NdiSource(string Name, string Address)
{
    public override string ToString() => Name;
}

public static class NdiRuntime
{
    private static readonly object Gate = new();
    private static bool attempted;
    public static bool Available { get; private set; }
    public static string Status { get; private set; } = "NDI has not been initialized";
    public static string? LibraryPath { get; private set; }

    public static bool Initialize()
    {
        lock (Gate)
        {
            if (attempted) return Available;
            attempted = true;
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) { Status = "NDI requires 64-bit Windows."; return false; }
            var candidates = new List<string>();
            foreach (string variable in new[] { "SCOREBOARD_NDI_RUNTIME", "NDI_RUNTIME_DIR_V6", "NDI_RUNTIME_DIR_V5" })
            {
                string? location = Environment.GetEnvironmentVariable(variable);
                if (string.IsNullOrWhiteSpace(location)) continue;
                if (File.Exists(location)) candidates.Add(location);
                else { candidates.Add(Path.Combine(location, "Processing.NDI.Lib.x64.dll")); candidates.Add(Path.Combine(location, "Processing.NDI.Lib.dll")); }
            }
            foreach (string folder in new[] { AppContext.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", "NDI 6 Runtime", "v6"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", "NDI 6 Runtime"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NDI", "NDI 5 Runtime", "v5"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "vMix", "ndi", "x64") })
            { candidates.Add(Path.Combine(folder, "Processing.NDI.Lib.x64.dll")); candidates.Add(Path.Combine(folder, "Processing.NDI.Lib.dll")); }
            foreach (var path in candidates.Distinct())
            {
                if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library)) continue;
                string[] required = ["NDIlib_initialize", "NDIlib_find_create_v2", "NDIlib_find_get_current_sources", "NDIlib_find_destroy", "NDIlib_recv_create_v3", "NDIlib_recv_capture_v2", "NDIlib_recv_free_video_v2", "NDIlib_recv_destroy", "NDIlib_send_create", "NDIlib_send_send_video_v2", "NDIlib_send_destroy"];
                if (required.Any(name => !NativeLibrary.TryGetExport(library, name, out _))) { NativeLibrary.Free(library); continue; }
                NativeLibrary.SetDllImportResolver(typeof(NdiRuntime).Assembly, (name, _, _) => name == Native.Library ? library : IntPtr.Zero);
                LibraryPath = path;
                Available = Native.Initialize();
                Status = Available ? "NDI ready" : "NDI could not initialize on this machine.";
                return Available;
            }
            Status = "NDI runtime not found. Image setup is available. Install the NDI runtime, or set SCOREBOARD_NDI_RUNTIME to its DLL path and restart.";
            return false;
        }
    }

    internal static void Require() { if (!Initialize()) throw new InvalidOperationException(Status); }
}

public sealed class NdiDiscovery : IDisposable
{
    private IntPtr handle;
    public NdiDiscovery()
    {
        NdiRuntime.Require();
        var settings = new Native.FindSettings { ShowLocal = true };
        handle = Native.FindCreate(ref settings);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("NDI source discovery could not start.");
    }
    public List<NdiSource> GetSources()
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
        var ptr = Native.FindSources(handle, out uint count);
        var result = new List<NdiSource>();
        for (int i = 0; i < Math.Min(count, 1000); i++)
        {
            var source = Marshal.PtrToStructure<Native.Source>(ptr + i * Marshal.SizeOf<Native.Source>());
            result.Add(new(Marshal.PtrToStringUTF8(source.Name) ?? "Unnamed source", Marshal.PtrToStringUTF8(source.Address) ?? ""));
        }
        return result;
    }
    public void Dispose() { if (handle != IntPtr.Zero) { Native.FindDestroy(handle); handle = IntPtr.Zero; } }
}

public sealed class NdiReceiver : IDisposable
{
    private IntPtr handle;
    public NdiReceiver(NdiSource source)
    {
        NdiRuntime.Require();
        using var name = new Utf8(source.Name);
        using var address = new Utf8(source.Address);
        using var receiverName = new Utf8("Scoreboard Studio receiver");
        var settings = new Native.ReceiveSettings { Source = new() { Name = name.Pointer, Address = address.Pointer }, ColorFormat = 0, Bandwidth = 100, AllowFields = false, Name = receiverName.Pointer };
        handle = Native.ReceiveCreate(ref settings);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Could not connect to this NDI source.");
    }
    public VideoFrame? Capture(uint timeout = 100)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
        Native.Video video = default;
        int type = Native.Capture(handle, ref video, IntPtr.Zero, IntPtr.Zero, timeout);
        if (type == 4) throw new IOException("The NDI receiver reported a connection error.");
        if (type != 1) return null;
        try
        {
            if (video.Width < 1 || video.Height < 1 || video.Width > 8192 || video.Height > 4320 || video.Data == IntPtr.Zero) throw new InvalidDataException("Unsupported source dimensions.");
            if (video.FourCC != Native.Bgra && video.FourCC != Native.Bgrx) throw new InvalidDataException("The NDI source did not return a supported BGRA frame.");
            int stride = video.Stride == 0 ? video.Width * 4 : video.Stride;
            if (Math.Abs((long)stride) < video.Width * 4) throw new InvalidDataException("Invalid NDI row size.");
            var pixels = new byte[video.Width * video.Height * 4];
            for (int y = 0; y < video.Height; y++) Marshal.Copy(video.Data + y * stride, pixels, y * video.Width * 4, video.Width * 4);
            if (video.FourCC == Native.Bgrx) for (int p = 3; p < pixels.Length; p += 4) pixels[p] = 255;
            return new(video.Width, video.Height, pixels);
        }
        finally { Native.FreeVideo(handle, ref video); }
    }
    public void Dispose() { if (handle != IntPtr.Zero) { Native.ReceiveDestroy(handle); handle = IntPtr.Zero; } }
}

public sealed class NdiSender : IDisposable
{
    private IntPtr handle;
    public string Name { get; }
    public NdiSender(string name)
    {
        NdiRuntime.Require(); Name = name;
        using var namePtr = new Utf8(name);
        var settings = new Native.SendSettings { Name = namePtr.Pointer, ClockVideo = false, ClockAudio = false };
        handle = Native.SendCreate(ref settings);
        if (handle == IntPtr.Zero) throw new InvalidOperationException($"Could not start NDI output: {name}");
    }
    public void Send(VideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
        if (frame.Width % 2 != 0) throw new ArgumentException("NDI output width must be even.");
        var pinned = GCHandle.Alloc(frame.Pixels, GCHandleType.Pinned);
        try
        {
            var video = new Native.Video { Width = frame.Width, Height = frame.Height, FourCC = Native.Bgra, RateN = 30, RateD = 1, Aspect = (float)frame.Width / frame.Height, Format = 1, Timecode = long.MaxValue, Data = pinned.AddrOfPinnedObject(), Stride = frame.Stride };
            // Synchronous API: the native library is finished with this buffer on return.
            Native.SendVideo(handle, ref video);
        }
        finally { pinned.Free(); }
    }
    public void Dispose() { if (handle != IntPtr.Zero) { Native.SendDestroy(handle); handle = IntPtr.Zero; } }
}

internal sealed class Utf8(string text) : IDisposable
{
    public IntPtr Pointer { get; } = Marshal.StringToCoTaskMemUTF8(text);
    public void Dispose() => Marshal.FreeCoTaskMem(Pointer);
}

internal static class Native
{
    internal const string Library = "Scoreboard.NDI";
    internal const uint Bgra = 0x41524742, Bgrx = 0x58524742;
    [StructLayout(LayoutKind.Sequential)] internal struct Source { public IntPtr Name, Address; }
    [StructLayout(LayoutKind.Sequential)] internal struct FindSettings { [MarshalAs(UnmanagedType.I1)] public bool ShowLocal; public IntPtr Groups, ExtraIps; }
    [StructLayout(LayoutKind.Sequential)] internal struct ReceiveSettings { public Source Source; public int ColorFormat, Bandwidth; [MarshalAs(UnmanagedType.I1)] public bool AllowFields; public IntPtr Name; }
    [StructLayout(LayoutKind.Sequential)] internal struct SendSettings { public IntPtr Name, Groups; [MarshalAs(UnmanagedType.I1)] public bool ClockVideo; [MarshalAs(UnmanagedType.I1)] public bool ClockAudio; }
    [StructLayout(LayoutKind.Sequential)] internal struct Video { public int Width, Height; public uint FourCC; public int RateN, RateD; public float Aspect; public int Format; public long Timecode; public IntPtr Data; public int Stride; public IntPtr Metadata; public long Timestamp; }
    [DllImport(Library, EntryPoint="NDIlib_initialize", CallingConvention=CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool Initialize();
    [DllImport(Library, EntryPoint="NDIlib_find_create_v2", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr FindCreate(ref FindSettings settings);
    [DllImport(Library, EntryPoint="NDIlib_find_get_current_sources", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr FindSources(IntPtr handle, out uint count);
    [DllImport(Library, EntryPoint="NDIlib_find_destroy", CallingConvention=CallingConvention.Cdecl)] internal static extern void FindDestroy(IntPtr handle);
    [DllImport(Library, EntryPoint="NDIlib_recv_create_v3", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr ReceiveCreate(ref ReceiveSettings settings);
    [DllImport(Library, EntryPoint="NDIlib_recv_capture_v2", CallingConvention=CallingConvention.Cdecl)] internal static extern int Capture(IntPtr handle, ref Video video, IntPtr audio, IntPtr metadata, uint timeout);
    [DllImport(Library, EntryPoint="NDIlib_recv_free_video_v2", CallingConvention=CallingConvention.Cdecl)] internal static extern void FreeVideo(IntPtr handle, ref Video video);
    [DllImport(Library, EntryPoint="NDIlib_recv_destroy", CallingConvention=CallingConvention.Cdecl)] internal static extern void ReceiveDestroy(IntPtr handle);
    [DllImport(Library, EntryPoint="NDIlib_send_create", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr SendCreate(ref SendSettings settings);
    [DllImport(Library, EntryPoint="NDIlib_send_send_video_v2", CallingConvention=CallingConvention.Cdecl)] internal static extern void SendVideo(IntPtr handle, ref Video video);
    [DllImport(Library, EntryPoint="NDIlib_send_destroy", CallingConvention=CallingConvention.Cdecl)] internal static extern void SendDestroy(IntPtr handle);
}
