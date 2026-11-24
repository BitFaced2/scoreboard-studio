using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private VideoFrame? perspectiveFrame;
    private int selectedCorner;
    private static VideoFrame LoadImage(string path)
    {
        var decoder = BitmapDecoder.Create(new Uri(System.IO.Path.GetFullPath(path)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var original = decoder.Frames[0];
        if (original.PixelWidth > 8192 || original.PixelHeight > 4320)
            throw new InvalidDataException("Use an image no larger than 8192 × 4320.");
        var converted = new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return new(converted.PixelWidth, converted.PixelHeight, pixels);
    }

    private void OpenLayout(string path)
    {
        var layout = Layout.Load(path);
        string? mediaPath = layout.VideoPath ?? layout.ImagePath;
        if (!string.IsNullOrWhiteSpace(mediaPath))
            mediaPath = System.IO.Path.GetFullPath(mediaPath, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        bool missing = mediaPath is not null && !File.Exists(mediaPath);
        // Decode images before changing the existing setup, so an invalid image
        // does not discard the operator's current source or geometry.
        VideoFrame? image = mediaPath is not null && !missing && layout.VideoPath is null ? LoadImage(mediaPath) : null;
        CancelDetection(); // Even an intentionally empty saved layout belongs to the operator.
        EndPerspectiveEditing();
        ResetStabilization();
        output = false; OutputButton.Content = "Start NDI outputs"; generation++;
        // Digit appearance is authoritative, including for setups with legacy output tints.
        fields.Clear(); foreach (var field in layout.Fields) { field.OutputColor=null; fields.Add(field); }
        RestoreOutput(layout,path);
        perspective = layout.Perspective with { };
        if (mediaPath is not null)
        {
            if (missing) SwitchSource("None");
            else if (image is not null) { SwitchSource("Image",image:image); stillPath=mediaPath; }
            else LoadMedia(mediaPath);
        }
        else if (layout.SourceName is not null)
        {
            SwitchSource("None");
            SourceCombo.SelectedItem = SourceCombo.Items.Cast<NdiSource>().FirstOrDefault(s => s.Name == layout.SourceName);
        }
        suggestions.Clear(); beforeDetection = null; beforeDetectionPerspective = null; UndoDetection.Content = "Undo detection";
        AcceptDetection.Visibility = UndoDetection.Visibility = Visibility.Collapsed;
        savePath = path; setupName = layout.Name; dirty = false;
        SetupTitle.Text = setupName + " • Saved";
        DetectionIsolation.SelectedIndex = fields.FirstOrDefault()?.Cleanup.Isolation == "White highlights" ? 1 : 0;
        DetectionThreshold.Value = fields.FirstOrDefault()?.Cleanup.Threshold ?? 130;
        SyncFields(); PopulateInspector(); UpdatePerspectiveStatus(); PushPlan();
        CancelDetection();
        DetectionHint.Text="";
        Notice.Text = missing ? "Setup loaded. Use Load image or video to locate the missing file: " + mediaPath
            : layout.SourceName is not null ? $"Setup loaded. Connect to {layout.SourceName}, then review the crops."
            : "Setup loaded. Review the fields before starting outputs.";
    }

    private void PerspectiveClick(object sender, RoutedEventArgs e)
    {
        if (locked || engine.Preview.RawSource is not { } frame || !engine.Preview.Healthy)
        { Notice.Text = "Connect a source or load an image or video before adjusting perspective."; return; }
        if (EditingPerspective) { EndPerspectiveEditing(); return; }
        CameraSection.IsExpanded = true;
        FieldToolGroups.Columns = 1;
        DetectionSection.Visibility = Visibility.Collapsed;
        CancelDetection();
        perspectiveFrame=frame;
        selectedCorner=0;
        output = false; OutputButton.Content = "Start NDI outputs";
        drawing = false; draft = null; DrawButton.Content = "+ Draw a field";
        suggestions.Clear(); AcceptDetection.Visibility = Visibility.Collapsed;
        perspectiveDraft = perspective.Enabled ? perspective with { } : new() {
            Enabled=true, Width=Math.Min(frame.Width,4096), Height=Math.Min(frame.Height,2160),
            TopLeft=new(.05,.05),TopRight=new(.95,.05),BottomRight=new(.95,.95),BottomLeft=new(.05,.95)
        };
        PerspectiveWidth.Text = perspectiveDraft.Width.ToString(); PerspectiveHeight.Text = perspectiveDraft.Height.ToString();
        PerspectiveControls.Visibility = Visibility.Visible;
        DetectionOptions.Visibility = DetectionHint.Visibility = DetectButton.Visibility = Visibility.Collapsed;
        StabilizeButton.IsEnabled=ReferenceButton.IsEnabled=false;
        PerspectiveButton.Content = "Cancel correction";
        DetectButton.IsEnabled = DetectionOptions.IsEnabled = FieldActions.IsEnabled = Inspector.IsEnabled = FieldList.IsEnabled = SourceControls.IsEnabled = false;
        WorkspaceTabs.SelectedIndex = 0;
        UpdateLayout();
        Notice.Text = "Mark the board's four corners. Applying the correction re-detects the fields once in the selected view; outputs remain stopped for review.";
        PushPlan(); RefreshPreview(); RenderBoxes();
    }

    private void ApplyPerspectiveClick(object sender, RoutedEventArgs e)
    {
        if (perspectiveDraft is null || locked) return;
        if (!int.TryParse(PerspectiveWidth.Text, out int width) || !int.TryParse(PerspectiveHeight.Text, out int height))
        { Notice.Text = "Enter whole numbers for the corrected width and height."; return; }
        var candidate = perspectiveDraft with { Width = width, Height = height, Enabled = true };
        if (!candidate.IsValid(out string reason)) { Notice.Text = reason; return; }
        var frame = perspectiveFrame;
        if (frame is null) return;
        var previousPerspective = perspective with { };
        perspective = candidate;
        ResetStabilization(); EndPerspectiveEditing(); Changed();
        perspectiveDetectionTask = ReevaluatePerspectiveFields(frame, previousPerspective);
    }

    private void CancelPerspectiveClick(object sender, RoutedEventArgs e)
    { EndPerspectiveEditing(); Notice.Text = "Perspective edits canceled. Your previous correction is unchanged; outputs remain stopped."; }

    private void ResetPerspectiveClick(object sender, RoutedEventArgs e)
    {
        if (locked) return;
        var frame = perspectiveFrame ?? engine.Preview.RawSource;
        if (frame is null) return;
        var previousPerspective = perspective with { };
        perspective = new(); ResetStabilization(); EndPerspectiveEditing(); Changed();
        perspectiveDetectionTask = ReevaluatePerspectiveFields(frame, previousPerspective);
    }

    private void EndPerspectiveEditing()
    {
        dragging = false; cornerDragging = -1; BoxCanvas.ReleaseMouseCapture(); perspectiveDraft = null;perspectiveFrame=null;
        PerspectiveControls.Visibility = Visibility.Collapsed; PerspectiveButton.Content = "Keystone Correction";
        FieldToolGroups.Columns = 2;
        DetectionSection.Visibility = Visibility.Visible;
        DetectionOptions.Visibility = DetectionHint.Visibility = DetectButton.Visibility = Visibility.Visible;
        DetectButton.IsEnabled = DetectionOptions.IsEnabled = FieldActions.IsEnabled = SourceControls.IsEnabled = !locked;
        FieldList.IsEnabled = true; Inspector.IsEnabled = !locked && Selected is not null;
        StabilizeButton.IsEnabled=!locked;ReferenceButton.IsEnabled=!locked && stabilization.Enabled;
        UpdatePerspectiveStatus(); RenderBoxes();
    }

    private void UpdatePerspectiveStatus() => PerspectiveStatus.Text = perspective.Enabled
        ? $"Perspective on • {perspective.Width} × {perspective.Height} • Applied before cropping" : "Perspective correction off";

    private void DrawPerspectiveCorners()
    {
        if (perspectiveDraft is null) return;
        double width = VideoSurface.Width, height = VideoSurface.Height;
        var corners = perspectiveDraft.Corners();
        bool valid = perspectiveDraft.IsValid(out var reason);
        var color = Brush(valid ? "#62F0C5" : "#FF8B8B");
        var polygon = new Polygon { Stroke=color, StrokeThickness=4, Fill=new SolidColorBrush(Color.FromArgb(30,30,255,180)), IsHitTestVisible=false };
        foreach (var corner in corners) polygon.Points.Add(new(corner.X*width, corner.Y*height));
        BoxCanvas.Children.Add(polygon);
        string[] labels = ["1  TOP LEFT", "2  TOP RIGHT", "3  BOTTOM RIGHT", "4  BOTTOM LEFT"];
        for (int i=0; i<4; i++)
        {
            double x=corners[i].X*width, y=corners[i].Y*height, radius=CanvasUnits(7);
            var grip = new Ellipse { Width=radius*2,Height=radius*2,Fill=color,Stroke=Brush(i==selectedCorner?"#FFFFFF":"#10251F"),StrokeThickness=CanvasUnits(2),Tag=i,Cursor=System.Windows.Input.Cursors.SizeAll };
            Canvas.SetLeft(grip,x-radius); Canvas.SetTop(grip,y-radius); BoxCanvas.Children.Add(grip);
            var label = new Border { Background=color, Padding=new Thickness(CanvasUnits(5)),Tag=i,Cursor=System.Windows.Input.Cursors.SizeAll,
                Child=new TextBlock { Text=labels[i],FontSize=CanvasUnits(11),FontWeight=FontWeights.Bold,Foreground=Brush("#10251F") } };
            label.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            Canvas.SetLeft(label,Math.Clamp(x-label.DesiredSize.Width/2,0,Math.Max(0,width-label.DesiredSize.Width)));
            double labelY=y>height/2?y-radius-CanvasUnits(5)-label.DesiredSize.Height:y+radius+CanvasUnits(5);
            Canvas.SetTop(label,Math.Clamp(labelY,0,Math.Max(0,height-label.DesiredSize.Height)));
            BoxCanvas.Children.Add(label);
        }
        PerspectiveStatus.Text = valid ? "Corner order: top left → top right → bottom right → bottom left" : reason;
    }
    private double CanvasUnits(double pixels)
    {
        if(!IsAncestorOf(BoxCanvas))return pixels*2;
        var transform=BoxCanvas.TransformToAncestor(this);
        var a=transform.Transform(new Point(0,0));var b=transform.Transform(new Point(1,0));
        return pixels/Math.Max(.01,Math.Abs(b.X-a.X));
    }
    private void PreviewSizeChanged(object sender,SizeChangedEventArgs e)
    { if(ready)RenderBoxes(); }
    private void MoveCorner(int index,Core.NormalPoint point)
    {
        if(perspectiveDraft is null)return;
        switch(index) {case 0:perspectiveDraft.TopLeft=point;break;case 1:perspectiveDraft.TopRight=point;break;case 2:perspectiveDraft.BottomRight=point;break;case 3:perspectiveDraft.BottomLeft=point;break;}
        RenderBoxes();
    }
    private void CornerKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(!EditingPerspective || locked)return;
        var p=perspectiveDraft!.Corners()[selectedCorner];
        double step=System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)?10:1;
        double dx=e.Key==System.Windows.Input.Key.Left?-step:e.Key==System.Windows.Input.Key.Right?step:0;
        double dy=e.Key==System.Windows.Input.Key.Up?-step:e.Key==System.Windows.Input.Key.Down?step:0;
        if(dx==0&&dy==0)return;
        MoveCorner(selectedCorner,new(Math.Clamp(p.X+dx/VideoSurface.Width,0,1),Math.Clamp(p.Y+dy/VideoSurface.Height,0,1)));e.Handled=true;
    }
}
