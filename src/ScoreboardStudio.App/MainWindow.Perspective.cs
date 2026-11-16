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
        EndPerspectiveEditing();
        output = false; OutputButton.Content = "Start NDI outputs"; generation++;
        fields.Clear(); foreach (var field in layout.Fields) fields.Add(field);
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
        suggestions.Clear(); beforeDetection = null; AcceptDetection.Visibility = UndoDetection.Visibility = Visibility.Collapsed;
        savePath = path; setupName = layout.Name; dirty = false;
        SetupTitle.Text = setupName + " • Saved";
        DetectionIsolation.SelectedIndex = fields.FirstOrDefault()?.Cleanup.Isolation == "White highlights" ? 1 : 0;
        DetectionThreshold.Value = fields.FirstOrDefault()?.Cleanup.Threshold ?? 130;
        SyncFields(); PopulateInspector(); UpdatePerspectiveStatus(); PushPlan();
        CancelAutomaticDetection();
        Notice.Text = missing ? "Setup loaded. Use Load image or video to locate the missing file: " + mediaPath
            : layout.SourceName is not null ? $"Setup loaded. Connect to {layout.SourceName}, then review the crops."
            : "Setup loaded. Review the fields before starting outputs.";
    }

    private void PerspectiveClick(object sender, RoutedEventArgs e)
    {
        if (locked || engine.Preview.RawSource is not { } frame || !engine.Preview.Healthy)
        { Notice.Text = "Connect a source or load an image or video before adjusting perspective."; return; }
        if (EditingPerspective) { EndPerspectiveEditing(); return; }
        CancelAutomaticDetection();
        output = false; OutputButton.Content = "Start NDI outputs";
        drawing = false; draft = null; DrawButton.Content = "+ Draw a field";
        suggestions.Clear(); AcceptDetection.Visibility = Visibility.Collapsed;
        perspectiveDraft = perspective.Enabled ? perspective with { } : new() {
            Enabled=true, Width=Math.Min(frame.Width,4096), Height=Math.Min(frame.Height,2160)
        };
        PerspectiveWidth.Text = perspectiveDraft.Width.ToString(); PerspectiveHeight.Text = perspectiveDraft.Height.ToString();
        PerspectiveControls.Visibility = Visibility.Visible;
        DetectionOptions.Visibility = DetectionHint.Visibility = DetectButton.Visibility = Visibility.Collapsed;
        PerspectiveButton.Content = "Cancel straightening";
        DetectButton.IsEnabled = DetectionOptions.IsEnabled = FieldActions.IsEnabled = Inspector.IsEnabled = FieldList.IsEnabled = SourceControls.IsEnabled = false;
        WorkspaceTabs.SelectedIndex = 0;
        Notice.Text = "Outputs are stopped while you adjust perspective. Mark the board's four corners, then apply and review your field boxes.";
        PushPlan(); RefreshPreview(); RenderBoxes();
    }

    private void ApplyPerspectiveClick(object sender, RoutedEventArgs e)
    {
        if (perspectiveDraft is null || locked) return;
        if (!int.TryParse(PerspectiveWidth.Text, out int width) || !int.TryParse(PerspectiveHeight.Text, out int height))
        { Notice.Text = "Enter whole numbers for the corrected width and height."; return; }
        var candidate = perspectiveDraft with { Width = width, Height = height, Enabled = true };
        if (!candidate.IsValid(out string reason)) { Notice.Text = reason; return; }
        perspective = candidate;
        EndPerspectiveEditing(); Changed();
        Notice.Text = "Perspective correction applied before cropping. Review every field box on the straightened image before starting outputs.";
    }

    private void CancelPerspectiveClick(object sender, RoutedEventArgs e)
    { EndPerspectiveEditing(); Notice.Text = "Perspective edits canceled. Your previous correction is unchanged; outputs remain stopped."; }

    private void ResetPerspectiveClick(object sender, RoutedEventArgs e)
    {
        if (locked) return;
        perspective = new(); EndPerspectiveEditing(); Changed();
        Notice.Text = "Perspective correction removed. Review your field boxes on the original camera image.";
    }

    private void EndPerspectiveEditing()
    {
        dragging = false; cornerDragging = -1; BoxCanvas.ReleaseMouseCapture(); perspectiveDraft = null;
        PerspectiveControls.Visibility = Visibility.Collapsed; PerspectiveButton.Content = "Straighten camera";
        DetectionOptions.Visibility = DetectionHint.Visibility = DetectButton.Visibility = Visibility.Visible;
        DetectButton.IsEnabled = DetectionOptions.IsEnabled = FieldActions.IsEnabled = SourceControls.IsEnabled = !locked;
        FieldList.IsEnabled = true; Inspector.IsEnabled = !locked && Selected is not null;
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
            double x=corners[i].X*width, y=corners[i].Y*height, radius=width*.018;
            var grip = new Ellipse { Width=radius*2,Height=radius*2,Fill=color,Stroke=Brush("#10251F"),StrokeThickness=3,IsHitTestVisible=false };
            Canvas.SetLeft(grip,x-radius); Canvas.SetTop(grip,y-radius); BoxCanvas.Children.Add(grip);
            var label = new Border { Background=color, Padding=new Thickness(8,4,8,4), IsHitTestVisible=false,
                Child=new TextBlock { Text=labels[i],FontSize=Math.Max(18,width/64),FontWeight=FontWeights.Bold,Foreground=Brush("#10251F") } };
            Canvas.SetLeft(label,Math.Clamp(x-85,0,Math.Max(0,width-220))); Canvas.SetTop(label, y > height/2 ? Math.Max(0,y-radius-40) : Math.Min(height-40,y+radius+5));
            BoxCanvas.Children.Add(label);
        }
        PerspectiveStatus.Text = valid ? "Corner order: top left → top right → bottom right → bottom left" : reason;
    }
}
