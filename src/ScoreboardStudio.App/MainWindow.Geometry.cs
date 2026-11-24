using System.Windows;
using System.Windows.Controls;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private bool geometryUpdating;
    private static void SetGeometrySlider(Slider slider,TextBlock label,double value,double minimum,double maximum,string suffix)
    {
        slider.Minimum=minimum;slider.Maximum=Math.Max(minimum,maximum);slider.Value=value;
        label.Text=$"{value:0.0}{suffix}";
    }
    private void PopulateCrop()
    {
        if(!ready||Selected is not {} field)return;
        geometryUpdating=true;
        try
        {
            var r=field.Region;
            SetGeometrySlider(CropX,CropXValue,r.X*100,0,(1-r.Width)*100,"%");
            SetGeometrySlider(CropY,CropYValue,r.Y*100,0,(1-r.Height)*100,"%");
            SetGeometrySlider(CropW,CropWValue,r.Width*100,.1,(1-r.X)*100,"%");
            SetGeometrySlider(CropH,CropHValue,r.Height*100,.1,(1-r.Y)*100,"%");
        }
        finally {geometryUpdating=false;}
    }
    private void PopulateOutputGeometry()
    {
        if(!ready)return;
        geometryUpdating=true;
        try
        {
            var r=Selected?.OutputRegion;
            OutputGeometryControls.IsEnabled=r is not null&&!locked;
            double w=outputSettings.Width,h=outputSettings.Height;
            SetGeometrySlider(OutputX,OutputXValue,(r?.X??0)*w,0,(1-(r?.Width??0))*w," px");
            SetGeometrySlider(OutputY,OutputYValue,(r?.Y??0)*h,0,(1-(r?.Height??0))*h," px");
            SetGeometrySlider(OutputW,OutputWValue,(r?.Width??.001)*w,.001*w,(1-(r?.X??0))*w," px");
            SetGeometrySlider(OutputH,OutputHValue,(r?.Height??.001)*h,.001*h,(1-(r?.Y??0))*h," px");
            if(r is null)OutputXValue.Text=OutputYValue.Text=OutputWValue.Text=OutputHValue.Text="—";
        }
        finally {geometryUpdating=false;}
    }
    private void CropGeometryChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!ready||geometryUpdating||updating||locked||Selected is not {} field)return;
        var r=field.Region;double value=e.NewValue/100;
        if(sender==CropX)r=r with {X=Math.Clamp(value,0,1-r.Width)};
        else if(sender==CropY)r=r with {Y=Math.Clamp(value,0,1-r.Height)};
        else if(sender==CropW)r=r with {Width=Math.Clamp(value,.001,1-r.X)};
        else if(sender==CropH)r=r with {Height=Math.Clamp(value,.001,1-r.Y)};
        else return;
        if(r==field.Region)return;
        field.Region=r.Clamp();PopulateCrop();RenderBoxes();Changed();
    }
    private void OutputGeometryChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!ready||geometryUpdating||compositionUpdating||locked||Selected is not {} field||field.OutputRegion is not {} r)return;
        double value=e.NewValue;
        if(sender==OutputX)r=r with {X=Math.Clamp(value/outputSettings.Width,0,1-r.Width)};
        else if(sender==OutputY)r=r with {Y=Math.Clamp(value/outputSettings.Height,0,1-r.Height)};
        else if(sender==OutputW)r=r with {Width=Math.Clamp(value/outputSettings.Width,.001,1-r.X)};
        else if(sender==OutputH)r=r with {Height=Math.Clamp(value/outputSettings.Height,.001,1-r.Y)};
        else return;
        if(r==field.OutputRegion)return;
        field.OutputRegion=r.Clamp();field.OutputSlot=null;
        compositionUpdating=true;GuideTarget.SelectedIndex=0;compositionUpdating=false;
        PopulateOutputGeometry();RenderCompositionBoxes();Changed();
    }
}
