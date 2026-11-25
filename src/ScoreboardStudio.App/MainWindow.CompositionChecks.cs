using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private async Task CompositionSmokeTest(string directory)
    {
        var report=new List<string>();
        try
        {
            Directory.CreateDirectory(directory);
            WindowState=WindowState.Normal;
            var args=Environment.GetCommandLineArgs();
            string Arg(string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:throw new ArgumentException("Supply "+name);}
            void Require(bool value,string message){if(!value)throw new Exception(message);}
            async Task Until(Func<bool> condition,string message)
            {for(int i=0;i<200;i++){if(condition())return;await Task.Delay(50);}throw new Exception(message);}
            var resizeBox=new Crop(.2,.2,.4,.4);
            Point[] resizePoints=[new(.2,.2),new(.6,.2),new(.6,.6),new(.2,.6)];
            for(int corner=0;corner<4;corner++)
            {
                Require(HitResizeCorner(resizeBox,resizePoints[corner],.02,.02)==corner,"Corner hit selection failed.");
                var resized=ResizeOrMoveBox(resizeBox,.05,.03,corner,.005,.005);
                bool left=corner is 0 or 3,top=corner is 0 or 1;
                Require(Math.Abs(resized.X-(left?.25:.2))<1e-9 && Math.Abs(resized.Y-(top?.23:.2))<1e-9 &&
                    Math.Abs(resized.Width-(left?.35:.45))<1e-9 && Math.Abs(resized.Height-(top?.37:.43))<1e-9,"Resize moved the opposite corner.");
                foreach(double delta in new[]{-2.0,2.0})
                {
                    var bounded=ResizeOrMoveBox(resizeBox,delta,delta,corner,.005,.005);
                    Require(bounded.X>=0 && bounded.Y>=0 && bounded.X+bounded.Width<=1.00000001 && bounded.Y+bounded.Height<=1.00000001 &&
                        bounded.Width>=.00499999 && bounded.Height>=.00499999,"Corner crossed its anchor or escaped bounds.");
                }
                var field=new ScoreField {Region=resizeBox,OutputRegion=resizeBox,OutputSlot="old target"};
                MoveOutputField(field,resizeBox,.05,.03,corner);
                Require(field.OutputRegion==resized && field.Region==resizeBox && field.OutputSlot is null,"Output corner resize changed the source crop or retained the target.");
            }
            Require(HitResizeCorner(resizeBox,new(.19,.19),.02,.02)==0,"Corner cannot be grabbed just outside the outline.");
            Require(HitResizeCorner(resizeBox,new(.4,.4),.02,.02)==-1,"Box interior should move, not resize.");
            var moved=ResizeOrMoveBox(resizeBox,.1,.1,-1,.005,.005);
            Require(Math.Abs(moved.X-.3)<1e-9 && Math.Abs(moved.Y-.3)<1e-9 && moved.Width==resizeBox.Width && moved.Height==resizeBox.Height,"Box movement failed.");
            foreach(double scale in new[]{.25,.5,1.0,2.0})
            {
                var nearby=new Point(.6+20/scale/1920,.6+20/scale/1080);
                Require(HitResizeCorner(resizeBox,nearby,FieldCornerGrabRadius/scale/1920,FieldCornerGrabRadius/scale/1080)==2,"Scaled corner grab area is too small.");
            }
            Require(FieldDragCursor(0)==Cursors.SizeNWSE && FieldDragCursor(1)==Cursors.SizeNESW && FieldDragCursor(2)==Cursors.SizeNWSE && FieldDragCursor(3)==Cursors.SizeNESW,"Corner cursor directions are wrong.");
            var gripCanvas=new System.Windows.Controls.Canvas();
            DrawResizeGrips(gripCanvas,0,0,400,200,2,"#62F0C5");
            Require(gripCanvas.Children.OfType<System.Windows.Shapes.Ellipse>().Count()==4 && gripCanvas.Children.OfType<System.Windows.Shapes.Ellipse>().All(pin=>pin.Width==28 && System.Windows.Controls.Panel.GetZIndex(pin)==100),"Round pins are missing or obscured by labels.");
            report.Add("PASS: round field pins, enlarged grab area at four preview scales, resize cursors and pin visibility above labels.");
            report.Add("PASS: all four resize corners, opposite-corner anchoring, outside-handle hits, canvas bounds, minimum size and independent output geometry.");
            Require(outputSettings.Mode=="Combined","New sessions should use a combined output.");
            OpenLayout(Arg("--test-layout"));
            Require(outputSettings.Mode=="Individual","Legacy setup lost separate-output compatibility.");
            OutputMode.SelectedIndex=0;
            Require(fields.All(f=>f.OutputRegion is not null),"Switching to combined did not place existing fields.");
            var firstField=fields[0];FieldList.SelectedItem=firstField;
            firstField.OutputRegion=new(.1,.1,.1,.1);var fixedPosition=firstField.OutputRegion;
            var secondField=fields[1];FieldList.SelectedItem=secondField;UnplaceOutputClick(this,new());
            SyncFields();Changed();await Task.Delay(100);
            Require(firstField.OutputRegion==fixedPosition&&secondField.OutputRegion is null,"Refresh moved an edit or replaced an unplaced field.");
            var extra=new ScoreField{Name="Added field"};fields.Add(extra);SyncFields();Changed();
            Require(extra.OutputRegion is not null&&firstField.OutputRegion==fixedPosition&&secondField.OutputRegion is null,"Adding a field rearranged existing output positions.");
            savePath=Path.Combine(directory,"automatic-placement.scoreboard.json");Require(SaveSetup(),"Automatic placement did not save.");
            OpenLayout(savePath);
            Require(fields[0].OutputRegion==fixedPosition&&fields[1].OutputRegion is null,"Reopening changed placement decisions.");
            OutputMode.SelectedIndex=1;OutputMode.SelectedIndex=0;
            Require(fields[1].OutputRegion is null,"Mode switch re-placed an intentionally unplaced field.");
            FieldList.SelectedItem=fields[1];PlaceOutputFieldClick(this,new());
            Require(fields[1].OutputRegion is not null,"Manual placement could not restore an unplaced field.");
            report.Add("PASS: automatic output placement, mode switch, manual addition, preserved edits, deliberate unplacement, save/reload and explicit restoration.");
            OpenLayout(Arg("--test-layout"));
            var crops=fields.ToDictionary(f=>f.Id,f=>f.Region);
            // Reproduce loading a bottom-of-frame guide AFTER fields were placed
            // in the upper canvas, without matching any names to guide targets.
            OutputMode.SelectedIndex=0;WorkspaceTabs.SelectedIndex=1;
            fields[0].OutputRegion=new(.03,.03,.2,.12);Changed();
            var priorPlacement=fields.ToDictionary(f=>f.Id,f=>f.OutputRegion);
            LoadGuide(Arg("--guide-file"));
            Require(fields.All(f=>f.OutputRegion==priorPlacement[f.Id]),"Loading guide moved existing placements.");
            Require(fields.Count==priorPlacement.Count && fields.All(f=>f.Region==crops[f.Id]),
                "Loading guide changed the fields or source crops.");
            Require(CloseUpGuide.IsChecked==false && compositionView==new Crop(0,0,1,1) &&
            OffscreenFieldsNotice.Visibility==Visibility.Collapsed,"Loading guide hid fields by automatically zooming in.");
            await Until(()=>engine.Preview.Combined is {Width:1920,Height:1080},"Guide load did not produce combined fields.");
            await Task.Delay(200);RefreshPreview();UpdateLayout();
            Require(CompositionCanvas.Children.OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text==fields[0].Name),
                "Existing field has no selectable outline immediately after guide load.");
            Require(FrameProcessor.Crop(engine.Preview.Combined!,compositionView).Pixels.Where((_,i)=>i%4==3).Any(a=>a>200),
                "No live digit pixels immediately after guide load.");
            CompositionPreviewViewport.BringIntoView();UpdateLayout();
            CaptureWindow(Path.Combine(directory,"guide-loaded-full-canvas.png"));
            double fullCanvasUnits=CompositionUnits(1);
            CloseUpGuide.IsChecked=true;CompositionViewChanged(CloseUpGuide,new());UpdateLayout();
            var guideBounds=scorebugGuide!.Bounds;
            Require(fullCanvasUnits/CompositionUnits(1)>3,
                "Close-up did not enlarge the guide: distant fields still limit magnification.");
            Require(ContainsPoint(compositionView,new(guideBounds.X,guideBounds.Y)) &&
                ContainsPoint(compositionView,new(guideBounds.X+guideBounds.Width,guideBounds.Y+guideBounds.Height)),
                "Close-up clipped guide artwork.");
            Require(OffscreenFieldsNotice.Visibility==Visibility.Visible,"Close-up did not identify offscreen fields.");
            await Until(()=>engine.Preview.Combined is {Width:1920,Height:1080},"Guide load did not produce combined fields.");
            await Task.Delay(200);RefreshPreview();UpdateLayout();
            CompositionPreviewViewport.BringIntoView();UpdateLayout();
            CaptureWindow(Path.Combine(directory,"guide-close-up.png"));
            CheckGuideNavigation(report,directory);
            var zoomOut=new MouseWheelEventArgs(Mouse.PrimaryDevice,0,-120*32){RoutedEvent=Mouse.PreviewMouseWheelEvent};
            CompositionCanvas.RaiseEvent(zoomOut);
            Require(zoomOut.Handled && compositionView==new Crop(0,0,1,1),
                "Wheel zoom could not escape guide close-up to recover distant fields.");
            FitCompositionClick(this,new());
            Require(compositionView==new Crop(0,0,1,1) && OffscreenFieldsNotice.Visibility==Visibility.Collapsed,
                "Show all did not restore the whole canvas and clear the notice.");
            Require(CompositionCanvas.Children.OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text==fields[0].Name),"Off-guide field has no selectable outline after Show all.");
            var visibleDigits=FrameProcessor.Crop(engine.Preview.Combined!,compositionView);
            Require(visibleDigits.Pixels.Where((_,i)=>i%4==3).Any(a=>a>200),"Show all contains no live digit pixels after loading guide.");
            CaptureWindow(Path.Combine(directory,"guide-loaded-existing-fields.png"));
            FieldList.SelectedItem=fields[0];ZoomSelectedClick(this,new());
            Require(ContainsPoint(compositionView,new(.13,.09)),"Zoom to selected did not recover the off-guide field.");
            CloseUpGuide.IsChecked=true;CompositionViewChanged(CloseUpGuide,new());
            Require(OffscreenFieldsNotice.Visibility==Visibility.Visible && fields.All(f=>f.OutputRegion==priorPlacement[f.Id] && f.Region==crops[f.Id]),
                "Returning to close-up lost the offscreen notice or changed field geometry.");
            report.Add("PASS: Close-up enlarges the guide despite distant fields; wheel zoom recovers the full canvas, Fit canvas and Zoom to selected work, and all crops and placements remain unchanged.");
            Require(scorebugGuide?.Slots.Count==8&&outputSettings.Width==1920&&outputSettings.Height==1080,"Tipoff guide geometry did not load.");
            var digitStyles=fields.ToDictionary(f=>f.Id,f=>f.Cleanup);
            WorkspaceTabs.SelectedIndex=1;MatchGuideClick(this,new());
            Require(fields.All(f=>f.OutputColor is null&&f.Cleanup==digitStyles[f.Id]),"Guide placement changed digit appearance.");
            foreach(var field in fields.Where(f=>f.OutputSlot is null).ToArray())
            {FieldList.SelectedItem=field;UnplaceOutputClick(this,new());}
            Require(fields.Count(f=>f.OutputRegion is not null)==8,"Eight matching Tipoff fields were not placed.");
            Require(fields.All(f=>f.Region==crops[f.Id]),"Guide placement changed source crops.");
            await Until(()=>engine.Preview.Combined is {Width:1920,Height:1080},"Combined preview did not appear.");
            await Task.Delay(200);RefreshPreview();UpdateLayout();
            CaptureWindow(Path.Combine(directory,"combined-editor.png"));
            void Png(VideoFrame frame,string file)
            {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(Bitmap(frame)));using var stream=File.Create(Path.Combine(directory,file));encoder.Save(stream);}
            Png(scorebugGuide!.Image,"guide-only.png");Png(engine.Preview.Combined!,"fields-only.png");
            int background=(800*1920+420)*4+3;
            Require(scorebugGuide.Image.Pixels[background]>0&&engine.Preview.Combined!.Pixels[background]==0,"Guide artwork entered the output.");
            var home=fields.First(f=>f.Name=="Home score");FieldList.SelectedItem=home;
            CheckEditingKeepsZoom(report);
            // Let the worker finish restoring the edited fixture before pixel comparisons.
            await Task.Delay(200);RefreshPreview();
            CheckCompositionZoomAndLayers(report,directory);
            var before=home.OutputRegion!;
            CompositionKeyDown(this,new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,0,Key.Right){RoutedEvent=Keyboard.KeyDownEvent});
            Require(Math.Abs(home.OutputRegion!.X-before.X-1.0/1920)<.000001&&home.Region==crops[home.Id],"Keyboard positioning changed the crop or moved the wrong distance.");
            OutputW.Value=85;OutputH.Value=90;OutputX.Value=200;OutputY.Value=780;
            Require(Math.Abs(home.OutputRegion.X-200.0/1920)<.000001,"Output position sliders failed.");
            var outputBeforeCrop=home.OutputRegion;
            CropW.Value=20;CropH.Value=20;CropX.Value=100;CropY.Value=100;
            Require(Math.Abs(home.Region.X+.2-1)<.000001&&Math.Abs(home.Region.Y+.2-1)<.000001&&home.OutputRegion==outputBeforeCrop,"Crop sliders exceeded the source or changed output placement.");
            CropW.Value=0;CropH.Value=0;
            Require(home.Region.Width>=.001&&home.Region.Height>=.001,"Crop sliders allowed an empty crop.");
            home.Region=crops[home.Id];PopulateCrop();
            var cropBeforeOutput=home.Region;
            OutputX.Value=outputSettings.Width;OutputY.Value=outputSettings.Height;
            Require(Math.Abs(home.OutputRegion.X+home.OutputRegion.Width-1)<.000001&&Math.Abs(home.OutputRegion.Y+home.OutputRegion.Height-1)<.000001&&home.Region==cropBeforeOutput,"Output sliders exceeded canvas bounds or altered the source crop.");
            home.OutputRegion=outputBeforeCrop;PopulateOutputInspector();
            double sliderX=OutputX.Value;
            System.Windows.Controls.Slider.IncreaseSmall.Execute(null,OutputX);
            Require(Math.Abs(OutputX.Value-sliderX-1)<.000001,"Output slider fine adjustment was not one pixel.");
            double cropX=CropX.Value;
            System.Windows.Controls.Slider.IncreaseSmall.Execute(null,CropX);
            Require(Math.Abs(CropX.Value-cropX-.1)<.000001,"Crop slider fine adjustment was not 0.1 percent.");
            home.Region=crops[home.Id];PopulateCrop();
            var positionBeforeSelection=home.OutputRegion;FieldList.SelectedItem=fields.First(f=>f!=home);FieldList.SelectedItem=home;
            Require(home.OutputRegion==positionBeforeSelection&&Math.Abs(OutputX.Value-positionBeforeSelection.X*outputSettings.Width)<.000001,"Selecting a field changed or failed to refresh slider values.");
            Changed();await Task.Delay(100);
            Require(engine.Preview.Combined is not null,"Live slider changes lost the combined preview.");
            report.Add("PASS: live crop/output sliders, independent geometry, edge bounds, minimum size, keyboard fine steps and field-selection synchronization.");
            GuideTarget.SelectedItem=scorebugGuide.Slots.First(s=>s.Name=="HomeScore");
            // Selecting the currently selected target does not raise an event; use the same action explicitly.
            SetSlot(home,scorebugGuide.Slots.First(s=>s.Name=="HomeScore"));Changed();SyncComposition();
            FieldEnabled.IsChecked=false;CleanupChanged(this,new());
            Require(!home.Enabled,"Field hide failed.");
            FieldEnabled.IsChecked=true;CleanupChanged(this,new());
            LiveClick(this,new());var lockedRegion=home.OutputRegion;UnplaceOutputClick(this,new());
            var lockedCrop=home.Region;OutputX.Value=0;CropX.Value=0;
            Require(home.Region==lockedCrop&&home.OutputRegion==lockedRegion,"Sliders bypassed live lock.");
            Require(home.OutputRegion==lockedRegion&&!OutputMode.IsEnabled&&!OutputLayoutControls.IsEnabled&&!PlacementActions.IsEnabled,"Live lock allowed output edits.");
            LiveClick(this,new());
            savePath=Path.Combine(directory,"tipoff.scoreboard.json");Require(SaveSetup(),"Combined setup could not be saved.");
            OpenLayout(savePath);await Until(()=>engine.Preview.Combined is not null,"Saved canvas did not restore.");
            Require(outputSettings.Mode=="Combined"&&scorebugGuide?.Slots.Count==8&&fields.Count(f=>f.OutputRegion is not null)==8&&!output,"Combined layout did not round-trip with outputs stopped.");
            Require(CloseUpGuide.IsChecked==false && compositionView==new Crop(0,0,1,1),"Restoring a guide layout automatically hid fields with close-up.");
            var missing=Layout.Load(savePath);missing.Output.GuidePath="missing-guide.gtzip";string missingPath=Path.Combine(directory,"missing-guide.scoreboard.json");missing.Save(missingPath);
            OpenLayout(missingPath);Require(scorebugGuide is null&&guideWarning is not null&&fields.Count(f=>f.OutputRegion is not null)==8,"Missing guide discarded the saved layout.");
            OpenLayout(Path.Combine(directory,"tipoff.scoreboard.json"));
            report.Add("PASS: real GTZIP guide, eight targets, separate crop/output placement, preserved digit colors, keyboard/slider edits, visibility, live lock, relative save/load and missing-guide recovery.");
            if(args.Contains("--ndi"))
            {
                await Until(()=>engine.Preview.Healthy,"Photo pipeline did not recover.");OutputClick(this,new());
                await Until(()=>engine.Preview.OutputCount==1,"Expected exactly one combined NDI output.");
                using var finder=new NdiDiscovery();NdiSource? found=null;
                await Until(()=> (found=finder.GetSources().FirstOrDefault(s=>s.Name.Contains(VideoEngine.OutputName(kind,"Combined")))) is not null,"Combined source was not discovered.");
                using var receiver=new NdiReceiver(found!);VideoFrame? received=null;
                await Until(()=> (received=receiver.Capture(20)) is {Width:1920,Height:1080} f&&f.Pixels.Where((_,i)=>i%4==3).Any(a=>a>240),"Combined NDI did not carry live fields.");
                Require(received!.Pixels[background]<10,"Guide artwork leaked into NDI.");
                OutputClick(this,new());await Until(()=>engine.Preview.OutputCount==0,"NDI output did not stop.");report.Add("PASS: one full-HD NDI input contains all eight placed fields and transparency, without guide artwork.");
            }
            FieldList.SelectedItem=fields.First(f=>f.Name=="Game clock");WorkspaceTabs.SelectedIndex=1;RefreshPreview();UpdateLayout();CompositionPreviewViewport.BringIntoView();await Task.Delay(100);CaptureWindow(Path.Combine(directory,"combined-editor.png"));
            if(args.Contains("--video-file"))
            {
                LoadMedia(Arg("--video-file"));await Until(()=>engine.Preview.Source is not null&&engine.Preview.Healthy,"Video did not open.");await Task.Delay(800);
                var detected=FieldDetector.DetectAutomatic(engine.Preview.Source!);
                Require(detected.Regions.Count>=2,"Recorded video fields were not found.");
                fields.Clear();
                for(int i=0;i<detected.Regions.Count;i++)
                {
                    var field=new ScoreField {Name="Recorded field "+(i+1),Region=detected.Regions[i],Cleanup=new(){Isolation=detected.Isolation,Threshold=detected.Threshold}};
                    SetSlot(field,scorebugGuide!.Slots[i%scorebugGuide.Slots.Count]);fields.Add(field);
                }
                SyncFields();Changed();await Until(()=>engine.Preview.Combined is not null,"Recorded composite did not appear.");
                var process=System.Diagnostics.Process.GetCurrentProcess();process.Refresh();long memoryBefore=process.PrivateMemorySize64;
                string Hash(VideoFrame f)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f.Pixels));
                string previous=Hash(engine.Preview.Combined!);
                await Until(()=>Hash(engine.Preview.Combined!)!=previous,"Combined recorded digits did not move.");
                if(args.Contains("--ndi"))
                {
                    OutputClick(this,new());await Until(()=>engine.Preview.OutputCount==1,"Recorded combined NDI did not start.");
                    using var finder=new NdiDiscovery();NdiSource? found=null;
                    await Until(()=> (found=finder.GetSources().FirstOrDefault(s=>s.Name.Contains(VideoEngine.OutputName("Video","Combined")))) is not null,"Recorded combined source not discovered.");
                    using var receiver=new NdiReceiver(found!);string? first=null;
                    await Until(()=> {var frame=receiver.Capture(20);if(frame is null)return false;string next=Hash(frame);if(first is null){first=next;return false;}return next!=first&&frame.Pixels.Where((_,i)=>i%4==3).Any(a=>a<10);},"Recorded combined NDI did not change or lost transparency.");
                    for(int i=0;i<100;i++) {await Task.Delay(200);Require(engine.Preview.OutputCount==1&&engine.Preview.Healthy,"Combined video output failed during playback.");}
                    OutputClick(this,new());
                }
                process.Refresh();long growth=process.PrivateMemorySize64-memoryBefore;
                Require(growth<250L*1024*1024,"Combined playback memory grew excessively.");
                report.Add(args.Contains("--ndi")
                    ? $"PASS: recorded video updates one transparent combined NDI feed; 20-second check, private memory change {growth/1048576} MiB."
                    : $"PASS: recorded video updates the combined preview; private memory change {growth/1048576} MiB.");
            }
            await CheckTwoDistantFields(report,directory);
            File.WriteAllLines(Path.Combine(directory,"composition-checks.txt"),report);
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(directory,"composition-error.txt"),ex.ToString());Environment.ExitCode=1;}
        finally {dirty=false;Close();}
    }
}
