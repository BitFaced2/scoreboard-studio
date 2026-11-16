using System.Windows;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private bool automaticDetectionPending;
    private int detectionRevision;

    private void CancelAutomaticDetection()
    {
        automaticDetectionPending=false;
        detectionRevision++;
    }

    private async Task DetectLoadedSource(int sourceGeneration,int revision)
    {
        bool StillWanted() => revision==detectionRevision && sourceGeneration==generation
            && fields.Count==0 && !locked && !drawing && !EditingPerspective;
        try
        {
            DetectionHint.Text="Finding scoreboard fields…";
            Notice.Text="Finding fields automatically. You can adjust the boxes afterward.";
            // MediaOpened can precede the first decoded picture. Wait for video
            // to settle and retry blank opening frames rather than accepting them.
            if(kind=="Video")await Task.Delay(600);
            for(int attempt=0;attempt<3;attempt++)
            {
                if(!StillWanted())return;
                if(engine.Preview is not { Healthy:true,Source:{} frame })return;
                var detected=await Task.Run(()=>FieldDetector.DetectAutomatic(frame));
                if(!StillWanted())return;
                if(detected.Regions.Count>0)
                {
                    beforeDetection=[];
                    DetectionIsolation.SelectedIndex=detected.Isolation=="White highlights"?1:0;
                    DetectionThreshold.Value=detected.Threshold;
                    foreach(var region in detected.Regions)fields.Add(new() {
                        Name=$"Field {fields.Count+1}",Region=region,
                        Cleanup=new() {Isolation=detected.Isolation,Threshold=detected.Threshold,Softness=40,Smooth=true}
                    });
                    suggestions.Clear();AcceptDetection.Visibility=Visibility.Collapsed;UndoDetection.Visibility=Visibility.Visible;
                    SyncFields();PopulateInspector();Changed();
                    DetectionHint.Text=$"{fields.Count} fields created automatically. Adjust and name them as needed.";
                    Notice.Text="Fields are ready. Select one to adjust its box or open Cleanup.";
                    return;
                }
                if(kind!="Video")break;
                await Task.Delay(500);
            }
            if(StillWanted())
            {
                DetectionHint.Text="No fields found. Adjust Look for / Cutoff and try Auto-detect fields, or draw a box.";
                Notice.Text=DetectionHint.Text;
            }
        }
        catch(Exception ex)
        {
            if(StillWanted()) {DetectionHint.Text="Automatic detection failed: "+ex.Message;Notice.Text=DetectionHint.Text;}
        }
    }
}
