using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow : Window
{
    private readonly VideoEngine engine = new(ex=>Diagnostics.Log("Video processing",ex));
    private readonly ObservableCollection<ScoreField> fields = [];
    private readonly ObservableCollection<OutputCard> cards = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private NdiDiscovery? discovery;
    private bool ready, updating, dirty, locked, output, drawing, dragging;
    private int resizeCorner = -1;
    private string kind = "None", setupName = "Untitled setup";
    private string? savePath;
    private string? stillPath;
    private string? videoPath;
    private VideoFileSource? video;
    private PerspectiveSettings perspective = new();
    private PerspectiveSettings? perspectiveDraft;
    private int cornerDragging = -1;
    private bool EditingPerspective => perspectiveDraft is not null;
    private NdiSource? source;
    private VideoFrame? still;
    private int generation, ticks;
    private Point dragStart;
    private Crop? dragOriginal, draft;
    private List<Crop> suggestions = [];
    private ScoreField[]? beforeDetection;
    private ScoreField? Selected => FieldList.SelectedItem as ScoreField;

    public MainWindow()
    {
        InitializeComponent();
        UpdatePreviewBackground();
        var work=SystemParameters.WorkArea;
        Width=Math.Min(1440,work.Width);Height=Math.Min(940,work.Height);
        MinWidth=Math.Min(900,work.Width);MinHeight=Math.Min(580,work.Height);
        WindowState=WindowState.Maximized;
        FieldList.ItemsSource = fields;
        OutputCards.ItemsSource = cards;
        ready = true;
        WorkspaceTabs.SelectionChanged+=(_,e)=>{if(e.Source==WorkspaceTabs){PopulateOutputInspector();UpdateCompositionView();}};
        CompositionViewbox.SizeChanged+=(_,_)=>RenderCompositionBoxes();
        WorkspaceTabs.SizeChanged+=(_,_)=>
        {
            CompositionViewbox.MaxHeight=Math.Clamp(WorkspaceTabs.ActualHeight-360,150,420);
            FieldToolsScroll.MaxHeight=Math.Clamp(WorkspaceTabs.ActualHeight*.25,110,220);
        };
        SizeChanged+=(_,_)=>
        {
            bool compact=ActualWidth<1200;
            SourceColumn.Width=new GridLength(compact?190:225);
            InspectorColumn.Width=new GridLength(compact?280:310);
            HeaderBrandText.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            HeaderLogo.Width=compact?72:106;
        };
        SyncFields();
        SetupTitle.Text = setupName;
        timer.Tick += (_, _) => RefreshPreview();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            bool available = await Task.Run(NdiRuntime.Initialize);
            if (available) discovery = new NdiDiscovery();
            RefreshSources();
            PushPlan(); timer.Start();
            var args = Environment.GetCommandLineArgs();
            CompleteUpdateStartup(args);
            int colorIndex=Array.IndexOf(args,"--color-smoke-test");
            if(colorIndex>=0&&args.Length>colorIndex+1){await ColorSmokeTest(args[colorIndex+1]);return;}
            int compositionIndex=Array.IndexOf(args,"--composition-smoke-test");
            if(compositionIndex>=0&&args.Length>compositionIndex+1){await CompositionSmokeTest(args[compositionIndex+1]);return;}
            if(args.Contains("--update-test-exit")) {dirty=false;Close();return;}
            int updateIndex=Array.IndexOf(args,"--update-smoke-test");
            if(updateIndex>=0&&args.Length>updateIndex+1) {await UpdateSmokeTest(args[updateIndex+1]);return;}
            int index = Array.IndexOf(args, "--smoke-test");
            if (index >= 0 && args.Length > index + 1) await SmokeTest(args[index + 1]);
            int videoIndex = Array.IndexOf(args, "--video-smoke-test");
            if (videoIndex >= 0 && args.Length > videoIndex + 1) await VideoSmokeTest(args[videoIndex + 1]);
        }
        catch (Exception ex) { Notice.Text = ex.Message; }
    }

    private void PushPlan() => engine.Configure(new(kind, source, still, fields.Select(f => f.Snapshot()).ToArray(), output, generation, perspective with { }, video, stabilization,outputSettings with {}));
    private void Changed(bool preserveInitialPlacement = false)
    {
        CancelDetection(cancelInitial: !preserveInitialPlacement);
        dirty = true;
        SetupTitle.Text = setupName + " • Unsaved changes";
        PushPlan();
    }
    private void SyncFields()
    {
        PlaceNewOutputFields();
        var selected = Selected;
        FieldList.Items.Refresh();
        if (selected is not null && fields.Contains(selected)) FieldList.SelectedItem = selected;
        else if (fields.Count > 0) FieldList.SelectedIndex = 0;
        cards.Clear();
        foreach (var field in fields) cards.Add(new() { Id = field.Id, Name = field.Name, Output = VideoEngine.OutputName(kind, field.Name) });
        Inspector.IsEnabled = Selected is not null && !locked;
        EmptyFieldsText.Visibility = fields.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderBoxes();
        SyncComposition();
    }
    private void RefreshSources()
    {
        if (discovery is null) { DiscoveryText.Text = "Load an image or video, or install the NDI runtime to connect a live feed."; return; }
        try
        {
            var selected = (SourceCombo.SelectedItem as NdiSource)?.Name;
            var found = discovery.GetSources().Where(s => !s.Name.Contains("(Scoreboard - ") && !s.Name.Contains("(Scoreboard STILL - ")).ToList();
            SourceCombo.ItemsSource = found;
            SourceCombo.SelectedItem = found.FirstOrDefault(s => s.Name == selected) ?? found.FirstOrDefault();
            DiscoveryText.Text = found.Count == 0 ? "No sources found. Enable NDI on your vMix scoreboard output." : $"{found.Count} NDI source{(found.Count == 1 ? "" : "s")} available";
        }
        catch (Exception ex) { DiscoveryText.Text = ex.Message; }
    }
    private void RefreshPreview()
    {
        if (video is not null)
        {
            PauseVideoButton.Content = video.IsPlaying ? "Pause" : "Play";
            VideoTime.Text = $"{video.Position:mm\\:ss} / {video.Duration:mm\\:ss}";
        }
        var preview = engine.Preview;
        RefreshCompositionPreview();
        if(!initialPlacementFinished && fields.Count==0 && preview.Generation==generation && preview.Healthy && preview.Source is not null && !locked && !drawing && !EditingPerspective)
        {
            initialPlacementFinished=true;
            _=PlaceInitialFields(generation,detectionRevision);
        }
        EmptyFieldsText.Text=preview.Healthy ? "Use + Draw a field to add a box, or Find fields to rescan. Your boxes stay where you put them." : "Load an image or video, or connect NDI. A fresh setup finds its initial fields once.";
        StabilizationStatus.Text = preview.StabilizationStatus;
        SignalText.Text = preview.Status;
        SignalBanner.Background = Brush(kind == "None" ? "#17304A" : preview.Healthy ? kind == "NDI" && preview.TrackingHealthy ? "#173D32" : "#40331E" : "#4C2429");
        SignalText.Foreground = Brush(kind == "None" ? "#A8D6FF" : preview.Healthy ? kind == "NDI" && preview.TrackingHealthy ? "#8EF2D0" : "#F2CA83" : "#FFB1B9");
        var displayFrame = EditingPerspective ? perspectiveFrame : preview.Source;
        EmptySourcePanel.Visibility = displayFrame is null ? Visibility.Visible : Visibility.Collapsed;
        if (displayFrame is { } frame)
        {
            if (VideoSurface.Width != frame.Width || VideoSurface.Height != frame.Height)
            { VideoSurface.Width = frame.Width; VideoSurface.Height = frame.Height; RenderBoxes(); }
            if(WorkspaceTabs.SelectedIndex==0)UpdateImage(SourceImage,frame);
            SourceImage.Opacity = preview.Healthy ? 1 : .3;
            FrameInfo.Text = $"{frame.Width} × {frame.Height}  •  {fields.Count} fields  •  {preview.ProcessingMs:0} ms processing";
        }
        else { UpdateImage(SourceImage,null);UpdateImage(OriginalPreview,null); FrameInfo.Text = "Waiting for a video source"; }
        if(WorkspaceTabs.SelectedIndex==1 && ComparisonSection.IsExpanded)
        {
            ComparisonFieldName.Text=Selected?.Name ?? "No field selected";
            bool hasComparison=Selected is {} && preview.Healthy && preview.Source is not null;
            ComparisonEmpty.Visibility=hasComparison?Visibility.Collapsed:Visibility.Visible;
            ComparisonEmpty.Text=Selected is null?"Select a field on the left to compare its original crop and cleaned digits.":"Load media or connect a source to preview this field.";
            ComparisonPanels.Visibility=hasComparison?Visibility.Visible:Visibility.Collapsed;
            UpdateImage(OriginalPreview,hasComparison?FrameProcessor.Crop(preview.Source!,Selected!.Region):null);
            UpdateImage(CleanPreview,hasComparison&&preview.Fields.TryGetValue(Selected!.Id,out var clean)?clean:null);
        }
        else { UpdateImage(OriginalPreview,null);UpdateImage(CleanPreview,null); }
        if (WorkspaceTabs.SelectedIndex == 1)
            foreach (var card in cards)
            {
                card.Image = preview.Fields.TryGetValue(card.Id, out var fieldFrame) ? UpdateBitmap(card.Image,fieldFrame) : null;
                var field = fields.First(f => f.Id == card.Id);
                card.Status = !field.Enabled ? "DISABLED" : !output ? "STOPPED" : !preview.Healthy ? "NO SIGNAL" : preview.OutputCount > 0 ? preview.TrackingHealthy ? "SENDING" : "SENDING / CHECK ALIGNMENT" : "STARTING";
            }
        OutputStatus.Text = preview.OutputCount > 0 ? $"{preview.OutputCount} NDI OUTPUTS   •   {(preview.Healthy ? preview.TrackingHealthy ? "SENDING" : "SENDING / CHECK ALIGNMENT" : "TRANSPARENT / NO SIGNAL")}" : output ? "OUTPUTS STARTING / CHECK STATUS" : "OUTPUTS STOPPED";
        if (++ticks % 30 == 0 && !SourceCombo.IsDropDownOpen) RefreshSources();
    }
    private static BitmapSource Bitmap(VideoFrame frame)
    {
        var result = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels, frame.Stride);
        result.Freeze(); return result;
    }
    private static BitmapSource UpdateBitmap(BitmapSource? current, VideoFrame frame)
    {
        var bitmap=current as WriteableBitmap;
        if(bitmap is null || bitmap.PixelWidth!=frame.Width || bitmap.PixelHeight!=frame.Height)
            bitmap=new WriteableBitmap(frame.Width,frame.Height,96,96,PixelFormats.Bgra32,null);
        bitmap.WritePixels(new Int32Rect(0,0,frame.Width,frame.Height),frame.Pixels,frame.Stride,0);
        return bitmap;
    }
    private static void UpdateImage(Image image,VideoFrame? frame)
    {
        if(ReferenceEquals(image.Tag,frame))return;
        image.Tag=frame;
        image.Source=frame is null ? null : UpdateBitmap(image.Source as BitmapSource,frame);
    }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private void SwitchSource(string newKind, NdiSource? newSource = null, VideoFrame? image = null)
    {
        CancelDetection(cancelInitial:false);
        ResetStabilization();
        video?.Dispose(); video = null; videoPath = null; VideoControls.Visibility = Visibility.Collapsed;
        output = false; OutputButton.Content = "Start NDI outputs";
        kind = newKind; source = newSource; still = image; generation++;
        stillPath = null;
        EndPerspectiveEditing();
        suggestions.Clear(); AcceptDetection.Visibility = Visibility.Collapsed; beforeDetection = null; beforeDetectionPerspective = null;
        UndoDetection.Content = "Undo detection"; UndoDetection.Visibility = Visibility.Collapsed;
        drawing = false; DrawButton.Content = "+ Draw a field";
        SyncFields(); Changed(preserveInitialPlacement:true);
        Notice.Text = newKind == "NDI" ? "Source connected. Review every crop before starting outputs." : newKind == "Image" ? "Image loaded. Draw fields or load a saved setup." : "Video loaded. Draw fields or load a saved setup.";
        if (perspective.Enabled) Notice.Text += " Perspective correction is retained; review its corners for this source.";
    }
    private void RefreshClick(object sender, RoutedEventArgs e) => RefreshSources();
    private void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (locked) return;
        if (SourceCombo.SelectedItem is NdiSource chosen) SwitchSource("NDI", chosen);
        else Notice.Text = "Select an NDI source first. Refresh to scan again.";
    }
    private void LoadMediaClick(object sender, RoutedEventArgs e)
    {
        if (locked) return;
        var dialog = new OpenFileDialog { Filter = "Images and videos|*.png;*.jpg;*.jpeg;*.bmp;*.mp4;*.m4v;*.mov;*.wmv;*.avi|Images|*.png;*.jpg;*.jpeg;*.bmp|Videos|*.mp4;*.m4v;*.mov;*.wmv;*.avi", Title = "Load image or video" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            LoadMedia(dialog.FileName);
        }
        catch (Exception ex) { Notice.Text = "Could not load file: " + ex.Message; }
    }
    private void LoadMedia(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()))
        { SwitchSource("Image", image: LoadImage(path)); stillPath = path; }
        else
        {
            var next = new VideoFileSource(path);
            SwitchSource("Video"); video = next; videoPath = path;
            video.Loop = LoopVideo.IsChecked == true;
            MediaFileLabel.Text = System.IO.Path.GetFileName(path); MediaFileLabel.ToolTip = path;
            VideoControls.Visibility = Visibility.Visible; PushPlan();
        }
    }
    private void PauseVideoClick(object sender, RoutedEventArgs e) { if (!locked) video?.TogglePause(); }
    private void RestartVideoClick(object sender, RoutedEventArgs e) { if (!locked) video?.Restart(); }
    private void LoopVideoClick(object sender, RoutedEventArgs e) { if (!locked && video is not null) video.Loop = LoopVideo.IsChecked == true; }
    private void FieldSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        PopulateInspector(); RenderBoxes();PopulateOutputInspector();RenderCompositionBoxes();
    }
    private void PopulateInspector()
    {
        updating = true;
        Inspector.IsEnabled = Selected is not null && !locked;
        if (Selected is { } field)
        {
            FieldName.Text = field.Name; FieldEnabled.IsChecked = field.Enabled; PopulateCrop();
            var c = field.Cleanup;
            IsolationMode.SelectedIndex = c.Isolation == "White highlights" ? 1 : 0;
            RemoveBackground.IsChecked = c.RemoveBackground; Threshold.Value = c.Threshold; Softness.Value = c.Softness;
            Brightness.Value = c.Brightness; Thickness.Value = c.Thickness; SmoothEdges.IsChecked = c.Smooth; Speckles.IsChecked = c.RemoveSpeckles;
            DigitColor.SelectedIndex = c.Color == "Original" ? 1 : c.Color == "Custom" ? 2 : 0;
        }
        updating = false; UpdateSliderLabels();PopulateOutputInspector();
    }
    private void UpdateSliderLabels()
    {
        if (!ready) return;
        ThresholdValue.Text = $"{Threshold.Value:0}"; SoftnessValue.Text = $"{Softness.Value:0}";
        BrightnessValue.Text = $"{Brightness.Value:0.00}×"; ThicknessValue.Text = $"{Thickness.Value:+0;-0;0} px";
        RefreshColorSwatches();
    }
    private void ReadCleanup()
    {
        if (!ready || updating || locked || Selected is not { } field) return;
        field.Cleanup = new Cleanup { Threshold = (int)Threshold.Value, Softness = (int)Softness.Value, Brightness = Brightness.Value,
            Isolation = IsolationMode.SelectedIndex == 1 ? "White highlights" : "Bright colors",
            Thickness = (int)Thickness.Value, Smooth = SmoothEdges.IsChecked == true, RemoveSpeckles = Speckles.IsChecked == true,
            RemoveBackground = RemoveBackground.IsChecked == true, Color = ((ComboBoxItem)DigitColor.SelectedItem).Content.ToString()!, CustomColor = field.Cleanup.CustomColor }.Validated();
        field.OutputColor=null;
        field.Enabled = FieldEnabled.IsChecked == true;
        UpdateSliderLabels(); Changed();RenderCompositionBoxes();
    }
    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ReadCleanup();
    private void CleanupChanged(object sender, RoutedEventArgs e) => ReadCleanup();
    private void ColorChanged(object sender, SelectionChangedEventArgs e) => ReadCleanup();
    private void RenameField(object sender, RoutedEventArgs e)
    {
        if (!ready || updating || locked || Selected is not { } field) return;
        string name = Layout.CleanName(FieldName.Text);
        if (fields.Any(f => f.Id != field.Id && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) { FieldName.Text = field.Name; Notice.Text = "Each field needs a unique name."; return; }
        FieldName.Text = name;
        if (field.Name == name) return;
        field.Name = name; SyncFields(); Changed();
    }
    private void NameKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { RenameField(sender, e); Keyboard.ClearFocus(); } }
    private void ApplyAllClick(object sender, RoutedEventArgs e)
    {
        if (locked || Selected is not { } field) return;
        foreach (var f in fields) { f.Cleanup = field.Cleanup with { }; f.OutputColor=null; }
        Changed(); Notice.Text = "Cleanup settings applied to every field.";
    }
    private void ResetCleanupClick(object sender, RoutedEventArgs e) { if (!locked && Selected is { } f) { f.Cleanup = new(); f.OutputColor=null; PopulateInspector(); Changed(); } }
    private void OutputsTabClick(object sender, RoutedEventArgs e) => WorkspaceTabs.SelectedIndex = 1;
    private void ComparisonChanged(object sender, RoutedEventArgs e) { if(ready)RefreshPreview(); }
    private void ComparisonSizeChanged(object sender, SizeChangedEventArgs e)
    { ComparisonPanels.Columns=e.NewSize.Width<520?1:2; }
    private async void DetectClick(object sender, RoutedEventArgs e)
    {
        if (locked || engine.Preview.Source is not { } frame || !engine.Preview.Healthy) { Notice.Text = "A healthy source is needed to detect fields."; return; }
        int sourceGeneration = generation;
        CancelDetection();
        int revision = detectionRevision;
        DetectButton.IsEnabled = false; DetectionHint.Text = "Looking for groups of bright digits…";
        try
        {
            int cutoff = (int)DetectionThreshold.Value;
            string isolation = DetectionIsolation.SelectedIndex == 1 ? "White highlights" : "Bright colors";
            var detected = await Task.Run(() => FieldDetector.Detect(frame, cutoff, isolation));
            if (sourceGeneration != generation || revision != detectionRevision || locked || drawing || EditingPerspective) return;
            suggestions = detected;
            AcceptDetection.Visibility = suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DetectionHint.Text = suggestions.Count > 0 ? $"{suggestions.Count} suggested fields in amber. Review before replacing your boxes; then name each field." : "No fields found. Adjust background cutoff or draw fields manually.";
            RenderBoxes();
        }
        catch (Exception ex) { DetectionHint.Text = "Detection failed: " + ex.Message; }
        finally { DetectButton.IsEnabled = !locked && !EditingPerspective; }
    }
    private void AcceptDetectionClick(object sender, RoutedEventArgs e)
    {
        if (locked || suggestions.Count == 0) return;
        beforeDetection = fields.Select(f => f.Snapshot()).ToArray();
        beforeDetectionPerspective = null; UndoDetection.Content = "Undo detection";
        fields.Clear();
        for (int i = 0; i < suggestions.Count; i++) fields.Add(new() {
            Name = $"Field {i + 1}", Region = suggestions[i],
            Cleanup = new() { Isolation = DetectionIsolation.SelectedIndex == 1 ? "White highlights" : "Bright colors", Threshold = (int)DetectionThreshold.Value }
        });
        suggestions.Clear(); AcceptDetection.Visibility = Visibility.Collapsed; UndoDetection.Visibility = Visibility.Visible;
        DetectionHint.Text = "Name each field and check the full digit area. Undo restores your previous fields.";
        SyncFields(); Changed();
    }
    private void UndoDetectionClick(object sender, RoutedEventArgs e)
    {
        if (locked || beforeDetection is null) return;
        CancelDetection();
        bool restoringCorrection = beforeDetectionPerspective is not null;
        if (beforeDetectionPerspective is {} previousView)
        {
            perspective = previousView; beforeDetectionPerspective = null;
            ResetStabilization(); output = false; OutputButton.Content = "Start NDI outputs";
            UpdatePerspectiveStatus();
        }
        fields.Clear(); foreach (var f in beforeDetection) fields.Add(f); beforeDetection = null;
        UndoDetection.Content = "Undo detection";
        UndoDetection.Visibility = Visibility.Collapsed; DetectionHint.Text = restoringCorrection ? "Previous view and fields restored." : "Previous fields restored.";
        SyncFields(); PopulateInspector(); Changed(); Notice.Text = DetectionHint.Text;
    }
    private void DrawClick(object sender, RoutedEventArgs e)
    {
        if (locked) return;
        if (engine.Preview.Source is null || !engine.Preview.Healthy) { Notice.Text = "Connect a source or load an image or video before drawing fields."; return; }
        CancelDetection();
        if (fields.Count >= 24) { Notice.Text = "This version supports up to 24 fields."; return; }
        drawing = !drawing; DrawButton.Content = drawing ? "Cancel drawing" : "+ Draw a field";
        WorkspaceTabs.SelectedIndex = 0; BoxCanvas.Cursor = drawing ? Cursors.Cross : Cursors.Arrow;
        Notice.Text = drawing ? "Drag a rectangle around a scoreboard field." : "Drawing canceled.";
    }
    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        if (locked || Selected is not { } f) return;
        fields.Remove(f); SyncFields(); Changed();
    }
    private Point NormalPoint(MouseEventArgs e)
    { var p = e.GetPosition(BoxCanvas); return new(Math.Clamp(p.X / VideoSurface.Width, 0, 1), Math.Clamp(p.Y / VideoSurface.Height, 0, 1)); }
    private void CanvasDown(object sender, MouseButtonEventArgs e)
    {
        if (locked || engine.Preview.Source is null) return;
        CancelDetection();
        dragStart = NormalPoint(e); dragOriginal = null;
        if (EditingPerspective)
        {
            var corners = perspectiveDraft!.Corners();
            int? hitCorner=null;
            for(var node=e.OriginalSource as DependencyObject;node is not null && node!=BoxCanvas;node=VisualTreeHelper.GetParent(node))
                if(node is FrameworkElement {Tag:int index}) {hitCorner=index;break;}
            double nearest = double.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                double distance = Math.Pow((corners[i].X-dragStart.X)*VideoSurface.Width,2) + Math.Pow((corners[i].Y-dragStart.Y)*VideoSurface.Height,2);
                if (distance < nearest) { nearest = distance; cornerDragging = i; }
            }
            if(hitCorner is int direct)cornerDragging=direct;
            else if (nearest > Math.Pow(CanvasUnits(24), 2)) { cornerDragging = -1; return; }
            selectedCorner=cornerDragging;BoxCanvas.Focus();
            dragging = true; BoxCanvas.CaptureMouse(); e.Handled = true; return;
        }
        if (!drawing)
        {
            double hitX=CanvasUnits(FieldCornerGrabRadius)/VideoSurface.Width, hitY=CanvasUnits(FieldCornerGrabRadius)/VideoSurface.Height;
            var field = Selected is {} active && HitResizeCorner(active.Region,dragStart,hitX,hitY)>=0 ? active :
                fields.Reverse().FirstOrDefault(f => HitResizeCorner(f.Region,dragStart,hitX,hitY)>=0 || ContainsPoint(f.Region,dragStart));
            if (field is null) return;
            FieldList.SelectedItem = field; dragOriginal = field.Region;
            resizeCorner = HitResizeCorner(field.Region,dragStart,hitX,hitY);
            BoxCanvas.Cursor=FieldDragCursor(resizeCorner);
        }
        dragging = true; BoxCanvas.CaptureMouse(); e.Handled = true;
    }
    private void CanvasMove(object sender, MouseEventArgs e)
    {
        if (locked) return;
        var p = NormalPoint(e);
        if (!dragging)
        {
            if(drawing || EditingPerspective)return;
            double hitX=CanvasUnits(FieldCornerGrabRadius)/VideoSurface.Width,hitY=CanvasUnits(FieldCornerGrabRadius)/VideoSurface.Height;
            var hovered=Selected is {} active && HitResizeCorner(active.Region,p,hitX,hitY)>=0 ? active :
                fields.Reverse().FirstOrDefault(f=>HitResizeCorner(f.Region,p,hitX,hitY)>=0 || ContainsPoint(f.Region,p));
            BoxCanvas.Cursor=hovered is null?Cursors.Arrow:FieldDragCursor(HitResizeCorner(hovered.Region,p,hitX,hitY));
            return;
        }
        if (EditingPerspective && cornerDragging >= 0)
        {
            MoveCorner(cornerDragging,new(p.X,p.Y));return;
        }
        if (drawing) draft = new(Math.Min(p.X, dragStart.X), Math.Min(p.Y, dragStart.Y), Math.Abs(p.X - dragStart.X), Math.Abs(p.Y - dragStart.Y));
        else if (Selected is { } f && dragOriginal is { } r)
        {
            double dx = p.X - dragStart.X, dy = p.Y - dragStart.Y;
            f.Region = ResizeOrMoveBox(r,dx,dy,resizeCorner,.005,.005);
            PopulateCrop(); PushPlan();
        }
        RenderBoxes();
    }
    private void CanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        if (EditingPerspective) { dragging=false; cornerDragging=-1; BoxCanvas.ReleaseMouseCapture(); return; }
        if (drawing && draft is { Width: > .005, Height: > .005 } region && fields.Count < 24)
        {
            int number = 1; while (fields.Any(f => f.Name.Equals($"Field {number}", StringComparison.OrdinalIgnoreCase))) number++;
            var field = new ScoreField { Name = $"Field {number}", Region = region.Clamp() }; fields.Add(field); SyncFields(); FieldList.SelectedItem = field;
        }
        dragging = false; drawing = false; draft = null; DrawButton.Content = "+ Draw a field";
        BoxCanvas.ReleaseMouseCapture(); BoxCanvas.Cursor = Cursors.Arrow; RenderBoxes(); Changed();
    }
    private void CanvasLostCapture(object sender, MouseEventArgs e) { if (e.OriginalSource==BoxCanvas && dragging) { dragging = false; cornerDragging=-1; draft = null; BoxCanvas.Cursor=Cursors.Arrow; RenderBoxes(); if (!EditingPerspective) Changed(); } }
    private void RenderBoxes()
    {
        if (!ready) return;
        BoxCanvas.Children.Clear();
        if (locked) return;
        if (EditingPerspective) { DrawPerspectiveCorners(); return; }
        foreach (var field in fields) DrawBox(field.Region, field.Name, field == Selected ? "#62F0C5" : "#6E9BC4", false, field == Selected);
        for (int i = 0; i < suggestions.Count; i++) DrawBox(suggestions[i], $"Suggestion {i+1}", "#FFCB72", true, false);
        if (draft is { } r) DrawBox(r, "New field", "#FFFFFF", true, false);
    }
    private void DrawBox(Crop crop, string label, string color, bool dashed, bool selected)
    {
        double w = VideoSurface.Width, h = VideoSurface.Height;
        var rectangle = new Rectangle { Width = Math.Max(1, crop.Width * w), Height = Math.Max(1, crop.Height * h), Stroke = Brush(color), StrokeThickness = selected ? 4 : 2, Fill = new SolidColorBrush(Color.FromArgb(15, 100, 220, 180)), IsHitTestVisible = false };
        if (dashed) rectangle.StrokeDashArray = [5, 3];
        Canvas.SetLeft(rectangle, crop.X * w); Canvas.SetTop(rectangle, crop.Y * h); BoxCanvas.Children.Add(rectangle);
        var text = new Border { Background = Brush(color), Padding = new Thickness(7, 3, 7, 3), CornerRadius = new CornerRadius(3), IsHitTestVisible = false,
            Child = new TextBlock { Text = label, Foreground = Brush("#0D1822"), FontSize = Math.Max(18, w / 64), FontWeight = FontWeights.SemiBold } };
        Canvas.SetLeft(text, crop.X * w); Canvas.SetTop(text, Math.Max(0, crop.Y * h - 34)); BoxCanvas.Children.Add(text);
        if (selected)
        {
            DrawResizeGrips(BoxCanvas,crop.X*w,crop.Y*h,crop.Width*w,crop.Height*h,CanvasUnits(1),color);
        }
    }

    private void OutputClick(object sender, RoutedEventArgs e)
    {
        if (perspectiveDetectionPending) { Notice.Text = "Wait for the corrected-view fields to finish updating before starting outputs."; return; }
        if (!output)
        {
            if (!NdiRuntime.Available) { Notice.Text = NdiRuntime.Status; return; }
            if (EditingPerspective) { Notice.Text = "Apply or cancel the perspective correction before starting outputs."; return; }
            if (!engine.Preview.Healthy || !fields.Any(f => f.Enabled)) { Notice.Text = "Select a healthy source and enable at least one field first."; return; }
            if(outputSettings.Mode=="Combined"&&!fields.Any(f=>f.Enabled&&f.OutputRegion is not null)){Notice.Text="Place at least one enabled field on the Outputs canvas before starting the combined feed.";WorkspaceTabs.SelectedIndex=1;return;}
        }
        output = !output; OutputButton.Content = output ? "Stop NDI outputs" : "Start NDI outputs"; PushPlan();
        Notice.Text = output ? "NDI outputs are starting. Open the Outputs tab for the names to select in vMix." : "NDI outputs stopped.";
    }
    private void LiveClick(object sender, RoutedEventArgs e)
    {
        if (perspectiveDetectionPending) { Notice.Text = "Wait for the corrected-view fields to finish updating before locking for live."; return; }
        if (EditingPerspective) { Notice.Text = "Apply or cancel the perspective correction before locking for live."; return; }
        locked = !locked;
        LiveButton.Content = locked ? "Unlock editing" : "Lock for live";
        SourceControls.IsEnabled = FieldActions.IsEnabled = DetectionControls.IsEnabled = !locked;
        OutputMode.IsEnabled=OutputLayoutControls.IsEnabled=PlacementActions.IsEnabled=!locked;
        Inspector.IsEnabled = !locked && Selected is not null;
        if (locked) { drawing = false; draft = null; BoxCanvas.ReleaseMouseCapture(); WorkspaceTabs.SelectedIndex = 1; }
        CompositionCanvas.ReleaseMouseCapture();RenderCompositionBoxes();
        RenderBoxes(); Notice.Text = locked ? "Live lock is on. Source and field settings are protected; outputs can still be stopped." : "Editing unlocked.";
    }
    private bool SaveSetup()
    {
        if (perspectiveDetectionPending) { Notice.Text = "Wait for the corrected-view fields to finish updating before saving."; return false; }
        if (EditingPerspective) { Notice.Text = "Apply or cancel the perspective correction before saving."; return false; }
        RenameField(this, new());
        if (savePath is null)
        {
            var dialog = new SaveFileDialog { Filter = "Scoreboard Studio setup|*.scoreboard.json", FileName = "scoreboard-setup.scoreboard.json", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return false;
            savePath = dialog.FileName;
        }
        try
        {
            setupName = System.IO.Path.GetFileName(savePath).Replace(".scoreboard.json", "");
            new Layout { Name = setupName, SourceName = source?.Name, Perspective = perspective with { },Output=SavedOutput(savePath),
                ImagePath = stillPath is null ? null : System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(savePath)!, stillPath),
                VideoPath = videoPath is null ? null : System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(savePath)!, videoPath),
                Fields = fields.Select(f => f.Snapshot()).ToList() }.Save(savePath);
            dirty = false; SetupTitle.Text = setupName + " • Saved"; Notice.Text = "Setup saved: " + savePath; return true;
        }
        catch (Exception ex) { Notice.Text = "Could not save: " + ex.Message; return false; }
    }
    private void SaveClick(object sender, RoutedEventArgs e) => SaveSetup();
    private void SaveAsClick(object sender, RoutedEventArgs e)
    {
        var previous = savePath;
        savePath = null;
        if (!SaveSetup()) savePath = previous;
    }
    private bool ConfirmDiscard()
    {
        if (!dirty) return true;
        var choice = MessageBox.Show(this, "Save your setup changes before continuing?", "Unsaved setup", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return choice == MessageBoxResult.No || choice == MessageBoxResult.Yes && SaveSetup();
    }
    private void LoadClick(object sender, RoutedEventArgs e)
    {
        if (locked) { Notice.Text = "Unlock editing before loading a setup."; return; }
        var dialog = new OpenFileDialog { Filter = "Scoreboard Studio setup|*.scoreboard.json;*.json" };
        if (dialog.ShowDialog(this) != true || !ConfirmDiscard()) return;
        try
        {
            OpenLayout(dialog.FileName);
        }
        catch (Exception ex) { Notice.Text = "Could not load setup: " + ex.Message; }
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!restartingForUpdate && Application.Current is not App {FatalError:true} && !ConfirmDiscard()) { e.Cancel = true; return; }
        CancelDetection();timer.Stop(); video?.Dispose(); discovery?.Dispose(); engine.Dispose();
    }

    private async Task SmokeTest(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(200); RefreshPreview(); UpdateLayout();
            if(WindowState!=WindowState.Maximized)throw new Exception("App must start maximized.");
            WindowState=WindowState.Normal;
            double initialWidth=Width,initialHeight=Height;
            Width=1000;Height=650;await Task.Delay(100);UpdateLayout();
            if(ActualWidth>1001 || ActualHeight>651 || ResizeMode!=ResizeMode.CanResizeWithGrip)throw new Exception("Window cannot resize to laptop dimensions.");
            CaptureWindow(System.IO.Path.Combine(directory,"laptop-window.png"));
            MinimizeClick(this,new());if(WindowState!=WindowState.Minimized)throw new Exception("Minimize failed.");
            WindowState=WindowState.Normal;Width=initialWidth;Height=initialHeight;await Task.Delay(100);UpdateLayout();
            if (kind != "None" || fields.Count != 0 || engine.Preview.Source is not null || engine.Preview.Healthy || engine.Preview.OutputCount != 0 || output || dirty || Inspector.IsEnabled || EmptySourcePanel.Visibility != Visibility.Visible)
                throw new Exception("Startup must wait for a source with no fields or outputs.");
            DrawClick(this, new());
            if (drawing || fields.Count != 0) throw new Exception("Drawing was allowed without a source.");
            CaptureWindow(System.IO.Path.Combine(directory,"startup.png"));
            var testArgs = Environment.GetCommandLineArgs();
            int fixtureIndex = Array.IndexOf(testArgs,"--test-layout");
            string? fixturePath = fixtureIndex >= 0 && testArgs.Length > fixtureIndex+1 ? testArgs[fixtureIndex+1] : null;
            if (fixturePath is null)
            {
                File.WriteAllText(System.IO.Path.Combine(directory, "ui-checks.txt"), "PASS: empty startup, no source or outputs, disabled inspector, drawing blocked. SKIP: optional photo fixture is absent.\n");
                return;
            }
            var fixture=Layout.Load(fixturePath);
            string photoPath=System.IO.Path.GetFullPath(fixture.ImagePath!,System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(fixturePath))!);
            LoadMedia(photoPath);
            for(int i=0;i<100 && fields.Count==0;i++)await Task.Delay(100);
            if(fields.Count==0 || !initialPlacementFinished)throw new Exception("A fresh source did not place initial fields once.");
            int detectedCount=fields.Count;
            DrawClick(this,new());draft=new(.02,.02,.08,.08);dragging=true;
            CanvasUp(this,new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left));
            if(fields.Count!=detectedCount+1 || Selected?.Region!=new Crop(.02,.02,.08,.08))throw new Exception("Draw a field did not add a manual box.");
            RemoveClick(this,new());
            var acceptedFields=fields.Select(f=>f.Region).ToArray();
            DetectClick(this,new());
            fields[0].Region=new(.1,.1,.15,.2);Changed();
            await Task.Delay(300);
            if(suggestions.Count!=0 || AcceptDetection.Visibility==Visibility.Visible || fields[0].Region.X!=.1)throw new Exception("Pending detection overwrote manual edits.");
            fields[0].Region=acceptedFields[0];Changed();
            LoadMedia(photoPath);await Task.Delay(200);
            if(!fields.Select(f=>f.Region).SequenceEqual(acceptedFields))throw new Exception("Reloading the source replaced the setup.");
            await Task.Delay(200);RefreshPreview();UpdateLayout();CaptureWindow(System.IO.Path.Combine(directory,"automatic-image-fields.png"));
            fields.Clear();SyncFields();Changed();LoadMedia(photoPath);await Task.Delay(900);
            if(fields.Count!=0)throw new Exception("Deleting all fields and reopening media restarted automatic placement.");
            string emptyLayout=System.IO.Path.Combine(directory,"empty.scoreboard.json");
            new Layout {ImagePath=photoPath}.Save(emptyLayout);
            initialPlacementFinished=false; // Model loading an intentionally empty layout in a fresh session.
            OpenLayout(emptyLayout);await Task.Delay(900);
            if(fields.Count!=0 || !initialPlacementFinished)throw new Exception("An empty saved layout was automatically repopulated.");
            var pendingInitial=PlaceInitialFields(generation,detectionRevision);
            var manual=new ScoreField {Name="Manual during detection",Region=new(.1,.1,.1,.1)};
            fields.Add(manual);SyncFields();Changed();await pendingInitial;
            if(fields.Count!=1 || fields[0]!=manual)throw new Exception("An in-flight initial detection overwrote manual placement.");
            OpenLayout(fixturePath);
            for (int i = 0; i < 100 && engine.Preview.Fields.Count != 10; i++) await Task.Delay(100);
            if (engine.Preview.Fields.Count != 10) throw new Exception("Photo did not produce ten cleaned fields.");
            var originalField = fields[1].Snapshot();
            FieldList.SelectedIndex = 1; Threshold.Value = 120;
            if (fields[1].Cleanup.Threshold != 120) throw new Exception("Cleanup UI did not update the processing settings.");
            FieldName.Text = "Home points"; RenameField(this, new());
            if (fields[1].Name != "Home points") throw new Exception("Rename did not persist.");
            CropW.Value = 17; CropH.Value = 22; CropX.Value = 15; CropY.Value = 47;
            if (Math.Abs(fields[1].Region.X - .15) > .001) throw new Exception("Crop controls did not update.");
            fields[1].Name = originalField.Name; fields[1].Region = originalField.Region; fields[1].Cleanup = originalField.Cleanup;
            SyncFields(); PushPlan();
            FieldList.SelectedIndex = 0;
            if (FieldList.SelectedItems.Count != 1 || Selected != fields[0] || FieldName.Text != "Game clock") throw new Exception("Selection changed incorrectly after editing a field.");
            if(WorkspaceTabs.Items.Count!=2 || !ComparisonSection.IsExpanded)throw new Exception("Expected two tabs and an expanded comparison.");
            OutputMode.SelectedIndex=0;
            OutputsTabClick(this,new());
            if(WorkspaceTabs.SelectedIndex!=1 || OutputPlacementInspector.Visibility!=Visibility.Visible)throw new Exception("Outputs navigation or inspector did not follow the two-tab layout.");
            ComparisonSection.IsExpanded=true;RefreshPreview();
            if(OriginalPreview.Source is null || CleanPreview.Source is null || ComparisonFieldName.Text!=fields[0].Name)throw new Exception("Selected-field comparison did not render.");
            FieldList.SelectedIndex=1;RefreshPreview();
            if(ComparisonFieldName.Text!=fields[1].Name || !ReferenceEquals(CleanPreview.Tag,engine.Preview.Fields[fields[1].Id]))throw new Exception("Comparison did not follow field selection.");
            OutputMode.SelectedIndex=1;RefreshPreview();
            if(ComparisonPanels.Visibility!=Visibility.Visible || CleanPreview.Source is null)throw new Exception("Comparison unavailable in Separate Inputs mode.");
            OutputMode.SelectedIndex=0;
            Width=1000;Height=780;await Task.Delay(150);UpdateLayout();
            if(ComparisonPanels.Columns!=1)throw new Exception("Comparison did not stack in a narrow window.");
            CaptureWindow(System.IO.Path.Combine(directory,"comparison-small.png"));
            Width=1440;Height=940;await Task.Delay(150);UpdateLayout();
            if(ComparisonPanels.Columns!=2)throw new Exception("Comparison did not use two columns in a wide window.");
            FieldList.SelectedIndex=-1;RefreshPreview();
            if(OriginalPreview.Source is not null || CleanPreview.Source is not null || ComparisonEmpty.Visibility!=Visibility.Visible)throw new Exception("Comparison retained the previous field after deselection.");
            FieldList.SelectedIndex=0;
            ComparisonSection.IsExpanded=false;RefreshPreview();
            if(OriginalPreview.Source is not null || CleanPreview.Source is not null)throw new Exception("Collapsed comparison retained rendered frames.");
            for (int tab = 0; tab < 2; tab++)
            {
                WorkspaceTabs.SelectedIndex = tab; await Task.Delay(300); RefreshPreview(); UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(this); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(System.IO.Path.Combine(directory, $"ui-{tab + 1}.png")); encoder.Save(file);
            }
            LiveClick(this, new());
            if (Inspector.IsEnabled || SourceControls.IsEnabled || BoxCanvas.Children.Count != 0) throw new Exception("Live lock did not protect editing.");
            LiveClick(this, new());
            OpenLayout(fixturePath);
            for(int i=0;i<100 && engine.Preview.Fields.Count!=10;i++) await Task.Delay(100);
            if(kind!="Image" || fields.Count!=10 || IsolationMode.SelectedIndex!=1 || output) throw new Exception("Real scoreboard sample did not load as a stopped still image with white-highlight cleanup.");
            WorkspaceTabs.SelectedIndex=1;ComparisonSection.IsExpanded=true;await Task.Delay(150);RefreshPreview();UpdateLayout();
            ComparisonSection.BringIntoView();await Task.Delay(100);UpdateLayout();
            CaptureWindow(System.IO.Path.Combine(directory,"real-scoreboard-cleanup.png"));
            PerspectiveClick(this,new());
            if(!EditingPerspective || Inspector.IsEnabled || FieldActions.IsEnabled || output) throw new Exception("Perspective editor did not protect field geometry or stop outputs.");
            var frozen=perspectiveFrame;RefreshPreview();
            if(!ReferenceEquals(frozen,perspectiveFrame))throw new Exception("Perspective editing did not freeze its reference.");
            selectedCorner=0;BoxCanvas.Focus();var previousCorner=perspectiveDraft!.TopLeft;
            var keyboardEvent=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,0,Key.Right) {RoutedEvent=Keyboard.KeyDownEvent};
            BoxCanvas.RaiseEvent(keyboardEvent);
            if(Math.Abs(perspectiveDraft.TopLeft.X-previousCorner.X-1/VideoSurface.Width)>.000001)throw new Exception("Corner keyboard fine adjustment failed.");
            perspectiveDraft!.TopLeft=new(.025,.02);perspectiveDraft.TopRight=new(.985,.04);
            perspectiveDraft.BottomRight=new(.95,.98);perspectiveDraft.BottomLeft=new(.04,.96);
            RenderBoxes();UpdateLayout();CaptureWindow(System.IO.Path.Combine(directory,"perspective-editor.png"));
            PerspectiveWidth.Text="1280";PerspectiveHeight.Text="720";ApplyPerspectiveClick(this,new());
            await perspectiveDetectionTask;
            for(int i=0;i<100 && engine.Preview.Source?.Width!=1280;i++) await Task.Delay(100);
            if(!perspective.Enabled || EditingPerspective || engine.Preview.Source?.Width!=1280 || engine.Preview.RawSource?.Width!=1320) throw new Exception("Perspective correction did not reach the worker pipeline.");
            PerspectiveClick(this,new());perspectiveDraft!.TopLeft=new(.1,.1);CancelPerspectiveClick(this,new());
            if(perspective.TopLeft.X!=.025) throw new Exception("Cancel altered the accepted correction.");
            PerspectiveClick(this,new());ResetPerspectiveClick(this,new());
            await perspectiveDetectionTask;
            if(perspective.Enabled) throw new Exception("Perspective reset failed.");
            string? recovery=SaveRecovery();
            if(recovery is null || Layout.Load(recovery).Fields.Count!=fields.Count)throw new Exception("Recovery setup could not be restored.");
            var flatPixels=new byte[640*360*4];
            for(int i=0;i<flatPixels.Length;i+=4) {flatPixels[i]=flatPixels[i+1]=flatPixels[i+2]=100;flatPixels[i+3]=255;}
            engine.Configure(new("Image",null,new(640,360,flatPixels),fields.Select(f=>f.Snapshot()).ToArray(),false,generation+100,Stabilization:new(true,1)));
            for(int i=0;i<100 && !engine.Preview.Status.Contains("CHECK ALIGNMENT");i++)await Task.Delay(50);
            WorkspaceTabs.SelectedIndex=0;RefreshPreview();UpdateLayout();
            if(!engine.Preview.Healthy || engine.Preview.TrackingHealthy || SourceImage.Opacity!=1 || !SignalText.Text.Contains("CHECK ALIGNMENT"))throw new Exception("Tracking warning hid the live preview or reported a source outage.");
            CaptureWindow(System.IO.Path.Combine(directory,"tracking-warning.png"));
            PushPlan();await Task.Delay(200);
            if(testArgs.Contains("--update-ui-check"))
            {
                locked=true;CheckUpdatesClick(this,new());
                if(Application.Current.Windows.Count!=1)throw new Exception("Update dialog opened while live editing was locked.");
                locked=false;
                var updates=new UpdateWindow {Owner=this};updates.Show();
                for(int i=0;i<400 && updates.IsChecking;i++)await Task.Delay(100);
                if(!updates.StatusText.Contains("up to date"))throw new Exception("Update UI check failed: "+updates.StatusText);
                updates.UpdateLayout();
                var updateBitmap=new RenderTargetBitmap((int)updates.ActualWidth,(int)updates.ActualHeight,96,96,PixelFormats.Pbgra32);
                updateBitmap.Render(updates);var updateEncoder=new PngBitmapEncoder();updateEncoder.Frames.Add(BitmapFrame.Create(updateBitmap));
                using(var file=File.Create(System.IO.Path.Combine(directory,"update-window.png")))updateEncoder.Save(file);
                updates.Close();
            }
            File.WriteAllText(System.IO.Path.Combine(directory, "ui-checks.txt"), "PASS: initial automatic placement once; manual add/remove; no redetection after edits, deletion, reload or empty saved layout; pending detection cannot overwrite edits; laptop resize/minimize; cleanup, rename, crop, tabs, live lock; frozen perspective view, corner keyboard adjustment, perspective pipeline and recovery.\n");
        }
        catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(directory, "ui-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
        finally { dirty = false; Close(); }
    }
    private void CaptureWindow(string path)
    {
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path);encoder.Save(file);
    }
}

public sealed class OutputCard : INotifyPropertyChanged
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Output { get; init; } = "";
    private BitmapSource? image;
    public BitmapSource? Image { get => image; set { image = value; Notify(); } }
    private string status = "STOPPED";
    public string Status { get => status; set { if (status != value) { status = value; Notify(); } } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
