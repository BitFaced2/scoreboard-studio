namespace ScoreboardStudio.Core;

public sealed record OutputSettings
{
    public string Mode {get;set;}="Individual";
    public int Width {get;set;}=1920;
    public int Height {get;set;}=1080;
    public string? GuidePath {get;set;}
    public bool IsValid()=>Mode is "Combined" or "Individual" && Width>=320 && Width<=3840 && Width%2==0 && Height>=180 && Height<=2160;
}

// Only cleaned live fields are composited. Guide artwork never enters this path.
public static class FieldComposition
{
    // Assign only the first position. Later edits and deliberately unplaced fields
    // belong to the operator, including after saving and reopening a setup.
    public static int PlaceNewFields(IReadOnlyList<ScoreField> fields,Crop? area=null)
    {
        var bounds=(area??new Crop(0,0,1,1)).Clamp();
        var occupied=fields.Where(f=>f.OutputRegion is not null).Select(f=>f.OutputRegion!).ToList();
        double Overlap(Crop a,Crop b)=>Math.Max(0,Math.Min(a.X+a.Width,b.X+b.Width)-Math.Max(a.X,b.X))*
            Math.Max(0,Math.Min(a.Y+a.Height,b.Y+b.Height)-Math.Max(a.Y,b.Y));
        int count=0;
        foreach(var field in fields)
        {
            if(field.OutputPlacementInitialized)continue;
            field.OutputPlacementInitialized=true;
            if(field.OutputRegion is not null)continue;
            Crop? position=null,fallback=null;double leastOverlap=double.MaxValue;
            // Try open spaces at progressively smaller sizes without moving existing boxes.
            foreach(int divisions in new[]{3,4,6,8,12,24})
            {
                for(int row=0;row<divisions&&position is null;row++)
                for(int col=0;col<divisions;col++)
                {
                    var candidate=new Crop(bounds.X+(col+.08)*bounds.Width/divisions,bounds.Y+(row+.15)*bounds.Height/divisions,
                        .84*bounds.Width/divisions,.66*bounds.Height/divisions);
                    double overlap=occupied.Sum(r=>Overlap(candidate,r))/(candidate.Width*candidate.Height);
                    if(overlap<leastOverlap){leastOverlap=overlap;fallback=candidate;}
                    if(overlap<.000001){position=candidate;break;}
                }
                if(position is not null)break;
            }
            // A canvas covered by an existing box still gets a selectable new field.
            field.OutputRegion=position??fallback!;occupied.Add(field.OutputRegion);count++;
        }
        return count;
    }

    public static VideoFrame Render(OutputSettings settings,IReadOnlyList<ScoreField> fields,IReadOnlyDictionary<Guid,VideoFrame> frames,bool healthy=true)
    {
        if(!settings.IsValid())throw new ArgumentException("Invalid output canvas.");
        var result=new VideoFrame(settings.Width,settings.Height,new byte[settings.Width*settings.Height*4]);
        if(!healthy)return result;
        foreach(var field in fields)
        {
            if(!field.Enabled || field.OutputRegion is not {} region || !frames.TryGetValue(field.Id,out var source))continue;
            var r=region.Clamp();
            double left=r.X*result.Width,top=r.Y*result.Height,w=r.Width*result.Width,h=r.Height*result.Height;
            if(field.OutputFit!="Stretch")
            {
                double scale=Math.Min(w/source.Width,h/source.Height),fw=source.Width*scale,fh=source.Height*scale;
                left+=(w-fw)/2;top+=(h-fh)/2;w=fw;h=fh;
            }
            bool tint=TryColor(field.OutputColor,out byte red,out byte green,out byte blue);
            int x0=Math.Max(0,(int)Math.Ceiling(left-.5)),x1=Math.Min(result.Width,(int)Math.Ceiling(left+w-.5));
            int y0=Math.Max(0,(int)Math.Ceiling(top-.5)),y1=Math.Min(result.Height,(int)Math.Ceiling(top+h-.5));
            Parallel.For(y0,y1,y=> {
                for(int x=x0;x<x1;x++)
                {
                    double sx=Math.Clamp((x+.5-left)*source.Width/w-.5,0,source.Width-1),sy=Math.Clamp((y+.5-top)*source.Height/h-.5,0,source.Height-1);
                    int ix=(int)sx,iy=(int)sy,jx=Math.Min(source.Width-1,ix+1),jy=Math.Min(source.Height-1,iy+1);
                    double fx=sx-ix,fy=sy-iy;
                    int a=(iy*source.Width+ix)*4,b=(iy*source.Width+jx)*4,c=(jy*source.Width+ix)*4,d=(jy*source.Width+jx)*4;
                    double wa=(1-fx)*(1-fy)*source.Pixels[a+3]/255.0,wb=fx*(1-fy)*source.Pixels[b+3]/255.0,wc=(1-fx)*fy*source.Pixels[c+3]/255.0,wd=fx*fy*source.Pixels[d+3]/255.0;
                    double alpha=wa+wb+wc+wd;if(alpha<=0)continue;
                    int target=(y*result.Width+x)*4;double background=result.Pixels[target+3]/255.0*(1-alpha),combined=alpha+background;
                    for(int channel=0;channel<3;channel++)
                    {
                        double foreground=tint?(channel==0?blue:channel==1?green:red)*alpha:
                            source.Pixels[a+channel]*wa+source.Pixels[b+channel]*wb+source.Pixels[c+channel]*wc+source.Pixels[d+channel]*wd;
                        result.Pixels[target+channel]=(byte)Math.Clamp((int)Math.Round((foreground+result.Pixels[target+channel]*background)/combined),0,255);
                    }
                    result.Pixels[target+3]=(byte)Math.Clamp((int)Math.Round(combined*255),0,255);
                }
            });
        }
        return result;
    }
    public static bool TryColor(string? color,out byte red,out byte green,out byte blue)
    {
        red=green=blue=0;
        if(color is null || !System.Text.RegularExpressions.Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$"))return false;
        red=Convert.ToByte(color.Substring(1,2),16);green=Convert.ToByte(color.Substring(3,2),16);blue=Convert.ToByte(color.Substring(5,2),16);return true;
    }
}
