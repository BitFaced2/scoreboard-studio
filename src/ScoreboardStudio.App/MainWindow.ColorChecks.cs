using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private async Task ColorSmokeTest(string directory)
    {
        var report=new List<string>();
        try
        {
            Directory.CreateDirectory(directory);
            void Require(bool value,string message){if(!value)throw new Exception(message);}
            Require(WindowState==WindowState.Maximized,"App did not start maximized.");
            WindowState=WindowState.Normal;
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--test-layout");
            OpenLayout(args[index+1]);OutputMode.SelectedIndex=0;WorkspaceTabs.SelectedIndex=1;
            var sections=new[]{FieldSection,CropSection,OutputPlacementInspector,CleanupSection,DigitAppearanceSection,EdgeSection};
            Require(sections.All(section=>!section.IsExpanded),"Inspector sections must start collapsed.");
            foreach(var section in sections) section.IsExpanded=true;
            var field=fields[0];FieldList.SelectedItem=field;
            field.Cleanup=field.Cleanup with {Brightness=2,Color="Custom",CustomColor="#AAAAAA"};field.OutputColor="#FFFFFF";
            ApplyPickedColor(field,"#4080C0");
            Require(field.Cleanup.Color=="Custom"&&field.Cleanup.Brightness==1&&field.OutputColor is null,"Picker did not clear brightness/tint interference.");
            for(int i=0;i<100;i++){await Task.Delay(50);if(engine.Preview.Fields.TryGetValue(field.Id,out var f)&&f.Pixels.Where((_,j)=>j%4==0).Any(b=>b==192))break;}
            var clean=engine.Preview.Fields[field.Id];int pixel=Enumerable.Range(0,clean.Width*clean.Height).First(i=>clean.Pixels[i*4+3]>240)*4;
            Require(clean.Pixels[pixel]==192&&clean.Pixels[pixel+1]==128&&clean.Pixels[pixel+2]==64,"Cleanup preview did not use selected RGB.");
            var combined=engine.Preview.Combined!;
            Require(Enumerable.Range(0,combined.Width*combined.Height).Any(i=>combined.Pixels[i*4]==192&&combined.Pixels[i*4+1]==128&&combined.Pixels[i*4+2]==64&&combined.Pixels[i*4+3]>240),"Combined preview did not use selected RGB.");
            // Simulate a setup from the previous UI with a conflicting output tint.
            field.OutputColor="#FFFFFF";
            savePath=Path.Combine(directory,"color.scoreboard.json");Require(SaveSetup(),"Color setup save failed.");OpenLayout(savePath);field=fields[0];
            Require(field.OutputColor is null&&field.Cleanup.CustomColor=="#4080C0","Legacy output tint was not discarded on reload.");
            FieldList.SelectedItem=field;LiveClick(this,new());ApplyPickedColor(field,"#FF0000");Require(field.Cleanup.CustomColor=="#4080C0","Picker bypassed live lock.");LiveClick(this,new());
            ApplyPickedColor(field,"#4080C0");
            report.Add("PASS: exact custom RGB in cleanup and combined pixels, 2× brightness and white-tint recovery, legacy tint removal on reload, live lock.");
            var picker=new ColorPickerWindow(this,"#4080C0");
            foreach(string hex in new[]{"#000000","#FFFFFF","#AAAAAA","#4080C0","#ED1824","#005BAB"})
            {picker.SetColor(hex);Require(picker.SelectedHex==hex,"Color wheel round-trip changed "+hex);}
            Require(ColorPickerWindow.Hsv(120,1,1)==Colors.Lime&&ColorPickerWindow.Hsv(240,1,1)==Colors.Blue,"Wheel hue conversion failed.");
            picker.SetColor("#4080C0");picker.Show();await Task.Delay(200);picker.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)picker.ActualWidth,(int)picker.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(picker);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(directory,"color-picker.png")))encoder.Save(stream);
            picker.Close();Require(field.Cleanup.CustomColor=="#4080C0","Closing the picker modified the field.");
            report.Add("PASS: visual picker, HSV primaries, white/black/gray/custom round-trip and cancellation.");
            WorkspaceTabs.SelectedIndex=1;InspectorScroll.ScrollToTop();Width=1440;Height=940;await Task.Delay(200);UpdateLayout();CaptureWindow(Path.Combine(directory,"outputs-modern.png"));
            DigitColor.IsDropDownOpen=true;await Task.Delay(100);
            var popup=(System.Windows.Controls.Primitives.Popup)DigitColor.Template.FindName("PART_Popup",DigitColor);
            Require(popup.IsOpen&&popup.Child is FrameworkElement {ActualWidth:>100,ActualHeight:>60},"Styled dropdown did not open.");
            DigitColor.SelectedIndex=0;DigitColor.IsDropDownOpen=false;
            Require(field.Cleanup.Color=="White"&&field.OutputColor is null,"Digit preset did not apply.");
            DigitColor.SelectedIndex=2;
            Require(field.Cleanup.Color=="Custom"&&field.OutputColor is null,"Styled dropdown could not restore digit color.");
            InspectorScroll.ScrollToBottom();await Task.Delay(100);Require(InspectorScroll.VerticalOffset>0,"Inspector cannot scroll to its lower controls.");
            InspectorScroll.ScrollToTop();
            foreach(double width in new[]{1440d,1180d,1000d})
            {
                Width=width;Height=780;await Task.Delay(100);UpdateLayout();
                Require(InspectorScroll.ExtentWidth<=InspectorScroll.ViewportWidth+.5,"Inspector overflow at "+width);
                var inspectorBounds=InspectorScroll.TransformToAncestor(this).TransformBounds(new Rect(InspectorScroll.RenderSize));
                Require(inspectorBounds.Left>=0&&inspectorBounds.Right<=ActualWidth,"Inspector outside window at "+width);
                Require(WindowScroll.ExtentWidth<=WindowScroll.ViewportWidth+.5,"Window requires horizontal scrolling at "+width);
                foreach(var control in new FrameworkElement[]{OutputFit,GuideTarget,DigitColor,OutputX,OutputH,FieldName})
                {var box=control.TransformToAncestor(InspectorScroll).TransformBounds(new Rect(control.RenderSize));Require(box.Right<=InspectorScroll.ViewportWidth+.5,"Clipped "+control.Name+" at "+width);}
            }
            CaptureWindow(Path.Combine(directory,"laptop-modern.png"));
            report.Add("PASS: styled dropdown open/selection, inspector scrolling, controls within viewport at 1440, 1180 and 1000-pixel widths without horizontal scrolling.");
            Width=1440;Height=940;InspectorScroll.ScrollToTop();UpdateLayout();
            double expandedExtent=InspectorScroll.ExtentHeight;
            bool previousDirty=dirty;var previousCrop=field.Region;var previousOutput=field.OutputRegion;
            foreach(var section in sections)
            {
                section.ApplyTemplate();
                var toggle=(System.Windows.Controls.Primitives.ToggleButton)section.Template.FindName("SectionToggle",section);
                Require(toggle.FontSize>=16,"Section heading is too small.");
                toggle.IsChecked=false;UpdateLayout();
                Require(!section.IsExpanded,"Section header did not collapse its content.");
            }
            Require(InspectorScroll.ExtentHeight<expandedExtent-300,"Collapsed sections did not release scroll space.");
            FieldList.SelectedItem=fields[1];WorkspaceTabs.SelectedIndex=0;WorkspaceTabs.SelectedIndex=1;FieldList.SelectedItem=field;
            Require(sections.All(section=>!section.IsExpanded),"Field/tab selection reset section state.");
            Require(dirty==previousDirty&&field.Region==previousCrop&&field.OutputRegion==previousOutput,"Collapsing sections changed the setup.");
            await Task.Delay(100);UpdateLayout();
            Require(ApplyCleanupButton.IsVisible&&ResetCleanupButton.IsVisible&&CleanupActions.IsEnabled,"Cleanup actions disappeared with collapsed sections.");
            var actionBounds=CleanupActions.TransformToAncestor(this).TransformBounds(new Rect(CleanupActions.RenderSize));
            InspectorScroll.ScrollToBottom();UpdateLayout();
            Require(CleanupActions.TransformToAncestor(this).TransformBounds(new Rect(CleanupActions.RenderSize))==actionBounds,"Cleanup actions scroll away with inspector content.");
            FieldList.SelectedIndex=-1;Require(!CleanupActions.IsEnabled,"Cleanup actions enabled with no field.");FieldList.SelectedItem=field;
            LiveClick(this,new());Require(!CleanupActions.IsEnabled,"Cleanup actions bypassed live lock.");
            bool backgroundDirty=dirty;
            var pixels=engine.Preview.Combined!.Pixels.ToArray();
            ApplyPreviewBackgroundColor("#447755");await Task.Delay(100);UpdateLayout();
            Require(PreviewBackground.SelectedIndex==3&&PreviewBackdrop is SolidColorBrush {Color:var bg}&&bg==Color.FromRgb(68,119,85),"Custom background did not apply.");
            Require(OutputPreviewBorder.Background==PreviewBackdrop&&CleanPreviewBorder.Background==PreviewBackdrop,"Preview backgrounds are not synchronized.");
            Require(dirty==backgroundDirty&&engine.Preview.Combined!.Pixels.SequenceEqual(pixels),"Preview background changed setup or transmitted pixels.");
            LiveClick(this,new());
            CaptureWindow(Path.Combine(directory,"custom-background.png"));
            PreviewBackground.SelectedIndex=0;UpdateLayout();
            Require(ReferenceEquals(PreviewBackdrop,FindResource("Checker")),"Checkerboard could not be restored.");
            PreviewBackground.SelectedIndex=3;Require(((SolidColorBrush)PreviewBackdrop).Color==Color.FromRgb(68,119,85),"Custom shade was lost switching background modes.");
            PreviewBackground.SelectedIndex=0;
            report.Add("PASS: pinned cleanup actions remain visible, follow selection/live lock; custom background synchronizes previews without changing output pixels or setup, and checkerboard can be restored.");
            CaptureWindow(Path.Combine(directory,"inspector-collapsed.png"));
            FieldSection.IsExpanded=true;OutputPlacementInspector.IsExpanded=true;UpdateLayout();
            CaptureWindow(Path.Combine(directory,"inspector-sections.png"));
            CropSection.IsExpanded=true;CleanupSection.IsExpanded=true;
            report.Add("PASS: all six sections start collapsed and toggle, collapsed content releases space, section choices survive field/tab selection without modifying setup.");
            foreach(var section in sections) section.IsExpanded=false;
            NdiSourceSection.IsExpanded=CameraSection.IsExpanded=DetectionSection.IsExpanded=GuideSection.IsExpanded=true;
            Width=1000;Height=780;WorkspaceTabs.SelectedIndex=0;await Task.Delay(150);UpdateLayout();
            Require(SourceScroll.ExtentWidth<=SourceScroll.ViewportWidth+.5,"Source controls overflow horizontally.");
            Require(FieldToolsScroll.ExtentWidth<=FieldToolsScroll.ViewportWidth+.5,"Camera/detection tools overflow horizontally.");
            Require(FieldList.ActualHeight>60&&SourcePreviewBorder.ActualHeight>80,"Expanded tools obscured fields or preview.");
            CaptureWindow(Path.Combine(directory,"workspace-tools-small.png"));
            WorkspaceTabs.SelectedIndex=1;await Task.Delay(150);UpdateLayout();
            Require(GuideSection.IsVisible&&CanvasControls.IsVisible,"Guide settings are inaccessible in Single Input mode.");
            CaptureWindow(Path.Combine(directory,"workspace-output-small.png"));
            OutputMode.SelectedIndex=1;await Task.Delay(50);
            Require(!GuideSection.IsVisible&&!CombinedPanel.IsVisible,"Combined settings remained visible in Separate Inputs mode.");
            OutputMode.SelectedIndex=0;
            report.Add("PASS: grouped source/camera/detection controls fit a small window, field list remains reachable, guide settings follow output mode.");
            File.WriteAllLines(Path.Combine(directory,"color-checks.txt"),report);
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(directory,"color-error.txt"),ex.ToString());Environment.ExitCode=1;}
        finally {dirty=false;Close();}
    }
}
