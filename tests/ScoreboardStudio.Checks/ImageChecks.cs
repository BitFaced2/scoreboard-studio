using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScoreboardStudio.Core;

internal static class ImageChecks
{
    internal static VideoFrame Load(string path)
    {
        var decoder = BitmapDecoder.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return new(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }

    internal static void Save(VideoFrame frame, string path)
    {
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels, frame.Stride);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    internal static void Inspect(string imagePath, string layoutPath, string directory)
    {
        Directory.CreateDirectory(directory);
        var frame = Load(imagePath);
        var layout = Layout.Load(layoutPath);
        Console.WriteLine($"  Image: {frame.Width} x {frame.Height}");
        Console.WriteLine($"  Baseline detector: {FieldDetector.Detect(frame, 90).Count} proposals");
        var detected = FieldDetector.Detect(frame,170,"White highlights");
        Console.WriteLine($"  White-highlight detector: {detected.Count} proposals");
        if (detected.Count<5) throw new Exception("Real scoreboard detection lost most candidate fields.");
        File.WriteAllText(Path.Combine(directory,"proposals.json"),JsonSerializer.Serialize(detected,new JsonSerializerOptions { WriteIndented=true }));
        var measurements = new List<object>();
        foreach (var field in layout.Fields)
        {
            var original = FrameProcessor.Crop(frame, field.Region);
            var cleaned = FrameProcessor.Process(frame, field.Region, field.Cleanup);
            Save(original, Path.Combine(directory, field.Name + "-original.png"));
            Save(cleaned, Path.Combine(directory, field.Name + "-cleaned.png"));
            double coverage = cleaned.Pixels.Where((_,i) => i % 4 == 3).Average(a => (double)a / 255);
            Console.WriteLine($"  {field.Name}: {coverage:P1} opaque coverage");
            measurements.Add(new { field.Name, Coverage = coverage });
            if (coverage < .03 || coverage > .6) throw new Exception($"{field.Name} has implausible foreground coverage: {coverage:P1}.");
        }
        File.WriteAllText(Path.Combine(directory, "measurements.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        RenderComparisons(frame,layout,directory,detected);
        var correction=new PerspectiveSettings { Enabled=true,Width=1280,Height=720,TopLeft=new(.025,.02),TopRight=new(.985,.04),BottomRight=new(.95,.98),BottomLeft=new(.04,.96) };
        var watch=System.Diagnostics.Stopwatch.StartNew();
        VideoFrame? corrected=null;
        for(int i=0;i<5;i++) corrected=PerspectiveTransform.Apply(frame,correction);
        Console.WriteLine($"  1280 x 720 perspective: {watch.Elapsed.TotalMilliseconds/5:0.0} ms average");
        Save(corrected!,Path.Combine(directory,"perspective-example.png"));
    }

    private static void RenderComparisons(VideoFrame frame, Layout layout, string directory, List<Crop> proposals)
    {
        Exception? error=null;
        var thread=new Thread(() =>
        {
            try
            {
                var visual=new DrawingVisual();
                using(var dc=visual.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(13,17,23)),null,new Rect(0,0,1000,layout.Fields.Count/2*220));
                    for(int i=0;i<layout.Fields.Count;i++)
                    {
                        int x=(i%2)*500,y=(i/2)*220;
                        var field=layout.Fields[i];
                        dc.DrawText(new FormattedText(field.Name,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),17,Brushes.White,1),new Point(x+15,y+10));
                        DrawFit(dc,FrameProcessor.Crop(frame,field.Region),new Rect(x+15,y+40,470,72));
                        for(int yy=y+123;yy<y+209;yy+=10) for(int xx=x+15;xx<x+485;xx+=10)
                            dc.DrawRectangle(new SolidColorBrush(((xx-x)/10+(yy-y)/10)%2==0 ? Color.FromRgb(30,40,50):Color.FromRgb(45,55,65)),null,new Rect(xx,yy,10,10));
                        DrawFit(dc,FrameProcessor.Process(frame,field.Region,field.Cleanup),new Rect(x+15,y+128,470,72));
                    }
                }
                SaveVisual(visual,1000,layout.Fields.Count/2*220,Path.Combine(directory,"comparison.png"));
                var overlay=new DrawingVisual();
                using(var dc=overlay.RenderOpen())
                {
                    DrawFit(dc,frame,new Rect(0,0,frame.Width,frame.Height));
                    foreach(var crop in proposals)
                        dc.DrawRectangle(null,new Pen(Brushes.Lime,3),new Rect(crop.X*frame.Width,crop.Y*frame.Height,crop.Width*frame.Width,crop.Height*frame.Height));
                }
                SaveVisual(overlay,frame.Width,frame.Height,Path.Combine(directory,"detection.png"));
            }
            catch(Exception ex) { error=ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(error is not null) throw error;
    }
    private static void DrawFit(DrawingContext dc,VideoFrame frame,Rect target)
    {
        var bitmap=BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Bgra32,null,frame.Pixels,frame.Stride);
        double scale=Math.Min(target.Width/frame.Width,target.Height/frame.Height);
        double w=frame.Width*scale,h=frame.Height*scale;
        dc.DrawImage(bitmap,new Rect(target.X+(target.Width-w)/2,target.Y+(target.Height-h)/2,w,h));
    }
    private static void SaveVisual(Visual visual,int width,int height,string path)
    {
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
}
