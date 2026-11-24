using System.Windows;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private int detectionRevision;
    // A fresh session gets one initial pass. Editing, loading a layout, or
    // completing that pass permanently hands placement back to the operator.
    private bool initialPlacementFinished;

    private void CancelDetection(bool cancelInitial = true)
    {
        if (perspectiveDetectionPending)
        {
            perspectiveDetectionPending = false;
            DetectionHint.Text = "Field rescan canceled by another edit. Review the boxes or use Find fields.";
            Notice.Text = DetectionHint.Text;
        }
        if(cancelInitial)initialPlacementFinished=true;
        detectionRevision++;
        suggestions.Clear();
        AcceptDetection.Visibility=Visibility.Collapsed;
    }

    private async Task PlaceInitialFields(int sourceGeneration,int revision)
    {
        bool Current() => generation==sourceGeneration && detectionRevision==revision && fields.Count==0
            && !locked && !drawing && !EditingPerspective;
        try
        {
            DetectionHint.Text="Finding the initial fields…";
            if(kind=="Video")await Task.Delay(600);
            for(int attempt=0;attempt<3;attempt++)
            {
                if(!Current())return;
                var preview=engine.Preview;
                if(preview.Generation!=sourceGeneration || !preview.Healthy || preview.Source is not {} frame)return;
                var detection=await Task.Run(()=>FieldDetector.DetectAutomatic(frame));
                if(!Current())return;
                if(detection.Regions.Count>0)
                {
                    beforeDetection=[]; beforeDetectionPerspective=null; UndoDetection.Content="Undo detection";
                    DetectionIsolation.SelectedIndex=detection.Isolation=="White highlights"?1:0;
                    DetectionThreshold.Value=detection.Threshold;
                    foreach(var region in detection.Regions)fields.Add(new() {
                        Name=$"Field {fields.Count+1}",Region=region,
                        Cleanup=new() {Isolation=detection.Isolation,Threshold=detection.Threshold,Softness=40,Smooth=true}
                    });
                    UndoDetection.Visibility=Visibility.Visible;
                    SyncFields();PopulateInspector();Changed();
                    DetectionHint.Text="Initial fields created. Boxes now stay fixed until you edit them.";
                    Notice.Text="Review and name the fields. Use + Draw a field to add one, or Remove selected field to delete one.";
                    return;
                }
                if(kind!="Video")break;
                await Task.Delay(500);
            }
            if(Current())DetectionHint.Text="No initial fields found. Draw fields or click Find fields to try again.";
        }
        catch(Exception ex)
        {
            Diagnostics.Log("Initial field detection",ex);
            if(Current())DetectionHint.Text="Could not find the initial fields. Draw fields or click Find fields to retry.";
        }
    }
}
