using System.Windows;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private void PickDigitColorClick(object sender,RoutedEventArgs e)
    {
        if(locked||Selected is not {} field)return;
        var picker=new ColorPickerWindow(this,field.Cleanup.CustomColor);
        if(picker.ShowDialog()==true)ApplyPickedColor(field,picker.SelectedHex);
    }
    private void ApplyPickedColor(ScoreField field,string hex)
    {
        if(locked||!FieldComposition.TryColor(hex,out _,out _,out _))return;
        // Apply the chosen shade without washing it out with earlier brightness.
        field.Cleanup=field.Cleanup with {Color="Custom",CustomColor=hex,Brightness=1};
        field.OutputColor=null;
        PopulateInspector();Changed();
        Notice.Text="Digit color applied at 1x brightness in both output modes.";
    }
    private void RefreshColorSwatches()
    {
        if(!ready||Selected is not {} field)return;
        DigitColorSwatch.Background=Brush(field.Cleanup.CustomColor);
    }
}
