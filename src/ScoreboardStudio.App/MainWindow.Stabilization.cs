using System.IO;
using System.Windows;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private StabilizationSettings stabilization=new();
    private void ResetStabilization()
    {
        stabilization=new();
        StabilizeButton.Content="Stabilize camera";
        ReferenceButton.IsEnabled=false;
    }
    private void StabilizeClick(object sender,RoutedEventArgs e)
    {
        if(locked || EditingPerspective)return;
        if(stabilization.Enabled) { ResetStabilization(); output=false;OutputButton.Content="Start NDI outputs";PushPlan();Notice.Text="Stabilization off. Review field alignment before starting outputs.";return; }
        SetReferenceClick(sender,e);
    }
    private void SetReferenceClick(object sender,RoutedEventArgs e)
    {
        if(perspectiveDetectionPending) { Notice.Text="Wait for the corrected-view fields to finish updating before setting stabilization.";return; }
        if(locked || EditingPerspective || engine.Preview.RawSource is null)return;
        output=false; OutputButton.Content="Start NDI outputs";
        Crop area=new(0,0,1,1);
        if(perspective.Enabled)
        {
            var p=perspective.Corners();double x=p.Min(c=>c.X),y=p.Min(c=>c.Y);
            area=new(x,y,p.Max(c=>c.X)-x,p.Max(c=>c.Y)-y);
        }
        else if(fields.Count>0)
        {
            double x=fields.Min(f=>f.Region.X),y=fields.Min(f=>f.Region.Y);
            area=new(x,y,fields.Max(f=>f.Region.X+f.Region.Width)-x,fields.Max(f=>f.Region.Y+f.Region.Height)-y);
        }
        double margin=.04;
        double left=Math.Max(0,area.X-margin),top=Math.Max(0,area.Y-margin);
        area=new(left,top,Math.Min(1,area.X+area.Width+margin)-left,Math.Min(1,area.Y+area.Height+margin)-top);
        stabilization=new(true,stabilization.Reference+1,area);
        StabilizeButton.Content="Stop stabilization";ReferenceButton.IsEnabled=true;
        CancelDetection();PushPlan();
        Notice.Text="Reference set to the current camera view. Review corners and fields, then start outputs. Small shifts, rotation and zoom are corrected; large moves need realignment. Reference is for this session only.";
    }
    private void MinimizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void MaximizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    internal string? SaveRecovery()
    {
        try
        {
            Directory.CreateDirectory(Diagnostics.DirectoryPath);
            string path=Path.Combine(Diagnostics.DirectoryPath,"Recovered-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".scoreboard.json");
            new Layout { Name=setupName,ImagePath=stillPath,VideoPath=videoPath,SourceName=source?.Name,
                Perspective=perspective with {},Output=SavedOutput(null),Fields=fields.Select(f=>f.Snapshot()).ToList() }.Save(path);
            return path;
        }
        catch(Exception ex) {Diagnostics.Log("Save recovery",ex);return null;}
    }
}
