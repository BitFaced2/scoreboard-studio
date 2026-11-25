using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using ScoreboardStudio.Core;
using Path=System.IO.Path;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private OutputSettings outputSettings=new() {Mode="Combined"};
    private ScorebugGuide? scorebugGuide;
    private string? guidePath,guideWarning;
    private bool compositionUpdating,compositionDragging;
    private int compositionResizeCorner=-1;
    private Point compositionStart;
    private Crop? compositionOriginal;
    private Crop compositionView=new(0,0,1,1);
    private VideoFrame? lastCompositePreview;

    private void PlaceNewOutputFields()
    {
        if(outputSettings.Mode=="Combined"&&!locked)
            FieldComposition.PlaceNewFields(fields.ToArray(),scorebugGuide?.Bounds);
    }

    private void SyncComposition()
    {
        if(!ready)return;
        compositionUpdating=true;
        OutputMode.SelectedIndex=outputSettings.Mode=="Combined"?0:1;
        bool combined=outputSettings.Mode=="Combined";
        CombinedPanel.Visibility=CanvasControls.Visibility=combined?Visibility.Visible:Visibility.Collapsed;
        OutputCards.Visibility=combined?Visibility.Collapsed:Visibility.Visible;
        CanvasWidthBox.Text=outputSettings.Width.ToString();CanvasHeightBox.Text=outputSettings.Height.ToString();
        OutputMode.IsEnabled=OutputLayoutControls.IsEnabled=PlacementActions.IsEnabled=!locked;
        GuideInfo.Text=guideWarning is not null?"Guide unavailable":scorebugGuide is null?"No guide loaded":Path.GetFileName(guidePath);
        GuideInfo.ToolTip=guideWarning ?? (scorebugGuide is null?"Load a GTZIP or image guide. Preview only.":scorebugGuide.Slots.Count+" targets. "+scorebugGuide.Notes);
        CombinedOutputName.Text=VideoEngine.OutputName(kind,"Combined")+$"  •  {outputSettings.Width} × {outputSettings.Height}";
        OutputInstructions.Text=combined?"In vMix, add the combined NDI feed and layer it over your GT title at the same full-canvas size and position. Clear the GT title's live-value text (scores, clock, period, fouls, timeouts) so it does not show behind the video digits. Guide artwork is never transmitted.":"Legacy mode: add each enabled field as a separate NDI input in vMix and position it over the GT title.";
        compositionUpdating=false;
        PopulateOutputInspector();UpdateCompositionView();
    }
    private void PopulateOutputInspector()
    {
        if(!ready)return;
        compositionUpdating=true;
        OutputPlacementInspector.Visibility=outputSettings.Mode=="Combined"&&WorkspaceTabs.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        GuideTarget.Items.Clear();GuideTarget.Items.Add("Custom position");
        foreach(var slot in scorebugGuide?.Slots??[])GuideTarget.Items.Add(slot);
        GuideTarget.SelectedIndex=0;
        if(Selected is {} field)
        {
            GuideTarget.SelectedItem=GuideTarget.Items.Cast<object>().OfType<GuideSlot>().FirstOrDefault(s=>s.Name==field.OutputSlot)??(object)GuideTarget.Items[0];
            OutputFit.SelectedIndex=field.OutputFit=="Stretch"?1:0;
        }
        else {OutputFit.SelectedIndex=1;}
        PopulateOutputGeometry();
        compositionUpdating=false;
        RefreshColorSwatches();
    }
    private void OutputModeChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ready||compositionUpdating||locked)return;
        output=false;OutputButton.Content="Start NDI outputs";
        outputSettings=outputSettings with {Mode=OutputMode.SelectedIndex==0?"Combined":"Individual"};
        PlaceNewOutputFields();
        Changed();SyncComposition();Notice.Text="Output mode changed. Review placement, then start outputs.";
    }
    private void LoadGuideClick(object sender,RoutedEventArgs e)
    {
        if(locked)return;
        var dialog=new OpenFileDialog {Filter="Scorebug guides|*.gtzip;*.png;*.jpg;*.jpeg;*.bmp",Title="Load scorebug positioning guide"};
        if(dialog.ShowDialog(this)!=true)return;
        try {LoadGuide(dialog.FileName);}
        catch(Exception ex){Notice.Text="Could not load guide: "+ex.Message;}
    }
    private void LoadGuide(string path)
    {
        var guide=ScorebugGuide.Load(path);
        scorebugGuide=guide;guidePath=Path.GetFullPath(path);guideWarning=null;
        output=false;OutputButton.Content="Start NDI outputs";
        outputSettings=outputSettings with {Mode="Combined",Width=guide.Image.Width,Height=guide.Image.Height};
        // Keep existing fields in view. Guide close-up is an explicit editing action.
        CloseUpGuide.IsChecked=false;ResetCompositionZoom();
        PlaceNewOutputFields();
        Changed();SyncComposition();
        Notice.Text="Guide loaded. Select a field and choose its target, or use Match field names to guide. Existing crops and placements are retained. The guide is preview-only.";
    }
    private void ClearGuideClick(object sender,RoutedEventArgs e)
    {
        if(locked)return;
        scorebugGuide=null;guidePath=null;guideWarning=null;CloseUpGuide.IsChecked=false;ResetCompositionZoom();Changed();SyncComposition();
    }
    private void ApplyCanvasSizeClick(object sender,RoutedEventArgs e)
    {
        if(locked)return;
        if(!int.TryParse(CanvasWidthBox.Text,out int w)||!int.TryParse(CanvasHeightBox.Text,out int h)||!new OutputSettings {Width=w,Height=h}.IsValid())
        {Notice.Text="Use an even canvas width, from 320 × 180 to 3840 × 2160.";return;}
        output=false;OutputButton.Content="Start NDI outputs";outputSettings=outputSettings with {Width=w,Height=h};Changed();SyncComposition();
        Notice.Text="Canvas size changed. Review placement and use matching full-canvas geometry in vMix before starting outputs.";
    }
    private void CompositionViewChanged(object sender,RoutedEventArgs e)
    {
        if(!ready)return;
        if(ReferenceEquals(sender,CloseUpGuide))
        {
            if(CloseUpGuide.IsChecked==true && scorebugGuide is not null)FrameComposition(scorebugGuide.Bounds,1.08);
            else ResetCompositionZoom();
        }
        UpdateCompositionView();
    }
    private void UpdateCompositionView()
    {
        if(!ready)return;
        compositionView=ZoomedCompositionView();
        // Match the integer crop used by the preview to its mouse coordinate system.
        int w=Math.Clamp((int)Math.Round(compositionView.Width*outputSettings.Width),1,outputSettings.Width);
        int h=Math.Clamp((int)Math.Round(compositionView.Height*outputSettings.Height),1,outputSettings.Height);
        int x=Math.Clamp((int)Math.Round(compositionView.X*outputSettings.Width),0,outputSettings.Width-w);
        int y=Math.Clamp((int)Math.Round(compositionView.Y*outputSettings.Height),0,outputSettings.Height-h);
        compositionView=new(x/(double)outputSettings.Width,y/(double)outputSettings.Height,w/(double)outputSettings.Width,h/(double)outputSettings.Height);
        CompositionSurface.Width=w;CompositionSurface.Height=h;
        CompositionZoomLabel.Text=$"{CompositionZoom.Value:0.##}×";
        UpdateImage(GuideImage,scorebugGuide is not null&&ShowGuide.IsChecked==true?FrameProcessor.Crop(scorebugGuide.Image,compositionView):null);
        UpdateImage(OverviewGuide,scorebugGuide is not null&&ShowGuide.IsChecked==true?scorebugGuide.Image:null);
        UpdateCompositionNavigation();
        lastCompositePreview=null;RefreshCompositionPreview();RenderCompositionBoxes();
    }
    private void RefreshCompositionPreview()
    {
        if(!ready||WorkspaceTabs.SelectedIndex!=1||outputSettings.Mode!="Combined")return;
        var frame=engine.Preview.Combined;
        if(!ReferenceEquals(frame,lastCompositePreview))
        {lastCompositePreview=frame;UpdateImage(CombinedImage,frame is null?null:compositionView==new Crop(0,0,1,1)?frame:FrameProcessor.Crop(frame,compositionView));UpdateImage(OverviewFields,frame);}
    }
    private void SetSlot(ScoreField field,GuideSlot slot)
    {field.OutputRegion=slot.Region;field.OutputPlacementInitialized=true;field.OutputSlot=slot.Name;field.OutputColor=null;}
    private void GuideTargetChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ready||compositionUpdating||locked||Selected is not {} field||GuideTarget.SelectedItem is not GuideSlot slot)return;
        SetSlot(field,slot);Changed();SyncComposition();Notice.Text=$"Placed {field.Name} in {slot.Name}. Source crop unchanged.";
    }
    private void MatchGuideClick(object sender,RoutedEventArgs e)
    {
        if(locked||scorebugGuide is null)return;
        static string Key(string s)=>new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var aliases=new Dictionary<string,string> { ["gameclock"]="period1",["clock"]="period1",["hometimeouts"]="hometo",["awaytimeouts"]="awayto",["quarter"]="period" };
        int count=0;
        foreach(var field in fields)
        {
            string key=Key(field.Name);if(aliases.TryGetValue(key,out var alias))key=alias;
            var slot=scorebugGuide.Slots.FirstOrDefault(s=>Key(s.Name)==key);
            if(slot is null)continue;SetSlot(field,slot);count++;
        }
        Changed();SyncComposition();Notice.Text=$"Placed {count} matching fields. Other placements are unchanged. Check every target before starting outputs.";
    }
    private void PlaceOutputFieldClick(object sender,RoutedEventArgs e)
    {
        if(locked||Selected is not {} field){Notice.Text="Select a field on the left first.";return;}
        if(field.OutputRegion is null)
        {
            field.OutputPlacementInitialized=false;
            FieldComposition.PlaceNewFields(fields.ToArray(),compositionView);
        }
        Changed();SyncComposition();
    }
    private void OutputAppearanceChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ready||compositionUpdating||locked||Selected is not {} field)return;
        field.OutputFit=OutputFit.SelectedIndex==1?"Stretch":"Contain";
        RefreshColorSwatches();Changed();
    }
    private void UnplaceOutputClick(object sender,RoutedEventArgs e)
    {if(!locked&&Selected is {} field){field.OutputRegion=null;field.OutputPlacementInitialized=true;field.OutputSlot=null;Changed();SyncComposition();}}
    private void OutputFrontClick(object sender,RoutedEventArgs e)
    {if(!locked&&Selected is {} field){fields.Move(fields.IndexOf(field),fields.Count-1);SyncFields();Changed();}}
    private Point CompositionPoint(MouseEventArgs e)
        => CompositionPoint(e.GetPosition(CompositionCanvas));
    private Point CompositionPoint(Point point)
        => new(Math.Clamp(compositionView.X+point.X/outputSettings.Width,0,1),Math.Clamp(compositionView.Y+point.Y/outputSettings.Height,0,1));
    private double CompositionUnits(double screenPixels)
    {
        double scale=Math.Min(CompositionViewbox.ActualWidth/CompositionSurface.Width,CompositionViewbox.ActualHeight/CompositionSurface.Height);
        return screenPixels/(scale>.01?scale:1);
    }
    private void CompositionDown(object sender,MouseButtonEventArgs e)
    {
        if(Keyboard.IsKeyDown(Key.Space))return;
        if(locked)return;var point=CompositionPoint(e);
        double hitX=CompositionUnits(FieldCornerGrabRadius)/outputSettings.Width,hitY=CompositionUnits(FieldCornerGrabRadius)/outputSettings.Height;
        var field=Selected is {OutputRegion:{} active} selected && HitResizeCorner(active,point,hitX,hitY)>=0 ? selected :
            fields.Reverse().FirstOrDefault(f=>f.OutputRegion is {} r && (HitResizeCorner(r,point,hitX,hitY)>=0 || ContainsPoint(r,point)));
        if(field?.OutputRegion is not {} region)return;
        FieldList.SelectedItem=field;compositionStart=point;compositionOriginal=region;
        compositionResizeCorner=HitResizeCorner(region,point,hitX,hitY);
        CompositionCanvas.Cursor=FieldDragCursor(compositionResizeCorner);
        compositionDragging=true;CompositionCanvas.Focus();CompositionCanvas.CaptureMouse();e.Handled=true;
    }
    private void CompositionMove(object sender,MouseEventArgs e)
    {
        if(compositionPanning){PanComposition(e);return;}
        if(locked)return;
        var p=CompositionPoint(e);
        if(!compositionDragging)
        {
            double hitX=CompositionUnits(FieldCornerGrabRadius)/outputSettings.Width,hitY=CompositionUnits(FieldCornerGrabRadius)/outputSettings.Height;
            var hovered=Selected is {OutputRegion:{} active} selected && HitResizeCorner(active,p,hitX,hitY)>=0 ? selected :
                fields.Reverse().FirstOrDefault(f=>f.OutputRegion is {} r && (HitResizeCorner(r,p,hitX,hitY)>=0 || ContainsPoint(r,p)));
            CompositionCanvas.Cursor=hovered?.OutputRegion is not {} region?Cursors.Arrow:FieldDragCursor(HitResizeCorner(region,p,hitX,hitY));
            return;
        }
        if(Selected is not {} field||compositionOriginal is not {} original)return;
        MoveOutputField(field,original,p.X-compositionStart.X,p.Y-compositionStart.Y,compositionResizeCorner);
        PopulateOutputInspector();RenderCompositionBoxes();PushPlan();
    }
    private void MoveOutputField(ScoreField field,Crop original,double dx,double dy,int corner)
    {
        field.OutputSlot=null;
        field.OutputRegion=ResizeOrMoveBox(original,dx,dy,corner,1.0/outputSettings.Width,1.0/outputSettings.Height);
    }
    private void CompositionUp(object sender,MouseButtonEventArgs e)
    {if(!compositionDragging)return;compositionDragging=false;CompositionCanvas.Cursor=Cursors.Arrow;CompositionCanvas.ReleaseMouseCapture();Changed();SyncComposition();}
    private void CompositionLostCapture(object sender,MouseEventArgs e)
    {compositionPanning=false;CompositionCanvas.Cursor=Cursors.Arrow;if(compositionDragging){compositionDragging=false;Changed();SyncComposition();}}
    private void CompositionKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Space){e.Handled=true;return;}
        if(locked||Selected is not {} field||field.OutputRegion is not {} r)return;
        int step=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?10:1;
        double dx=e.Key==Key.Left?-step:e.Key==Key.Right?step:0,dy=e.Key==Key.Up?-step:e.Key==Key.Down?step:0;
        if(dx==0&&dy==0)return;
        MoveOutputField(field,r,dx/outputSettings.Width,dy/outputSettings.Height,-1);Changed();SyncComposition();e.Handled=true;
    }
    private void RenderCompositionBoxes()
    {
        if(!ready)return;UpdateOffscreenFields();RenderCompositionOverview();CompositionCanvas.Children.Clear();if(locked)return;
        double units=CompositionUnits(1);
        void Box(Crop r,string name,string color,bool selected,bool target)
        {
            double x=(r.X-compositionView.X)*outputSettings.Width,y=(r.Y-compositionView.Y)*outputSettings.Height,w=r.Width*outputSettings.Width,h=r.Height*outputSettings.Height;
            if(x+w<0||y+h<0||x>CompositionSurface.Width||y>CompositionSurface.Height)return;
            var outline=new Rectangle {Width=w,Height=h,Stroke=Brush(color),StrokeThickness=(selected?2:1)*units,IsHitTestVisible=false};
            if(target)outline.StrokeDashArray=[4,3];Canvas.SetLeft(outline,x);Canvas.SetTop(outline,y);CompositionCanvas.Children.Add(outline);
            var label=new TextBlock {Text=name,Foreground=Brush(color),Background=Brush("#C010151E"),FontSize=11*units,IsHitTestVisible=false};
            Canvas.SetLeft(label,Math.Max(0,x));Canvas.SetTop(label,Math.Max(0,y-15*units));CompositionCanvas.Children.Add(label);
            if(selected)DrawResizeGrips(CompositionCanvas,x,y,w,h,units,color);
        }
        if(ShowGuide.IsChecked==true)foreach(var slot in scorebugGuide?.Slots??[])Box(slot.Region,slot.Name,"#B39E70",false,true);
        foreach(var field in fields)if(field.OutputRegion is {} r)Box(r,field.Name+(field.Enabled?"":" (hidden)"),field==Selected?"#62F0C5":"#8BC7FF",field==Selected,false);
    }
    private OutputSettings SavedOutput(string? setupPath)
    {
        string? path=guidePath;
        if(path is not null && setupPath is not null)path=Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(setupPath))!,path);
        return outputSettings with {GuidePath=path};
    }
    private void RestoreOutput(Layout layout,string path)
    {
        outputSettings=layout.Output with {};scorebugGuide=null;guidePath=null;guideWarning=null;
        if(!string.IsNullOrWhiteSpace(outputSettings.GuidePath))
        {
            try
            {
                guidePath=Path.GetFullPath(outputSettings.GuidePath,Path.GetDirectoryName(Path.GetFullPath(path))!);
                scorebugGuide=ScorebugGuide.Load(guidePath);
            }
            catch(Exception ex){guideWarning="Guide unavailable: "+ex.Message+" Saved field positions are retained. Load the guide again if needed.";}
        }
        CloseUpGuide.IsChecked=false;ResetCompositionZoom();
        SyncComposition();
    }
}
