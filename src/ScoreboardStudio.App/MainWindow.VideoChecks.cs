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
            await Until(()=>fields.Count>0 && engine.Preview.Fields.Count==fields.Count,"Initial automatic fields were not created.");
            Require(fields.All(f=>f.OutputRegion is not null),"Detected fields were not automatically placed on the combined output.");
            var initialOutputPositions=fields.Select(f=>f.OutputRegion).ToArray();await Task.Delay(500);
            Require(fields.Select(f=>f.OutputRegion).SequenceEqual(initialOutputPositions),"Playback rearranged initial output positions.");
            if(Arg("--expect-auto-fields") is {} expected)Require(fields.Count==int.Parse(expected),$"Expected {expected} automatic fields, got {fields.Count}.");
            RefreshPreview();UpdateLayout();CaptureWindow(System.IO.Path.Combine(directory,"automatic-fields.png"));
            report.Add($"PASS: first video load created {fields.Count} initial fields and output positions automatically; playback preserved placement.");
            if(Arg("--test-layout") is {} correctionLayout && Layout.Load(correctionLayout).Perspective is {Enabled:true} rightBoard)
            {
                video!.TogglePause();await Task.Delay(300);
                var originalFields=fields.Select(f=>f.Snapshot()).ToArray();
                // Exercise the same wide correction as the reported screenshots, plus
                // a canvas matching the scoreboard proportions.
                foreach(var size in new[]{(1280,720),(480,500)})
                {
                    PerspectiveClick(this,new());perspectiveDraft=rightBoard with {};
                    PerspectiveWidth.Text=size.Item1.ToString();PerspectiveHeight.Text=size.Item2.ToString();
                    ApplyPerspectiveClick(this,new());
                    Require(perspectiveDetectionPending,"Correction did not start its one-time rescan.");
                    OutputClick(this,new());Require(!output,"Outputs started with stale perspective boxes.");
                    await perspectiveDetectionTask;
                    await Task.Delay(300);RefreshPreview();UpdateLayout();CaptureWindow(Path.Combine(directory,$"corrected-{size.Item1}.png"));
                    Require(fields.Count==2,$"Corrected one-board view should have two fields; found {fields.Count} at {size}: "+string.Join("; ",fields.Select(f=>f.Region.ToString())));
                    Require(fields[0].Region.Y<.4&&fields[1].Region.Y>.35,"Corrected clock/shot-clock positions are wrong: "+string.Join("; ",fields.Select(f=>f.Region.ToString())));
                    foreach(var corrected in fields)
                    {
                        var retained=originalFields.FirstOrDefault(f=>f.Id==corrected.Id);
                        Require(retained is not null&&retained.Region.X>.43,"Correction matched a field on the wrong board.");
                        Require(corrected.OutputRegion==retained!.OutputRegion&&corrected.Cleanup==retained.Cleanup&&corrected.Name==retained.Name,
                            "Correction discarded a matching field's name, cleanup or output position.");
                    }
                    var correctedRegions=fields.Select(f=>f.Region).ToArray();await Task.Delay(300);
                    Require(fields.Select(f=>f.Region).SequenceEqual(correctedRegions),"Correction kept redetecting fields.");
                    await Until(()=>engine.Preview.Fields.Count==2,"Corrected fields did not reach the video engine.");
                    RefreshPreview();UpdateLayout();CaptureWindow(Path.Combine(directory,$"corrected-{size.Item1}.png"));
                    UndoDetectionClick(this,new());
                    Require(!perspective.Enabled&&fields.Select(f=>f.Region).SequenceEqual(originalFields.Select(f=>f.Region)),"Undo correction did not restore both view and boxes.");
                }
                PerspectiveClick(this,new());perspectiveDraft=rightBoard with {};
                PerspectiveWidth.Text="1280";PerspectiveHeight.Text="720";ApplyPerspectiveClick(this,new());
                fields[0].Region=new(.01,.02,.1,.1);Changed();await perspectiveDetectionTask;
                Require(fields.Count==originalFields.Length&&fields[0].Region==new Crop(.01,.02,.1,.1),"Late correction detection overwrote a subsequent edit.");
                UndoDetectionClick(this,new());
                PerspectiveClick(this,new());perspectiveDraft=rightBoard with {};
                PerspectiveWidth.Text="1280";PerspectiveHeight.Text="720";ApplyPerspectiveClick(this,new());await perspectiveDetectionTask;
                PerspectiveClick(this,new());ResetPerspectiveClick(this,new());await perspectiveDetectionTask;
                Require(!perspective.Enabled&&fields.Count==4,"Reset correction did not redetect the full four-field view.");
                foreach(int second in new[]{8,16,25})
                {
                    video.Seek(TimeSpan.FromSeconds(second));
                    await Until(()=>Math.Abs(video.Position.TotalSeconds-second)<.15,"Paused seek failed.");
                    var scan=DetectCorrectedFields(video.Snapshot.Frame!,rightBoard);
                    Require(scan.Regions.Count==2,$"Corrected field count at {second}s was {scan.Regions.Count}.");
                    Require(scan.Regions[0].Y<.2&&scan.Regions[1].Y>.35,"Rescan did not locate both display rows.");
                }
                Require(DetectCorrectedFields(new VideoFrame(1280,720,new byte[1280*720*4]),rightBoard).Regions.Count==0,"Empty view produced phantom fields.");
                video.TogglePause();
                report.Add("PASS: correcting two boards to one redetects four fields as two at both canvas sizes and several clock values; matching names/cleanup/output positions retained; undo restores view and boxes; reset redetects four; late results cannot overwrite edits; empty view has no fields.");
            }
            if(Arg("--test-layout") is {} layoutPath) OpenLayout(layoutPath);
            var savedRegions=fields.Select(f=>f.Region).ToArray();await Task.Delay(900);
            Require(fields.Select(f=>f.Region).SequenceEqual(savedRegions),"Automatic detection overwrote a loaded layout.");
            SetReferenceClick(this,new());
            await Until(()=>engine.Preview.StabilizationStatus.StartsWith("Stabilization ready")||engine.Preview.StabilizationStatus.StartsWith("Stabilizing"),"Reference capture failed: "+engine.Preview.StabilizationStatus);
            var cameraFrame=engine.Preview.RawSource!;
            var motionTracker=new CameraStabilizer();
            Require(motionTracker.Process(cameraFrame,stabilization.Area).Healthy,"Recorded image cannot establish a tracking reference.");
            var shaken=CameraStabilizer.Warp(cameraFrame,1,0,-12,8);
            Require(motionTracker.Process(shaken,stabilization.Area).Healthy,"Recorded image could not track a simulated camera shake.");
            var measured=motionTracker.Transform;
            Require(Math.Abs(measured.X-12)<3&&Math.Abs(measured.Y+8)<3,"Recorded camera shake was not corrected accurately.");
            report.Add("PASS: simulated camera shake on the real recording recovered within three source pixels.");
            foreach(int shift in new[]{100,150,0})
            {
                var moved=CameraStabilizer.Warp(cameraFrame,1,0,-shift,0);
                var result=motionTracker.Process(moved,stabilization.Area);
                Require(result.Healthy && Math.Abs(motionTracker.Transform.X-shift)<3,$"Recorded camera did not recover a {shift}px shift: {result.Status}");
            }
            report.Add("PASS: larger recorded-image shifts and return to the original view recovered without replacing the reference.");
            int seconds=int.TryParse(Arg("--soak-seconds"),out var requested)?Math.Clamp(requested,3,600):3;
            var process=Process.GetCurrentProcess();process.Refresh();long memoryStart=process.PrivateMemorySize64;
            int healthyFrames=0,samples=0;double processingTotal=0;
            for(int sample=0;sample<seconds*5;sample++)
            {
                await Task.Delay(200);samples++;
                if(engine.Preview.Healthy && engine.Preview.TrackingHealthy)healthyFrames++;
                processingTotal+=engine.Preview.ProcessingMs;
                Require(fields.Select(f=>f.Region).SequenceEqual(savedRegions),"Playback or stabilization moved operator fields.");
            }
            process.Refresh();long memoryEnd=process.PrivateMemorySize64;
            report.Add($"Playback soak: {seconds}s, {healthyFrames}/{samples} tracked frames, processing {processingTotal/samples:0.0} ms average; private memory {memoryStart/1048576} -> {memoryEnd/1048576} MiB.");
            Require(healthyFrames>=samples*.8,"Stabilization lost the stationary recorded scoreboard too often: "+engine.Preview.StabilizationStatus);
            Require(memoryEnd-memoryStart<250L*1024*1024,"Playback memory grew by more than 250 MiB during the soak.");
            StabilizeClick(this,new());
            await Task.Delay(300);
            await Until(()=>engine.Preview.Fields.Count==fields.Count,"Video crops were not produced.");
            var previous=Hash(engine.Preview.Fields[fields[0].Id]);
            await Until(()=>Hash(engine.Preview.Fields[fields[0].Id])!=previous,"Cleaned video did not update.");
            report.Add("PASS: direct file decoding and moving cleaned fields.");
            WorkspaceTabs.SelectedIndex=1;ComparisonSection.IsExpanded=true; await Task.Delay(300);RefreshPreview();UpdateLayout();
            Require(OriginalPreview.Source is not null&&ReferenceEquals(CleanPreview.Tag,engine.Preview.Fields[Selected!.Id]),"Live comparison did not show the selected cleaned frame.");
            var comparisonFrame=CleanPreview.Tag;await Task.Delay(250);RefreshPreview();
            Require(!ReferenceEquals(comparisonFrame,CleanPreview.Tag),"Live comparison stopped updating during playback.");
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
            Require(fields.Select(f=>f.Region).SequenceEqual(savedRegions),"Playback, restart or loop changed field placement.");

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
                outputSettings=outputSettings with {Mode="Individual"};SyncComposition();
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
