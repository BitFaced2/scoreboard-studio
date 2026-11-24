using System.Windows;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private bool perspectiveDetectionPending;
    private Task perspectiveDetectionTask = Task.CompletedTask;
    private PerspectiveSettings? beforeDetectionPerspective;

    private async Task ReevaluatePerspectiveFields(VideoFrame rawFrame, PerspectiveSettings previousPerspective)
    {
        var correction = perspective with { };
        var previousFields = fields.Select(f => f.Snapshot()).ToArray();
        int sourceGeneration = generation, revision = detectionRevision;
        bool Current() => sourceGeneration == generation && revision == detectionRevision && !locked && !EditingPerspective;
        beforeDetection = previousFields;
        beforeDetectionPerspective = previousPerspective;
        UndoDetection.Content = "Undo correction";
        UndoDetection.Visibility = Visibility.Visible;
        perspectiveDetectionPending = true;
        DetectionHint.Text = "Finding fields in the corrected view...";
        Notice.Text = "Updating field positions and count for this view. Outputs remain stopped.";
        try
        {
            // Use the exact frozen camera frame used for the corner edit. The engine
            // preview may still contain the old perspective when Apply is clicked.
            var detection = await Task.Run(() => DetectCorrectedFields(rawFrame, correction));
            if (!Current()) return;
            var replacements = MatchCorrectedFields(previousFields, previousPerspective, correction, detection);
            perspectiveDetectionPending = false;
            fields.Clear();
            foreach (var field in replacements) fields.Add(field);
            DetectionIsolation.SelectedIndex = detection.Isolation == "White highlights" ? 1 : 0;
            DetectionThreshold.Value = detection.Threshold;
            SyncFields(); PopulateInspector(); Changed();
            DetectionHint.Text = fields.Count > 0
                ? $"Found {fields.Count} fields in the corrected view. Boxes now stay fixed until edited."
                : "No fields found in the corrected view. Draw fields manually or undo the correction.";
            Notice.Text = "View and fields updated. Review the boxes before starting outputs. Field detection > Undo correction restores the previous view and fields.";
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Field detection after perspective correction", ex);
            if (!Current()) return;
            perspectiveDetectionPending = false;
            // A failed detector must not leave the old boxes on a new coordinate system.
            UndoDetectionClick(this, new());
            Notice.Text = "Could not update fields for the correction. The previous view and fields were restored. Try again or use Find fields.";
        }
        finally
        {
            if (revision == detectionRevision) perspectiveDetectionPending = false;
        }
    }

    private static FieldDetector.Detection DetectCorrectedFields(VideoFrame frame, PerspectiveSettings correction)
    {
        if (!correction.Enabled) return FieldDetector.DetectAutomatic(frame);
        double Distance(NormalPoint a, NormalPoint b) => Math.Sqrt(
            Math.Pow((a.X-b.X)*frame.Width,2)+Math.Pow((a.Y-b.Y)*frame.Height,2));
        var corners = correction.Corners();
        // Detect at the board's captured resolution and proportions. Stretching
        // a small board to a wide output otherwise separates individual LEDs.
        // Normalized boxes transfer directly to any chosen corrected size.
        var detectionView = correction with {
            Width = Math.Clamp((int)Math.Round((Distance(corners[0],corners[1])+Distance(corners[3],corners[2]))/2),32,4096),
            Height = Math.Clamp((int)Math.Round((Distance(corners[0],corners[3])+Distance(corners[1],corners[2]))/2),32,2160)
        };
        var board = PerspectiveTransform.Apply(frame,detectionView);
        // Retain the camera-sized search scale used by initial detection, with
        // only the chosen board visible. This keeps LED grouping consistent
        // when the operator crops away duplicate boards.
        int width=Math.Max(frame.Width,board.Width), height=Math.Max(frame.Height,board.Height);
        var pixels=new byte[width*height*4];
        for(int y=0;y<board.Height;y++)
            Buffer.BlockCopy(board.Pixels,y*board.Width*4,pixels,y*width*4,board.Width*4);
        var detection=FieldDetector.DetectAutomatic(new(width,height,pixels));
        var regions=detection.Regions.Select(r=>new Crop(
            r.X*width/board.Width,r.Y*height/board.Height,
            r.Width*width/board.Width,r.Height*height/board.Height).Clamp()).ToList();
        // Reject tiny colored marks/logos relative to the main display areas.
        double largestArea=regions.Select(r=>r.Width*r.Height).DefaultIfEmpty(0).Max();
        return detection with { Regions=regions.Where(r=>r.Width*r.Height>=largestArea*.03).ToList() };
    }

    private static List<ScoreField> MatchCorrectedFields(ScoreField[] previous, PerspectiveSettings oldView,
        PerspectiveSettings newView, FieldDetector.Detection detection)
    {
        var result = detection.Regions.Select((region, i) => new ScoreField {
            Name = $"Field {i + 1}", Region = region,
            Cleanup = new() {Isolation = detection.Isolation, Threshold = detection.Threshold, Softness = 40, Smooth = true}
        }).ToList();
        // Compare in camera coordinates so retaining only one of two duplicate boards
        // preserves the identity/output destination of the board actually selected.
        var oldBounds = previous.Select(f => CameraBounds(f.Region, oldView)).ToArray();
        var newBounds = result.Select(f => CameraBounds(f.Region, newView)).ToArray();
        var candidates = new List<(int Old, int New, double Overlap)>();
        for (int i = 0; i < oldBounds.Length; i++)
        for (int j = 0; j < newBounds.Length; j++)
        {
            var a = oldBounds[i]; var b = newBounds[j];
            double intersection = Math.Max(0, Math.Min(a.X+a.Width,b.X+b.Width)-Math.Max(a.X,b.X))
                * Math.Max(0,Math.Min(a.Y+a.Height,b.Y+b.Height)-Math.Max(a.Y,b.Y));
            double overlap = intersection / Math.Max(1e-12, a.Width*a.Height+b.Width*b.Height-intersection);
            if (overlap >= .25) candidates.Add((i,j,overlap));
        }
        var usedOld = new HashSet<int>(); var usedNew = new HashSet<int>();
        foreach (var match in candidates.OrderByDescending(c => c.Overlap))
        {
            if (usedOld.Contains(match.Old) || usedNew.Contains(match.New)) continue;
            usedOld.Add(match.Old); usedNew.Add(match.New);
            var retained = previous[match.Old].Snapshot();
            retained.Region = result[match.New].Region;
            result[match.New] = retained;
        }
        var names = usedNew.Select(i => result[i].Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int number = 1;
        for (int i = 0; i < result.Count; i++)
        {
            if (usedNew.Contains(i)) continue;
            while (!names.Add($"Field {number}")) number++;
            result[i].Name = $"Field {number++}";
        }
        return result;
    }

    private static Crop CameraBounds(Crop region, PerspectiveSettings view)
    {
        if (!view.Enabled) return region;
        var p = view.Corners();
        double dx1=p[1].X-p[2].X, dx2=p[3].X-p[2].X, dx3=p[0].X-p[1].X+p[2].X-p[3].X;
        double dy1=p[1].Y-p[2].Y, dy2=p[3].Y-p[2].Y, dy3=p[0].Y-p[1].Y+p[2].Y-p[3].Y;
        double g=0,h=0;
        if (Math.Abs(dx3)+Math.Abs(dy3)>1e-12)
        {
            double determinant=dx1*dy2-dx2*dy1;
            g=(dx3*dy2-dx2*dy3)/determinant; h=(dx1*dy3-dx3*dy1)/determinant;
        }
        NormalPoint Map(double u,double v)
        {
            double denominator=g*u+h*v+1;
            return new(((p[1].X-p[0].X+g*p[1].X)*u+(p[3].X-p[0].X+h*p[3].X)*v+p[0].X)/denominator,
                ((p[1].Y-p[0].Y+g*p[1].Y)*u+(p[3].Y-p[0].Y+h*p[3].Y)*v+p[0].Y)/denominator);
        }
        var corners = new[]{Map(region.X,region.Y),Map(region.X+region.Width,region.Y),
            Map(region.X+region.Width,region.Y+region.Height),Map(region.X,region.Y+region.Height)};
        double x=corners.Min(c=>c.X),y=corners.Min(c=>c.Y);
        return new(x,y,corners.Max(c=>c.X)-x,corners.Max(c=>c.Y)-y);
    }
}
