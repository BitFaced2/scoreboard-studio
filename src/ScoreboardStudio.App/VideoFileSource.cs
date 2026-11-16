using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

// Windows media decoding stays on the dispatcher; the processing worker reads
// immutable snapshots and never touches MediaPlayer or WPF objects.
internal sealed class VideoFileSource : IPlaybackSource, IDisposable
{
    private readonly MediaPlayer player = new() { IsMuted = true, Volume = 0 };
    private readonly DrawingVisual visual = new();
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    private PlaybackSnapshot snapshot = new(null, "Opening video", false);
    private bool disposed, opened, ended, failed;
    private readonly System.Diagnostics.Stopwatch opening = System.Diagnostics.Stopwatch.StartNew();
    private int width, height;
    public PlaybackSnapshot Snapshot => Volatile.Read(ref snapshot);
    public bool IsPlaying { get; private set; }
    public bool Loop { get; set; } = true;
    public TimeSpan Position => opened ? player.Position : TimeSpan.Zero;
    public TimeSpan Duration => opened && player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan : TimeSpan.Zero;
    public int LoopsCompleted { get; private set; }

    public VideoFileSource(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Video file not found.", path);
        player.MediaOpened += Opened;
        player.MediaFailed += Failed;
        player.MediaEnded += Ended;
        timer.Tick += Tick;
        player.Open(new Uri(Path.GetFullPath(path)));
        player.Play();
        timer.Start();
    }
    private void Opened(object? sender, EventArgs e)
    {
        if (disposed || failed) return;
        if (!player.HasVideo || player.NaturalVideoWidth < 1 || player.NaturalVideoHeight < 1)
        { Fail("This file has no supported video track."); return; }
        double scale = Math.Min(1, Math.Min(1280.0/player.NaturalVideoWidth,720.0/player.NaturalVideoHeight));
        width = Math.Max(2,(int)Math.Round(player.NaturalVideoWidth*scale));
        height = Math.Max(2,(int)Math.Round(player.NaturalVideoHeight*scale));
        using (var dc = visual.RenderOpen()) { dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,width,height)); dc.DrawVideo(player,new Rect(0,0,width,height)); }
        opened = true; IsPlaying = true; timer.Start();
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (disposed) return;
        if (!opened)
        {
            if (!failed && opening.Elapsed.TotalSeconds > 15) Fail("Video could not open. Try an H.264 MP4 supported by Windows.");
            return;
        }
        try
        {
            var bitmap = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[width*height*4]; bitmap.CopyPixels(pixels,width*4,0);
            // The drawing has an opaque black base, so its premultiplied pixels
            // are also valid straight BGRA pixels for the processing pipeline.
            Volatile.Write(ref snapshot,new(new(width,height,pixels),Status(),true));
        }
        catch (Exception ex) { Fail("Could not decode video: " + ex.Message); }
    }
    private string Status() => "VIDEO • " + (IsPlaying ? "Playing" : ended ? "Ended" : "Paused");
    private void UpdateStatus() => Volatile.Write(ref snapshot,Snapshot with { Status = Status() });
    public void TogglePause()
    {
        if (!opened || disposed) return;
        if (IsPlaying) { player.Pause(); IsPlaying = false; }
        else { if (ended) player.Position = TimeSpan.Zero; ended=false; player.Play(); IsPlaying=true; }
        UpdateStatus();
    }
    public void Restart()
    {
        if (!opened || disposed) return;
        player.Position=TimeSpan.Zero; ended=false; player.Play(); IsPlaying=true; UpdateStatus();
    }
    public void Seek(TimeSpan position)
    {
        if (!opened || disposed) return;
        player.Position=TimeSpan.FromTicks(Math.Clamp(position.Ticks,0,Duration.Ticks)); ended=false; UpdateStatus();
    }
    private void Ended(object? sender, EventArgs e)
    {
        if (disposed) return;
        if (Loop) { LoopsCompleted++; Restart(); }
        else { player.Pause(); IsPlaying=false; ended=true; UpdateStatus(); }
    }
    private void Failed(object? sender, ExceptionEventArgs e) => Fail("Cannot play this video. Try an H.264 MP4 supported by Windows. " + e.ErrorException.Message);
    private void Fail(string message)
    {
        failed=true; timer.Stop(); opened=false; IsPlaying=false; player.Close();
        Volatile.Write(ref snapshot,new(null,message,false));
    }
    public void Dispose()
    {
        if(disposed) return;
        disposed=true; timer.Stop(); timer.Tick-=Tick;
        player.MediaOpened-=Opened; player.MediaFailed-=Failed; player.MediaEnded-=Ended;
        player.Close(); IsPlaying=false; opened=false;
        Volatile.Write(ref snapshot,new(null,"Video closed",false));
    }
}
