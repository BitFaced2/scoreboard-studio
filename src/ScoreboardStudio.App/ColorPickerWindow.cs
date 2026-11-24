using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ScoreboardStudio.App;

// Local WPF picker: no browser, external dependency, or change to the field until Apply.
internal sealed class ColorPickerWindow : Window
{
    private readonly Canvas wheel=new(){Width=256,Height=256,Background=Brushes.Transparent};
    private readonly Ellipse marker=new(){Width=14,Height=14,Stroke=Brushes.Black,StrokeThickness=2,Fill=Brushes.Transparent,IsHitTestVisible=false};
    private readonly Ellipse markerInner=new(){Width=10,Height=10,Stroke=Brushes.White,StrokeThickness=2,IsHitTestVisible=false};
    private readonly Slider hue=new(){Minimum=0,Maximum=360,SmallChange=1,LargeChange=10};
    private readonly Slider saturation=new(){Minimum=0,Maximum=100,SmallChange=1,LargeChange=10};
    private readonly Slider value=new(){Minimum=0,Maximum=100,SmallChange=1,LargeChange=10};
    private readonly TextBlock preview=new(){Text="88:88",FontSize=38,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center};
    private readonly Border swatch=new(){Height=24,CornerRadius=new(4)};
    private readonly TextBlock readout=new(){HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,6,0,8)};
    private bool updating;
    internal string SelectedHex {get;private set;}="#FFFFFF";

    internal ColorPickerWindow(Window owner,string initial)
    {
        Owner=owner;Title="Choose digit color";Width=380;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        Resources=owner.Resources;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Background=new SolidColorBrush(Color.FromRgb(21,28,37));Foreground=Brushes.White;FontFamily=new("Segoe UI");FontSize=13;
        var panel=new StackPanel{Margin=new(20)};
        panel.Children.Add(new TextBlock{Text="Choose a color",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,5)});
        panel.Children.Add(new TextBlock{Text="Drag the wheel, then adjust lightness below.",Foreground=Brushes.LightSteelBlue,Margin=new(0,0,0,14)});
        var pixels=new byte[256*256*4];
        for(int y=0;y<256;y++)for(int x=0;x<256;x++)
        {
            double dx=x-127.5,dy=y-127.5,r=Math.Sqrt(dx*dx+dy*dy)/127.5;if(r>1)continue;
            var c=Hsv((Math.Atan2(-dy,dx)*180/Math.PI+360)%360,r,1);int i=(y*256+x)*4;
            pixels[i]=c.B;pixels[i+1]=c.G;pixels[i+2]=c.R;pixels[i+3]=255;
        }
        var bitmap=BitmapSource.Create(256,256,96,96,PixelFormats.Bgra32,null,pixels,1024);bitmap.Freeze();
        wheel.Children.Add(new Image{Source=bitmap,Width=256,Height=256,IsHitTestVisible=false});wheel.Children.Add(marker);wheel.Children.Add(markerInner);
        wheel.MouseLeftButtonDown+=(_,e)=>{wheel.CaptureMouse();Pick(e.GetPosition(wheel));e.Handled=true;};
        wheel.MouseMove+=(_,e)=>{if(wheel.IsMouseCaptured)Pick(e.GetPosition(wheel));};
        wheel.MouseLeftButtonUp+=(_,_)=>wheel.ReleaseMouseCapture();panel.Children.Add(wheel);
        void SliderRow(string title,Slider slider)
        {
            panel.Children.Add(new TextBlock{Text=title,Margin=new(0,10,0,2)});
            slider.Foreground=Brushes.DeepSkyBlue;slider.ValueChanged+=(_,_)=>{if(!updating)Refresh();};
            System.Windows.Automation.AutomationProperties.SetName(slider,title);panel.Children.Add(slider);
        }
        SliderRow("Hue",hue);SliderRow("Saturation",saturation);SliderRow("Lightness",value);
        var presets=new WrapPanel{Margin=new(0,12,0,10)};
        foreach(var hex in new[]{"#FFFFFF","#000000","#ED1824","#005BAB","#FFD54A","#33CC88","#AAAAAA"})
        {
            var button=new Button{Width=36,Height=28,Margin=new(0,0,7,0),Background=(SolidColorBrush)new BrushConverter().ConvertFromString(hex)!,ToolTip=hex};
            System.Windows.Automation.AutomationProperties.SetName(button,"Choose "+hex);
            button.Click+=(_,_)=>SetColor(hex);presets.Children.Add(button);
        }
        panel.Children.Add(presets);panel.Children.Add(swatch);
        panel.Children.Add(new Border{Background=new SolidColorBrush(Color.FromRgb(8,11,16)),Padding=new(8),Margin=new(0,8,0,0),CornerRadius=new(4),Child=preview});
        panel.Children.Add(readout);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="Cancel",IsCancel=true,Padding=new(16,8,16,8),Margin=new(0,0,8,0)};
        var apply=new Button{Content="Use color",IsDefault=true,Padding=new(16,8,16,8)};
        apply.Click+=(_,_)=>DialogResult=true;actions.Children.Add(cancel);actions.Children.Add(apply);panel.Children.Add(actions);
        SetColor(initial);
        MaxHeight=SystemParameters.WorkArea.Height;
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }
    internal void SetColor(string hex)
    {
        var c=(Color)ColorConverter.ConvertFromString(hex);double r=c.R/255.0,g=c.G/255.0,b=c.B/255.0;
        double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
        double h=delta==0?0:max==r?60*((g-b)/delta%6):max==g?60*((b-r)/delta+2):60*((r-g)/delta+4);
        updating=true;hue.Value=(h+360)%360;saturation.Value=max==0?0:delta/max*100;value.Value=max*100;updating=false;Refresh();
    }
    private void Pick(Point point)
    {
        double x=point.X-128,y=128-point.Y;
        updating=true;hue.Value=(Math.Atan2(y,x)*180/Math.PI+360)%360;saturation.Value=Math.Min(1,Math.Sqrt(x*x+y*y)/128)*100;updating=false;Refresh();
    }
    private void Refresh()
    {
        var c=Hsv(hue.Value,saturation.Value/100,value.Value/100);SelectedHex=$"#{c.R:X2}{c.G:X2}{c.B:X2}";
        swatch.Background=preview.Foreground=new SolidColorBrush(c);readout.Text=SelectedHex;
        double angle=hue.Value*Math.PI/180,r=saturation.Value/100*128,x=128+Math.Cos(angle)*r,y=128-Math.Sin(angle)*r;
        Canvas.SetLeft(marker,x-7);Canvas.SetTop(marker,y-7);Canvas.SetLeft(markerInner,x-5);Canvas.SetTop(markerInner,y-5);
    }
    internal static Color Hsv(double h,double s,double v)
    {
        double c=v*s,x=c*(1-Math.Abs(h/60%2-1)),m=v-c;
        var (r,g,b)=(h%360) switch {<60=>(c,x,0d),<120=>(x,c,0d),<180=>(0d,c,x),<240=>(0d,x,c),<300=>(x,0d,c),_=>(c,0d,x)};
        return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
    }
}
