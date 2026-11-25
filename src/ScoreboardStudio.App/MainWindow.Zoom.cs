using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private Point? compositionCenter;
    private bool compositionPanning, zoomUpdating, navigationUpdating;
    private Point panStart, panCenter;
    private double panUnits;

    private void FrameComposition(Crop region,double padding)
    {
        compositionCenter=new(region.X+region.Width/2,region.Y+region.Height/2);
        // Every navigation action uses the same full-canvas zoom scale. Close-up
        // is a framing preset, never a crop that limits later zooming or panning.
        zoomUpdating=true;
        CompositionZoom.Value=Math.Clamp(1/(Math.Max(region.Width,region.Height)*padding),1,32);
        zoomUpdating=false;
    }

    private void UpdateOffscreenFields()
    {
        var outside=fields.Where(f=>f.OutputRegion is {} r &&
            (r.X+r.Width<=compositionView.X || r.Y+r.Height<=compositionView.Y ||
             r.X>=compositionView.X+compositionView.Width || r.Y>=compositionView.Y+compositionView.Height)).ToArray();
        OffscreenFieldsNotice.Visibility=outside.Length>0?Visibility.Visible:Visibility.Collapsed;
        OffscreenFieldsText.Text=outside.Length==1?"1 field is outside the main view. Use Find a field above.":$"{outside.Length} fields are outside the main view. Use Find a field above.";
        OffscreenFieldsText.ToolTip=string.Join(", ",outside.Select(f=>f.Name))+". All fields remain visible in the whole-canvas overview.";
    }

    private void UpdateCompositionNavigation()
    {
        navigationUpdating=true;
        try
        {
            void Sync(ScrollBar bar,double start,double extent)
            {
                bar.Maximum=Math.Max(0,1-extent);bar.ViewportSize=extent;
                bar.LargeChange=extent*.8;bar.Value=start;bar.IsEnabled=bar.Maximum>0;
            }
            Sync(CompositionHorizontalScroll,compositionView.X,compositionView.Width);
            Sync(CompositionVerticalScroll,compositionView.Y,compositionView.Height);
        }
        finally {navigationUpdating=false;}
    }

    private void NavigateComposition(Point center)
    {
        if(compositionDragging || compositionPanning)return;
        CloseUpGuide.IsChecked=false;compositionCenter=center;UpdateCompositionView();
    }

    private void CompositionScrollChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!ready || navigationUpdating)return;
        NavigateComposition(new(CompositionHorizontalScroll.Value+compositionView.Width/2,
            CompositionVerticalScroll.Value+compositionView.Height/2));
    }

    private void CompositionOverviewDown(object sender,MouseButtonEventArgs e)
    {
        var point=e.GetPosition(OverviewCanvas);
        NavigateComposition(new(point.X/outputSettings.Width,point.Y/outputSettings.Height));e.Handled=true;
    }

    private void FindCompositionFieldClick(object sender,RoutedEventArgs e)
    {
        if(sender is not Button {Tag:ScoreField field} || field.OutputRegion is not {} r)return;
        FieldList.SelectedItem=field;
        NavigateComposition(new(r.X+r.Width/2,r.Y+r.Height/2));
    }

    private void GoToGuideClick(object sender,RoutedEventArgs e)
    {
        if(scorebugGuide?.Bounds is {} r)NavigateComposition(new(r.X+r.Width/2,r.Y+r.Height/2));
    }

    private void RenderCompositionOverview()
    {
        CompositionOverview.Width=outputSettings.Width;CompositionOverview.Height=outputSettings.Height;
        OverviewCanvas.Children.Clear();
        double units=outputSettings.Width/168.0;
        void Box(Crop r,string color,double thickness)
        {
            var box=new Rectangle {Width=r.Width*outputSettings.Width,Height=r.Height*outputSettings.Height,
                Stroke=Brush(color),StrokeThickness=thickness*units,IsHitTestVisible=false};
            Canvas.SetLeft(box,r.X*outputSettings.Width);Canvas.SetTop(box,r.Y*outputSettings.Height);
            OverviewCanvas.Children.Add(box);
        }
        foreach(var field in fields)if(field.OutputRegion is {} r)Box(r,field==Selected?"#62F0C5":"#8BC7FF",1);
        Box(compositionView,"#FFFFFF",2);
        // Keep existing buttons while panning/dragging so focus and input are stable.
        var placed=fields.Where(f=>f.OutputRegion is not null).ToArray();
        if(!CompositionFieldLinks.Children.OfType<Button>().Select(b=>b.Tag).SequenceEqual(placed))
        {
            CompositionFieldLinks.Children.Clear();
            foreach(var field in placed)
            {
                var button=new Button {Tag=field,Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,6,4),MaxWidth=160};
                button.Click+=FindCompositionFieldClick;CompositionFieldLinks.Children.Add(button);
            }
        }
        foreach(var button in CompositionFieldLinks.Children.OfType<Button>())
        {
            var field=(ScoreField)button.Tag;
            if(button.Content is not TextBlock label || label.Text!=field.Name)
                button.Content=new TextBlock {Text=field.Name,TextTrimming=TextTrimming.CharacterEllipsis};
            button.ToolTip=$"Find {field.Name} at the current zoom. Its output position stays unchanged.";
        }
    }

    private Crop ZoomedCompositionView()
    {
        var center=compositionCenter ?? new Point(.5,.5);
        double w=1/CompositionZoom.Value,h=w;
        return new(Math.Clamp(center.X-w/2,0,1-w),Math.Clamp(center.Y-h/2,0,1-h),w,h);
    }

    private void ResetCompositionZoom()
    {
        zoomUpdating=true;CompositionZoom.Value=1;compositionCenter=null;zoomUpdating=false;
    }

    private void CompositionZoomChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!ready || zoomUpdating)return;
        CloseUpGuide.IsChecked=false;
        compositionCenter=new(compositionView.X+compositionView.Width/2,compositionView.Y+compositionView.Height/2);
        UpdateCompositionView();
    }

    private void FitCompositionClick(object sender,RoutedEventArgs e)
    {
        CloseUpGuide.IsChecked=false;ResetCompositionZoom();UpdateCompositionView();
    }

    private void ZoomSelectedClick(object sender,RoutedEventArgs e)
    {
        if(Selected?.OutputRegion is not {} r)return;
        // Leave space for labels and all four handles, including very small fields.
        CloseUpGuide.IsChecked=false;
        FrameComposition(r,1.6);
        UpdateCompositionView();
    }

    private void CompositionWheel(object sender,MouseWheelEventArgs e)
    {
        // Handle the whole preview, including its letterboxed margins, before
        // the surrounding page's ScrollViewer can consume the wheel event.
        e.Handled=true;
        if(compositionDragging || compositionPanning)return;
        ZoomCompositionAt(CompositionPoint(e),e.Delta);
    }

    private void ZoomCompositionAt(Point at,int delta)
    {
        double fx=Math.Clamp((at.X-compositionView.X)/compositionView.Width,0,1);
        double fy=Math.Clamp((at.Y-compositionView.Y)/compositionView.Height,0,1);
        at=new(compositionView.X+fx*compositionView.Width,compositionView.Y+fy*compositionView.Height);
        CloseUpGuide.IsChecked=false;
        zoomUpdating=true;
        CompositionZoom.Value=Math.Clamp(CompositionZoom.Value*Math.Pow(1.25,delta/120.0),1,32);
        zoomUpdating=false;
        compositionCenter=new(at.X+(.5-fx)/CompositionZoom.Value,at.Y+(.5-fy)/CompositionZoom.Value);
        UpdateCompositionView();
    }

    private void CompositionPanDown(object sender,MouseButtonEventArgs e)
    {
        if(compositionDragging || !(e.ChangedButton==MouseButton.Middle ||
            e.ChangedButton==MouseButton.Left && Keyboard.IsKeyDown(Key.Space)))return;
        CloseUpGuide.IsChecked=false;
        compositionPanning=true;
        panStart=e.GetPosition(OutputPreviewBorder);
        panCenter=new(compositionView.X+compositionView.Width/2,compositionView.Y+compositionView.Height/2);
        panUnits=CompositionUnits(1);
        CompositionCanvas.Focus();CompositionCanvas.CaptureMouse();CompositionCanvas.Cursor=Cursors.Hand;e.Handled=true;
    }

    private void PanComposition(MouseEventArgs e)
        => PanComposition(e.GetPosition(OutputPreviewBorder));

    private void PanComposition(Point point)
    {
        compositionCenter=new(panCenter.X-(point.X-panStart.X)*panUnits/outputSettings.Width,
            panCenter.Y-(point.Y-panStart.Y)*panUnits/outputSettings.Height);
        UpdateCompositionView();
        // Clamp the stored center too, so zooming out after reaching an edge stays predictable.
        compositionCenter=new(compositionView.X+compositionView.Width/2,compositionView.Y+compositionView.Height/2);
    }

    private void CompositionPanUp(object sender,MouseButtonEventArgs e)
    {
        if(!compositionPanning || e.ChangedButton is not (MouseButton.Middle or MouseButton.Left))return;
        compositionPanning=false;CompositionCanvas.ReleaseMouseCapture();CompositionCanvas.Cursor=Cursors.Arrow;e.Handled=true;
    }
}
