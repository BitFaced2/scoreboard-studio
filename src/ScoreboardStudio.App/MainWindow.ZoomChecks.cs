using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private async Task CheckTwoDistantFields(List<string> report,string directory)
    {
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        // Match the reported layout: two fields at the top, guide at the bottom.
        var pair=fields.Take(2).Select(f=>f.Snapshot()).ToArray();
        for(int i=0;i<pair.Length;i++)
        {
            pair[i].Name=i==0?"Field 2":"Field 4";
            pair[i].OutputRegion=new(.03+i*.33,0,.28,.22);pair[i].OutputSlot=null;pair[i].Enabled=true;
        }
        fields.Clear();foreach(var field in pair)fields.Add(field);
        FieldList.SelectedItem=pair[0];SyncFields();Changed();
        for(int i=0;i<100 && engine.Preview.Fields.Count!=2;i++)await Task.Delay(50);
        await Task.Delay(200);RefreshPreview();
        CloseUpGuide.IsChecked=true;CompositionViewChanged(CloseUpGuide,new());UpdateLayout();
        var view=compositionView;double zoom=CompositionZoom.Value;
        var positions=fields.Select(f=>(f.Region,f.OutputRegion)).ToArray();
        Require(OffscreenFieldsText.Text.StartsWith("2 fields") && CompositionFieldLinks.Children.Count==2,
            "Two distant fields were not discoverable from guide close-up.");
        var overview=(BitmapSource)OverviewFields.Source;
        var overviewPixels=new byte[overview.PixelWidth*overview.PixelHeight*4];overview.CopyPixels(overviewPixels,overview.PixelWidth*4,0);
        Require(overviewPixels.Where((_,i)=>i%4==3).Any(a=>a>200),"Distant fields disappeared from the overview.");
        var outputScroll=(ScrollViewer)((TabItem)WorkspaceTabs.Items[1]).Content;
        outputScroll.ScrollToVerticalOffset(outputScroll.VerticalOffset+CombinedPanel.TranslatePoint(new Point(),outputScroll).Y);
        UpdateLayout();
        CaptureWindow(System.IO.Path.Combine(directory,"two-fields-guide-close-up.png"));
        var button=CompositionFieldLinks.Children.OfType<Button>().First();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));UpdateLayout();
        Require(CompositionZoom.Value==zoom && ContainsPoint(compositionView,new(.17,.11)),
            "Find Field 2 did not reach the field at unchanged zoom.");
        CaptureWindow(System.IO.Path.Combine(directory,"two-fields-find-field.png"));
        GoToGuideClick(this,new());UpdateLayout();
        Require(compositionView==view && fields.Select(f=>(f.Region,f.OutputRegion)).SequenceEqual(positions),
            "Returning to the guide changed field geometry or framing.");
        report.Add("PASS: reproduced two fields above the guide; both remain in the overview during Close-up, Find Field 2 reveals it at unchanged zoom, and Go to guide returns without moving either field or source crop.");
    }

    private void CheckEditingKeepsZoom(List<string> report)
    {
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        var field=Selected!;var region=field.OutputRegion!;var crop=field.Region;var slot=field.OutputSlot;
        var otherFields=fields.Where(f=>f!=field).Select(f=>(f.Id,f.Region,f.OutputRegion)).ToArray();
        bool wasDirty=dirty,close=CloseUpGuide.IsChecked==true;
        double savedZoom=CompositionZoom.Value;var savedCenter=compositionCenter;
        foreach(int preset in new[]{0,1,2})
        {
            field.OutputRegion=region;field.Region=crop;field.OutputSlot=slot;PopulateInspector();SyncComposition();
            if(preset==0){CloseUpGuide.IsChecked=true;CompositionViewChanged(CloseUpGuide,new());}
            else if(preset==1)ZoomSelectedClick(this,new());
            else {FitCompositionClick(this,new());CompositionZoom.Value=4;}
            UpdateLayout();
            var view=compositionView;double zoom=CompositionZoom.Value,units=CompositionUnits(1);
            void FixedView()
            {
                UpdateLayout();
                Require(compositionView==view && CompositionZoom.Value==zoom && Math.Abs(CompositionUnits(1)-units)<.000001,
                    "Editing a field changed the preview framing or displayed magnification.");
            }
            foreach(int corner in new[]{-1,0,1,2,3})
            {
                // Exercise movement/resize plus the same completion path as mouse release.
                compositionDragging=true;
                MoveOutputField(field,field.OutputRegion!,corner==-1?.15:.002,-.002,corner);
                PopulateOutputInspector();RenderCompositionBoxes();FixedView();
                var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120){RoutedEvent=Mouse.PreviewMouseWheelEvent};
                CompositionCanvas.RaiseEvent(wheel);FixedView();
                CompositionUp(this,new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left));FixedView();
                Require(field.Region==crop,"Output move/resize changed the source crop.");
            }
            OutputX.Value=Math.Max(0,OutputX.Value-10);FixedView();
            OutputW.Value=Math.Max(OutputW.Minimum,OutputW.Value-2);FixedView();
            CompositionKeyDown(this,new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,0,Key.Left)
                {RoutedEvent=Keyboard.KeyDownEvent});FixedView();
            CropX.Value=Math.Max(0,CropX.Value-.1);FixedView();
            FieldList.SelectedItem=fields.First(f=>f!=field);FixedView();
            FieldList.SelectedItem=field;FixedView();
        }
        Require(fields.Where(f=>f!=field).Select(f=>(f.Id,f.Region,f.OutputRegion)).SequenceEqual(otherFields),
            "Editing the selected field changed another field's geometry.");
        field.OutputRegion=region;field.Region=crop;field.OutputSlot=slot;dirty=wasDirty;
        CloseUpGuide.IsChecked=close;zoomUpdating=true;CompositionZoom.Value=savedZoom;zoomUpdating=false;compositionCenter=savedCenter;
        PopulateInspector();SyncComposition();PushPlan();
        report.Add("PASS: moves, all four resize corners, mouse-release refresh, keyboard nudges, crop/output sliders and field selection keep framing and displayed magnification fixed in Close-up, Zoom to selected and manual zoom; wheel ignored during a field drag.");
    }

    private void CheckGuideNavigation(List<string> report,string directory)
    {
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        string Geometry()=>System.Text.Json.JsonSerializer.Serialize(new {Fields=fields.ToArray(),Output=outputSettings});
        string before=Geometry();bool wasDirty=dirty;dirty=false;
        var pixels=engine.Preview.Combined!.Pixels.ToArray();
        double width=Width,height=Height;
        var selected=Selected;
        void CloseUp(){CloseUpGuide.IsChecked=true;CompositionViewChanged(CloseUpGuide,new());UpdateLayout();}
        void Wheel(UIElement target,int delta)
        {
            var e=new MouseWheelEventArgs(Mouse.PrimaryDevice,0,delta){RoutedEvent=Mouse.PreviewMouseWheelEvent};
            target.RaiseEvent(e);UpdateLayout();
            Require(e.Handled,"Preview wheel escaped to the page scroll viewer.");
        }
        void PanTo(Point target)
        {
            var down=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Middle){RoutedEvent=Mouse.PreviewMouseDownEvent};
            OutputPreviewBorder.RaiseEvent(down);
            Require(down.Handled && compositionPanning && CompositionCanvas.IsMouseCaptured && !compositionDragging,
                "Middle-button navigation failed to capture the preview or started a field edit.");
            PanComposition(new Point(panStart.X+(panCenter.X-target.X)*outputSettings.Width/panUnits,
                panStart.Y+(panCenter.Y-target.Y)*outputSettings.Height/panUnits));
            var up=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Middle){RoutedEvent=Mouse.PreviewMouseUpEvent};
            CompositionCanvas.RaiseEvent(up);UpdateLayout();
            Require(up.Handled && !compositionPanning && !CompositionCanvas.IsMouseCaptured,
                "Pan release left the mouse captured.");
            Require(ContainsPoint(compositionView,target),"Pan could not reach a distant field or canvas corner.");
        }
        foreach(double windowWidth in new[]{1000.0,1440.0})
        {
            Width=windowWidth;Height=windowWidth==1000?780:940;UpdateLayout();
            CloseUp();double closeZoom=CompositionZoom.Value;
            var closeView=compositionView;
            SyncComposition();UpdateLayout();
            Require(compositionView==closeView,"Inspector refresh reset guide framing.");
            Require(OverviewFields.Source is BitmapSource {PixelWidth:1920,PixelHeight:1080} &&
                CompositionFieldLinks.Children.OfType<Button>().Count()==fields.Count(f=>f.OutputRegion is not null),
                "Close-up lost the full-canvas image or a field navigation button.");
            var overviewPixels=new byte[pixels.Length];
            ((BitmapSource)OverviewFields.Source).CopyPixels(overviewPixels,outputSettings.Width*4,0);
            Require(overviewPixels.SequenceEqual(pixels),"Overview did not preserve every full-canvas field pixel.");
            foreach(var field in fields.Where(f=>f.OutputRegion is not null))
            {
                var button=CompositionFieldLinks.Children.OfType<Button>().Single(b=>ReferenceEquals(b.Tag,field));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));UpdateLayout();
                var r=field.OutputRegion!;
                Require(Selected==field && ContainsPoint(compositionView,new(r.X+r.Width/2,r.Y+r.Height/2)) &&
                    CompositionZoom.Value==closeZoom,"Find a field failed to recover a distant field at the same zoom.");
            }
            foreach(double edge in new[]{0.0,1.0})
            {
                CompositionHorizontalScroll.Value=edge==0?0:CompositionHorizontalScroll.Maximum;
                CompositionVerticalScroll.Value=edge==0?0:CompositionVerticalScroll.Maximum;UpdateLayout();
                Require(ContainsPoint(compositionView,new(edge,edge)) && CompositionZoom.Value==closeZoom,
                    "Canvas scrollbars could not reach an edge at the current zoom.");
            }
            GoToGuideClick(this,new());UpdateLayout();
            Require(compositionView==closeView && CompositionZoom.Value==closeZoom,"Go to guide changed magnification or lost guide framing.");
            foreach(var field in fields.Where(f=>f.OutputRegion is not null))
            {
                var r=field.OutputRegion!;
                PanTo(new(r.X+r.Width/2,r.Y+r.Height/2));
                Require(CompositionCanvas.Children.OfType<TextBlock>().Any(t=>t.Text==field.Name),
                    "Panning reached a field but did not render its selectable outline.");
            }
            foreach(var corner in new[]{new Point(0,0),new Point(1,0),new Point(1,1),new Point(0,1)})PanTo(corner);
            Require(CompositionZoom.Value==closeZoom,"Panning changed magnification.");
            PanTo(new(.5,.5));
            // Verify pointer anchoring away from clamped edges, in output pixels.
            var anchor=new Point(compositionView.X+compositionView.Width*.3,compositionView.Y+compositionView.Height*.6);
            ZoomCompositionAt(anchor,120);UpdateLayout();
            Require(Math.Abs(anchor.X-(compositionView.X+compositionView.Width*.3))*outputSettings.Width<=1 &&
                Math.Abs(anchor.Y-(compositionView.Y+compositionView.Height*.6))*outputSettings.Height<=1,
                "Wheel zoom drifted from its pointer anchor.");
            ZoomCompositionAt(anchor,-120);UpdateLayout();
            Require(Math.Abs(CompositionZoom.Value-closeZoom)<.000001,"Wheel in/out changed the zoom scale.");
            CloseUp();
            // Scroll out from Close-up one notch at a time, including the border margins.
            for(int i=0;i<20 && CompositionZoom.Value>1;i++)Wheel(OutputPreviewBorder,-120);
            Require(compositionView==new Crop(0,0,1,1) && OffscreenFieldsNotice.Visibility==Visibility.Collapsed,
                "Ordinary wheel steps could not reach the full canvas from Close-up.");
            Wheel(CompositionCanvas,120*32);Require(CompositionZoom.Value==32,"Wheel did not reach maximum zoom.");
            Wheel(OutputPreviewBorder,-120*32);Require(compositionView==new Crop(0,0,1,1),"Wheel did not recover from maximum zoom.");
        }
        Width=width;Height=height;UpdateLayout();CloseUp();
        var distant=fields[0].OutputRegion!;
        PanTo(new(distant.X+distant.Width/2,distant.Y+distant.Height/2));
        CompositionPreviewViewport.BringIntoView();UpdateLayout();
        CaptureWindow(System.IO.Path.Combine(directory,"panned-to-distant-field.png"));
        FieldList.SelectedItem=fields[0];ZoomSelectedClick(this,new());UpdateLayout();
        Require(ContainsPoint(compositionView,new(distant.X,distant.Y)) &&
            ContainsPoint(compositionView,new(distant.X+distant.Width,distant.Y+distant.Height)),
            "Zoom to selected did not frame the distant field.");
        CaptureWindow(System.IO.Path.Combine(directory,"distant-field-selected.png"));
        CloseUp();LiveClick(this,new());Wheel(OutputPreviewBorder,-120);PanTo(new(.5,.5));LiveClick(this,new());
        CloseUp();FieldList.SelectedItem=selected;
        Require(Geometry()==before && !dirty && engine.Preview.Combined!.Pixels.SequenceEqual(pixels),
            "Navigation changed saved fields, crops, output settings, dirty state or output pixels.");
        dirty=wasDirty;
        report.Add("PASS: full-canvas overview retains every field pixel during Close-up; every Find a field button and both scrollbars recover distant fields at unchanged zoom; Go to guide restores the guide view without changing crops or placements.");
        report.Add("PASS: guide navigation at 1000/1440 window widths, routed wheel over canvas/margins, full 1–32 zoom range, pointer anchor, middle-button capture/release, pan to every field and all four canvas edges, live-lock navigation; all field settings, crops, placements and output pixels unchanged.");
    }

    private void CheckCompositionZoomAndLayers(List<string> report,string directory)
    {
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        var field=Selected!;
        var region=field.OutputRegion;var crop=field.Region;var slot=field.OutputSlot;bool wasDirty=dirty;
        var settings=outputSettings;var pixels=engine.Preview.Combined!.Pixels.ToArray();
        var guide=scorebugGuide;bool close=CloseUpGuide.IsChecked==true,show=ShowGuide.IsChecked==true;
        double savedZoom=CompositionZoom.Value;var savedCenter=compositionCenter;
        FitCompositionClick(this,new());
        Require(compositionView==new Crop(0,0,1,1),"Fit canvas did not restore full frame.");
        var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120){RoutedEvent=Mouse.PreviewMouseWheelEvent};
        CompositionCanvas.RaiseEvent(wheel);
        Require(wheel.Handled&&Math.Abs(CompositionZoom.Value-1.25)<.001,"Ordinary scroll wheel did not zoom the preview.");
        var reverseWheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,0,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent};
        CompositionCanvas.RaiseEvent(reverseWheel);
        Require(reverseWheel.Handled&&CompositionZoom.Value==1,"Reverse scroll wheel did not zoom out.");
        FitCompositionClick(this,new());
        ZoomSelectedClick(this,new());UpdateLayout();CompositionPreviewViewport.BringIntoView();UpdateLayout();
        CaptureWindow(System.IO.Path.Combine(directory,"zoomed-editor.png"));
        field.OutputRegion=new(.49,.49,.02,.02);
        ZoomSelectedClick(this,new());UpdateLayout();
        Require(CompositionZoom.Value>20&&ContainsPoint(compositionView,new(.5,.5)),"Zoom to selected did not enlarge a tiny field.");
        var viewport=compositionView;
        SyncComposition();UpdateLayout();
        Require(compositionView==viewport,"Inspector refresh reset zoom.");
        var middle=CompositionPoint(new Point(CompositionSurface.Width/2,CompositionSurface.Height/2));
        Require(Math.Abs(middle.X-.5)<.001&&Math.Abs(middle.Y-.5)<.001,"Zoomed mouse coordinates are wrong.");
        double grabX=CompositionUnits(FieldCornerGrabRadius)/outputSettings.Width,grabY=CompositionUnits(FieldCornerGrabRadius)/outputSettings.Height;
        Require(HitResizeCorner(field.OutputRegion,new(.51+grabX*.5,.51+grabY*.5),grabX,grabY)==2,"Zoomed corner could not be grabbed.");
        MoveOutputField(field,field.OutputRegion,1.0/outputSettings.Width,0,2);
        Require(Math.Abs(field.OutputRegion.Width-.02-1.0/outputSettings.Width)<1e-9&&field.Region==crop,"Zoomed resize changed crop or used screen units as output units.");
        foreach(var center in new[]{new Point(-3,-3),new Point(4,4)})
        {
            compositionCenter=center;UpdateCompositionView();
            Require(compositionView.X>=0&&compositionView.Y>=0&&compositionView.X+compositionView.Width<=1.000001&&compositionView.Y+compositionView.Height<=1.000001,"Panned view escaped canvas.");
        }
        field.OutputRegion=region;field.OutputSlot=slot;FitCompositionClick(this,new());UpdateLayout();
        Require(settings==outputSettings&&wasDirty==dirty&&engine.Preview.Combined!.Pixels.SequenceEqual(pixels),"Preview navigation changed setup or output pixels.");
        report.Add("PASS: ordinary scroll-wheel zoom in/out, 32× zoom, selected-field framing, coordinate mapping, zoomed corner hit/resize, pan bounds, fit canvas and preserved view across inspector refresh; output pixels unchanged.");

        // Render the actual WPF layers with an opaque scorebug underneath a live field.
        // Transparent field pixels must reveal the guide, while opaque digits must cover it.
        int w=outputSettings.Width,h=outputSettings.Height;
        var artwork=new byte[w*h*4];var digits=new byte[artwork.Length];
        for(int i=0;i<artwork.Length;i+=4){artwork[i+2]=255;artwork[i+3]=255;}
        for(int y=h/3;y<h*2/3;y++)for(int x=w/3;x<w*2/3;x++)
        {int i=(y*w+x)*4;digits[i+1]=255;digits[i+3]=255;}
        double opacity=GuideOpacity.Value;GuideOpacity.Value=1;
        UpdateImage(GuideImage,new(w,h,artwork));UpdateImage(CombinedImage,new(w,h,digits));
        CompositionCanvas.Visibility=Visibility.Hidden;UpdateLayout();
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush(CompositionSurface){Stretch=Stretch.Fill},null,new Rect(0,0,300,180));
        var rendered=new RenderTargetBitmap(300,180,96,96,PixelFormats.Pbgra32);rendered.Render(visual);
        var result=new byte[300*180*4];rendered.CopyPixels(result,300*4,0);
        int front=(90*300+150)*4,back=(20*300+20)*4;
        Require(result[front+1]>240&&result[front+2]<10,"Scorebug obscured live digits in the WPF preview.");
        Require(result[back+2]>240&&result[back+1]<10,"Transparent digits hid the guide.");
        Require(Panel.GetZIndex(CombinedImage)>Panel.GetZIndex(GuideImage)&&Panel.GetZIndex(CompositionCanvas)>Panel.GetZIndex(CombinedImage),"Guide/fields/handles layer order is wrong.");
        GuideOpacity.Value=.35;UpdateLayout();
        Require(Math.Abs(GuideImage.Opacity-.35)<.001&&CombinedImage.Opacity==1&&engine.Preview.Combined!.Pixels.SequenceEqual(pixels)&&dirty==wasDirty,"Dimming guide changed live fields or output pixels.");
        GuideOpacity.Value=opacity;
        CompositionCanvas.Visibility=Visibility.Visible;scorebugGuide=guide;ShowGuide.IsChecked=show;CloseUpGuide.IsChecked=close;
        zoomUpdating=true;CompositionZoom.Value=savedZoom;compositionCenter=savedCenter;zoomUpdating=false;UpdateCompositionView();
        report.Add("PASS: rendered opaque guide sits behind live digit pixels; transparent areas reveal artwork and editor handles stay in front.");
    }
}
