using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    // Explicit verification entry point. No fixture is shipped or available
    // from the user interface: the caller supplies the video and optional layout.
    private async Task VideoSmokeTest(string directory)
    {
        var report = new List<string>();
        try
        {
            Directory.CreateDirectory(directory);
            var args = Environment.GetCommandLineArgs();
            string? Arg(string key) { int i=Array.IndexOf(args,key); return i>=0 && i+1<args.Length ? args[i+1] : null; }
            async Task Until(Func<bool> check, string message, double seconds=12)
            {
                var clock=Stopwatch.StartNew();
                while(clock.Elapsed.TotalSeconds<seconds) { if(check())return; await Task.Delay(50); }
                throw new Exception(message);
            }
            void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
            string Hash(VideoFrame f) => Convert.ToHexString(SHA256.HashData(f.Pixels));
            string path=Arg("--video-file") ?? throw new ArgumentException("Supply --video-file.");
            LoadMedia(path);
            await Until(()=>engine.Preview.Healthy && engine.Preview.Source is not null,"Video did not open: "+video?.Snapshot.Status);
            Require(kind=="Video" && VideoControls.Visibility==Visibility.Visible && !output,"Video did not load with outputs stopped.");
            var initial=engine.Preview.RawSource!;
            Require(initial.Width<=1280 && initial.Height<=720,"Video exceeded the processing size limit.");
            await Task.Delay(1000);
            Require(engine.Preview.RawSource!.Pixels.Where((_,i)=>i%4!=3).Average(b=>(double)b)>5,"Decoded video was black.");
            await Until(()=>fields.Count>0 && engine.Preview.Fields.Count==fields.Count,"Loading video did not automatically create usable fields.");
            if(Arg("--expect-auto-fields") is {} expected)Require(fields.Count==int.Parse(expected),$"Expected {expected} automatic fields, got {fields.Count}.");
            RefreshPreview();UpdateLayout();CaptureWindow(System.IO.Path.Combine(directory,"automatic-fields.png"));
            report.Add($"PASS: video load automatically created {fields.Count} editable fields without a detection click.");
            if(Arg("--test-layout") is {} layoutPath) OpenLayout(layoutPath);
            var savedRegions=fields.Select(f=>f.Region).ToArray();await Task.Delay(900);
            Require(fields.Select(f=>f.Region).SequenceEqual(savedRegions),"Automatic detection overwrote a loaded layout.");
            await Until(()=>engine.Preview.Fields.Count==fields.Count,"Video crops were not produced.");
            var previous=Hash(engine.Preview.Fields[fields[0].Id]);
            await Until(()=>Hash(engine.Preview.Fields[fields[0].Id])!=previous,"Cleaned video did not update.");
            report.Add("PASS: direct file decoding and moving cleaned fields.");
            WorkspaceTabs.SelectedIndex=1; await Task.Delay(300);RefreshPreview();UpdateLayout();
            CaptureWindow(System.IO.Path.Combine(directory,"video-cleanup.png"));
            WorkspaceTabs.SelectedIndex=0;RefreshPreview();UpdateLayout();
            CaptureWindow(System.IO.Path.Combine(directory,"video-fields.png"));

            video!.TogglePause(); await Task.Delay(400);
            var pausedPosition=video.Position;var pausedHash=Hash(video.Snapshot.Frame!);
            await Task.Delay(500);
            Require(!video.IsPlaying && (video.Position-pausedPosition).Duration().TotalMilliseconds<100 && Hash(video.Snapshot.Frame!)==pausedHash,"Pause did not hold the frame.");
            video.TogglePause(); await Until(()=>video.Position>pausedPosition+TimeSpan.FromMilliseconds(400),"Resume did not advance video.");
            video.Restart();await Until(()=>video.Position.TotalSeconds<1,"Restart did not return to the beginning.");
            report.Add("PASS: pause, resume and restart.");

            video.Loop=false;video.Seek(video.Duration-TimeSpan.FromMilliseconds(650));
            await Until(()=>!video.IsPlaying && video.Snapshot.Status.Contains("Ended"),"Playback did not stop at the end.");
            video.Loop=true;video.Restart();video.Seek(video.Duration-TimeSpan.FromMilliseconds(650));
            await Until(()=>video.LoopsCompleted>0 && video.Position.TotalSeconds<3,"Playback did not loop.");
            report.Add("PASS: end-of-file and automatic loop.");

            savePath=System.IO.Path.Combine(directory,"video-roundtrip.scoreboard.json");
            Require(SaveSetup(),"Video layout did not save.");
            var saved=Layout.Load(savePath);
            Require(saved.VideoPath is not null && saved.ImagePath is null,"Saved video path was incorrect.");
            OpenLayout(savePath);
            await Until(()=>engine.Preview.Healthy && video?.Snapshot.Frame is not null,"Saved video layout did not reopen.");
            Require(!output && kind=="Video","Saved video layout started outputs or lost its source.");
            report.Add("PASS: video path and crop settings restored from saved layout.");

            LiveClick(this,new());Require(!SourceControls.IsEnabled,"Live lock did not disable playback controls.");
            LiveClick(this,new());
            if(args.Contains("--ndi"))
            {
                Require(NdiRuntime.Available,"NDI runtime unavailable for requested test.");
                foreach(var field in fields)field.Name="Video check "+Guid.NewGuid().ToString("N")[..8];
                SyncFields();PushPlan();OutputClick(this,new());
                await Until(()=>engine.Preview.OutputCount==fields.Count,"File playback did not produce NDI outputs.");
                using var finder=new NdiDiscovery(); NdiSource? found=null;
                await Until(()=> (found=finder.GetSources().FirstOrDefault(s=>s.Name.Contains(VideoEngine.OutputName("Video",fields[0].Name)))) is not null,"File output not discovered.");
                using var receiver=new NdiReceiver(found!);VideoFrame? received=null;
                await Until(()=> (received=receiver.Capture(10)) is not null,"File output not received.");
                string first=Hash(received!);bool alpha=false;
                await Until(()=>{var f=receiver.Capture(10);if(f is null)return false;alpha=f.Pixels.Where((_,i)=>i%4==3).Any(a=>a<10)&&f.Pixels.Where((_,i)=>i%4==3).Any(a=>a>240);return Hash(f)!=first&&alpha;},"NDI video did not move or retain transparency.");
                string bad=System.IO.Path.Combine(directory,"invalid.mp4");File.WriteAllText(bad,"invalid video fixture");
                LoadMedia(bad);
                Require(!output,"Changing media did not stop NDI outputs.");
                await Until(()=>video!.Snapshot.Status.Contains("Cannot play") || video.Snapshot.Status.Contains("could not open"),"Invalid media did not report an error.",20);
                await Until(()=>!engine.Preview.Healthy&&engine.Preview.Source is null&&engine.Preview.OutputCount==0,"Invalid media retained old frames or outputs.");
                report.Add("PASS: file-to-NDI motion and transparency; source change stops outputs; invalid video clears stale frames.");
            }
            LoadMedia(path);
            await Until(()=>video?.Snapshot.Frame is not null,"Video did not reopen after a source change.");
            var held=video!;SwitchSource("Image",image:held.Snapshot.Frame);
            Require(video is null && VideoControls.Visibility==Visibility.Collapsed && held.Snapshot.Frame is null,"Switching to an image did not release video.");
            report.Add("PASS: switching sources releases video playback.");
            File.WriteAllLines(System.IO.Path.Combine(directory,"video-checks.txt"),report);
        }
        catch(Exception ex) { File.WriteAllText(System.IO.Path.Combine(directory,"video-error.txt"),ex.ToString());Environment.ExitCode=1; }
        finally {dirty=false;Close();}
    }
}
