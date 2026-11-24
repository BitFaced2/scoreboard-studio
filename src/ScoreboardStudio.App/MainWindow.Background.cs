using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    // Viewing preference only: never included in the engine plan or NDI frames.
    public static readonly DependencyProperty PreviewBackdropProperty = DependencyProperty.Register(
        nameof(PreviewBackdrop), typeof(System.Windows.Media.Brush), typeof(MainWindow));
    public System.Windows.Media.Brush PreviewBackdrop
    {
        get => (System.Windows.Media.Brush)GetValue(PreviewBackdropProperty);
        set => SetValue(PreviewBackdropProperty,value);
    }
    private string customPreviewBackground="#182332";

    private void BackgroundChanged(object sender,SelectionChangedEventArgs e)
    { if(ready)UpdatePreviewBackground(); }

    private void UpdatePreviewBackground()
    {
        PreviewBackdrop=PreviewBackground.SelectedIndex switch
        {
            1=>Brush("#080B10"), 2=>Brush("#E9EEF4"), 3=>Brush(customPreviewBackground),
            _=>(System.Windows.Media.Brush)FindResource("Checker")
        };
        BackgroundColorSwatch.Background=Brush(customPreviewBackground);
    }
    private void PickBackgroundColorClick(object sender,RoutedEventArgs e)
    {
        var picker=new ColorPickerWindow(this,customPreviewBackground){Title="Preview background color"};
        if(picker.ShowDialog()==true)ApplyPreviewBackgroundColor(picker.SelectedHex);
    }
    private void ApplyPreviewBackgroundColor(string hex)
    {
        if(!FieldComposition.TryColor(hex,out _,out _,out _))return;
        customPreviewBackground=hex;
        PreviewBackground.SelectedIndex=3;
        UpdatePreviewBackground();
    }
}
