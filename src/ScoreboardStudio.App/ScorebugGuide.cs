using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

internal sealed record GuideSlot(string Name,Crop Region,string Color)
{
    public override string ToString()=>Name;
}

// Read a static positioning guide, never execute/load GT markup as WPF code.
// This image is used only by the editor and is never passed to VideoEngine.
internal sealed class ScorebugGuide
{
    internal required VideoFrame Image {get;init;}
    internal List<GuideSlot> Slots {get;}=[];
    internal Crop Bounds {get;private set;}=new(0,0,1,1);
    internal string Notes {get;private set;}="";
    internal static ScorebugGuide Load(string path)
    {
        if(new FileInfo(path).Length>64L*1024*1024)throw new InvalidDataException("Use a guide smaller than 64 MB.");
        if(!Path.GetExtension(path).Equals(".gtzip",StringComparison.OrdinalIgnoreCase))
        {
            using var input=File.OpenRead(path);var bitmap=Decode(input);
            if(bitmap.PixelWidth>3840||bitmap.PixelHeight>2160||bitmap.PixelWidth<320||bitmap.PixelHeight<180||bitmap.PixelWidth%2!=0)
                throw new InvalidDataException("Guide images must have an even width, between 320 × 180 and 3840 × 2160.");
            return new() {Image=Pixels(bitmap),Notes="Image guide only. Place fields manually; artwork is never sent."};
        }
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Count>256||zip.Entries.Sum(e=>e.Length)>64L*1024*1024)throw new InvalidDataException("The GTZIP guide is too large.");
        XElement ReadXml(string name)
        {
            var entry=zip.GetEntry(name) ?? throw new InvalidDataException("GTZIP is missing "+name);
            if(entry.Length>2_000_000)throw new InvalidDataException("GTZIP document is too large.");
            using var stream=entry.Open();using var reader=new StreamReader(stream);
            using var xml=XmlReader.Create(new StringReader(reader.ReadToEnd()),new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2_000_000});
            return XElement.Load(xml);
        }
        var root=ReadXml("document.xml");
        if(root.Name.LocalName!="Composition")throw new InvalidDataException("Unsupported GTZIP document.");
        int width=(int)Number(root,"Width",1920),height=(int)Number(root,"Height",1080);
        if(!new OutputSettings {Width=width,Height=height}.IsValid())throw new InvalidDataException("GTZIP canvas must fit within 3840 × 2160 and have an even width.");
        var assets=ReadXml("resources.xml").Elements("resource").ToDictionary(e=>(string?)e.Attribute("filename")??"",e=>(string?)e.Element("source")?.Attribute("guid")??"");
        var slots=new List<GuideSlot>();var notes=new HashSet<string>();Rect bounds=Rect.Empty;
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            void Draw(XElement composition,double dx,double dy)
            {
                foreach(var element in composition.Elements())
                {
                    string type=element.Name.LocalName;
                    if(type=="Storyboard")continue;
                    var location=Vector(element,"Location",0,0);double x=dx+location.X,y=dy+location.Y;
                    if(type=="Layer")
                    {
                        if(element.Element("Layer.Composition")?.Element("Composition") is {} nested)Draw(nested,x,y);
                        continue;
                    }
                    if(type is not ("Rectangle" or "Ellipse" or "Image" or "TextBlock")) {notes.Add("Some GT objects are not supported in the static guide.");continue;}
                    var dimensions=Vector(element,"Dimensions",0,0);
                    if(dimensions.X<=0||dimensions.Y<=0)continue;
                    var rect=new Rect(x,y,dimensions.X,dimensions.Y);bounds.Union(rect);
                    var fill=Fill(element.Element(type+".Fill")?.Element("Brush"));
                    switch(type)
                    {
                        case "Rectangle":
                            var radius=Vector(element,"Radius",0,0);dc.DrawRoundedRectangle(fill,null,rect,radius.X,radius.Y);break;
                        case "Ellipse":dc.DrawEllipse(fill,null,new Point(x+rect.Width/2,y+rect.Height/2),rect.Width/2,rect.Height/2);break;
                        case "Image":
                            string imageName=(string?)element.Element("Image.Bitmap")?.Element("Bitmap")?.Attribute("Source")??"";
                            if(!assets.TryGetValue(imageName,out var asset)||zip.GetEntry(asset) is not {} entry) {notes.Add("A guide image could not be loaded.");break;}
                            using(var stream=entry.Open())dc.DrawImage(Decode(stream),rect);
                            break;
                        case "TextBlock":
                            string text=(string?)element.Attribute("Text")??"",name=(string?)element.Attribute("Name")??"Text";
                            // GT's DataFlags Hidden means hidden from its Title Editor,
                            // not visually hidden. Such labels remain in the guide.
                            if(!((string?)element.Attribute("DataFlags")??"").Contains("Hidden"))
                            {
                                var color=fill is SolidColorBrush solid?solid.Color:Colors.White;
                                slots.Add(new(name,new Crop(x/width,y/height,rect.Width/width,rect.Height/height).Clamp(),$"#{color.R:X2}{color.G:X2}{color.B:X2}"));break;
                            }
                            string family=(string?)element.Attribute("FontFamily")??"Segoe UI";
                            if(!Fonts.SystemFontFamilies.Any(f=>f.Source.Equals(family,StringComparison.OrdinalIgnoreCase))) {family="Segoe UI";notes.Add("A guide font is unavailable; labels use Segoe UI. Live digits are unchanged.");}
                            var formatted=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(family),Math.Clamp(Number(element,"FontSize",24),1,256),fill,1) {MaxTextWidth=rect.Width,MaxTextHeight=rect.Height,Trimming=TextTrimming.CharacterEllipsis};
                            formatted.TextAlignment=(string?)element.Attribute("TextAlign") switch {"Center"=>TextAlignment.Center,"Right"=>TextAlignment.Right,_=>TextAlignment.Left};
                            double ty=(string?)element.Attribute("VerticalAlign")=="Center"?y+(rect.Height-formatted.Height)/2:y;
                            dc.PushClip(new RectangleGeometry(rect));dc.DrawText(formatted,new(x,ty));dc.Pop();break;
                    }
                }
            }
            Draw(root,0,0);
        }
        var rendered=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);rendered.Render(visual);
        var result=new ScorebugGuide {Image=Pixels(rendered),Notes="Static positioning guide; editable-value placeholders are omitted. Artwork stays in vMix. "+string.Join(" ",notes)};
        result.Slots.AddRange(slots);
        if(!bounds.IsEmpty)result.Bounds=new Crop(Math.Max(0,bounds.X-15)/width,Math.Max(0,bounds.Y-15)/height,Math.Min(width,bounds.Width+30)/width,Math.Min(height,bounds.Height+30)/height).Clamp();
        return result;
    }
    private static BitmapSource Decode(Stream stream)
    {
        // ZIP entry streams are not seekable; WPF otherwise defers decoding.
        using var data=new MemoryStream();stream.CopyTo(data);data.Position=0;
        var source=BitmapDecoder.Create(data,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
        if((long)source.PixelWidth*source.PixelHeight>32_000_000)throw new InvalidDataException("Guide image dimensions are too large.");
        source.Freeze();return source;
    }
    private static VideoFrame Pixels(BitmapSource source)
    {
        var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        byte[] pixels=new byte[converted.PixelWidth*converted.PixelHeight*4];converted.CopyPixels(pixels,converted.PixelWidth*4,0);
        return new(converted.PixelWidth,converted.PixelHeight,pixels);
    }
    private static double Number(XElement e,string name,double fallback)
    {
        if(e.Attribute(name) is not {} attribute)return fallback;
        if(!double.TryParse(attribute.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)||!double.IsFinite(value)||Math.Abs(value)>100000)throw new InvalidDataException("Invalid GTZIP dimension.");
        return value;
    }
    private static Point Vector(XElement e,string name,double x,double y)
    {
        if(e.Attribute(name) is not {} attribute)return new(x,y);
        var parts=attribute.Value.Split(',');
        if(parts.Length<2||!double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out y)||!double.IsFinite(x)||!double.IsFinite(y)||Math.Abs(x)>100000||Math.Abs(y)>100000)throw new InvalidDataException("Invalid GTZIP coordinates.");
        return new(x,y);
    }
    private static System.Windows.Media.Brush Fill(XElement? brush)
    {
        if(brush is null)return Brushes.White;
        Color ColorOf(string? value) {try{return (Color)ColorConverter.ConvertFromString(value??"#FFFFFFFF");}catch{return Colors.White;}}
        if((string?)brush.Attribute("Type")=="LinearGradient")
        {
            var gradient=new LinearGradientBrush {StartPoint=Vector(brush,"StartPoint",0,0),EndPoint=Vector(brush,"EndPoint",1,0)};
            foreach(var stop in brush.Element("Brush.Stops")?.Elements("GradientStop")??[])gradient.GradientStops.Add(new(ColorOf((string?)stop.Attribute("Color")),Number(stop,"Position",0)));
            return gradient;
        }
        return new SolidColorBrush(ColorOf((string?)brush.Attribute("Color")));
    }
}
