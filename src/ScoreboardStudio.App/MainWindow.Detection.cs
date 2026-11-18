using System.Windows;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private int detectionRevision;

    private void CancelDetection()
    {
        detectionRevision++;
        suggestions.Clear();
        AcceptDetection.Visibility=Visibility.Collapsed;
    }
}
