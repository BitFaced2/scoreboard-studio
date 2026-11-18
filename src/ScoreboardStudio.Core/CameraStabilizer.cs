namespace ScoreboardStudio.Core;

public sealed record StabilizationSettings(bool Enabled = false, int Reference = 0, Crop? Area = null);
public sealed record StabilizationResult(VideoFrame? Frame, bool Healthy, string Status);

// Fixed-reference feature patches: never follows the digits by moving crop boxes,
// and never silently replaces the reference when tracking is lost.
public sealed class CameraStabilizer
{
    private sealed record Patch(int X, int Y, double Mean, byte[] Values);
    private readonly record struct Match(double X, double Y, double U, double V);
    private readonly record struct Motion(double A, double B, double X, double Y)
    {
        public (double X, double Y) Map(double x, double y) => (A*x-B*y+X, B*x+A*y+Y);
    }
    private readonly List<Patch> patches = [];
    private int width, height, sourceWidth, sourceHeight;
    private VideoFrame? lastSource;
    private StabilizationResult? lastResult;
    private const int Radius = 5;
    public (double A,double B,double X,double Y) Transform {get;private set;}=(1,0,0,0);

    public StabilizationResult Process(VideoFrame source, Crop? area)
    {
        if (ReferenceEquals(source, lastSource)) return lastResult!;
        if (lastSource is null)
        {
            sourceWidth=source.Width; sourceHeight=source.Height;
            width=Math.Min(320, source.Width); height=Math.Max(1,(int)Math.Round(source.Height*width/(double)source.Width));
            var reference=Gray(source);
            FindPatches(reference, (area ?? new Crop(0,0,1,1)).Clamp());
            lastSource=source;
            return lastResult = patches.Count >= 8
                ? new(source,true,"Stabilization ready • Reference set")
                : new(null,false,"Stabilization needs more detail • Reframe the board, then Set reference");
        }
        lastSource=source;
        if (source.Width!=sourceWidth || source.Height!=sourceHeight)
            return lastResult=new(null,false,"Source size changed • Review alignment and Set reference");
        if (patches.Count<8) return lastResult!;
        var gray=Gray(source);
        var matches=new List<Match>();
        int search=Math.Max(8,width/10);
        foreach (var patch in patches)
        {
            double best=double.MaxValue; int bx=0,by=0;
            for(int y=Math.Max(Radius,patch.Y-search); y<=Math.Min(height-Radius-1,patch.Y+search); y+=2)
                for(int x=Math.Max(Radius,patch.X-search); x<=Math.Min(width-Radius-1,patch.X+search); x+=2)
                {
                    double error=Error(gray,patch,x,y);
                    if(error<best) { best=error; bx=x; by=y; }
                }
            int cx=bx,cy=by;
            for(int y=Math.Max(Radius,cy-2);y<=Math.Min(height-Radius-1,cy+2);y++)
                for(int x=Math.Max(Radius,cx-2);x<=Math.Min(width-Radius-1,cx+2);x++)
                {
                    double error=Error(gray,patch,x,y);
                    if(error<best) { best=error; bx=x; by=y; }
                }
            if(best<24)
            {
                double ux=bx,uy=by;
                for(double y=by-.75;y<=by+.75;y+=.25)for(double x=bx-.75;x<=bx+.75;x+=.25)
                {
                    if(x<Radius||y<Radius||x>=width-Radius-1||y>=height-Radius-1)continue;
                    double error=SubpixelError(gray,patch,x,y);
                    if(error<best) {best=error;ux=x;uy=y;}
                }
                matches.Add(new(patch.X,patch.Y,ux,uy));
            }
        }
        var inliers=new List<Match>();
        for(int i=0;i<matches.Count;i++) for(int j=i+1;j<matches.Count;j++)
        {
            var p=matches[i];var q=matches[j];double dx=q.X-p.X,dy=q.Y-p.Y,den=dx*dx+dy*dy;
            if(den<400)continue;
            double du=q.U-p.U,dv=q.V-p.V;
            double a=(dx*du+dy*dv)/den,b=(dx*dv-dy*du)/den;
            var motion=new Motion(a,b,p.U-a*p.X+b*p.Y,p.V-b*p.X-a*p.Y);
            if(!Plausible(motion))continue;
            var agreed=matches.Where(m=>Residual(motion,m)<2.0).ToList();
            if(agreed.Count>inliers.Count)inliers=agreed;
        }
        if(inliers.Count<Math.Max(8,(int)Math.Ceiling(patches.Count*.5)))
            return lastResult=new(null,false,"Tracking lost • Outputs transparent; return camera or Set reference after realigning");
        double mx=inliers.Average(p=>p.X),my=inliers.Average(p=>p.Y),mu=inliers.Average(p=>p.U),mv=inliers.Average(p=>p.V);
        double divisor=0,aa=0,bb=0;
        foreach(var p in inliers) { double x=p.X-mx,y=p.Y-my,u=p.U-mu,v=p.V-mv;divisor+=x*x+y*y;aa+=x*u+y*v;bb+=x*v-y*u; }
        var fitted=new Motion(aa/divisor,bb/divisor,mu-aa/divisor*mx+bb/divisor*my,mv-bb/divisor*mx-aa/divisor*my);
        if(!Plausible(fitted))return lastResult=new(null,false,"Tracking lost • Review camera alignment");
        double scale=sourceWidth/(double)width;
        Transform=(fitted.A,fitted.B,fitted.X*scale,fitted.Y*scale);
        var pixels=Warp(source,fitted.A,fitted.B,fitted.X*scale,fitted.Y*scale);
        return lastResult=new(pixels,true,$"Stabilizing • {inliers.Count}/{patches.Count} reference points");
    }
    private bool Plausible(Motion m) => double.IsFinite(m.A) && Math.Abs(m.A*m.A+m.B*m.B-1)<.12
        && Math.Abs(Math.Atan2(m.B,m.A))<Math.PI/36 && Math.Abs(m.X)<width*.15 && Math.Abs(m.Y)<width*.15;
    private static double Residual(Motion m,Match p) {var q=m.Map(p.X,p.Y);return Math.Sqrt(Math.Pow(q.X-p.U,2)+Math.Pow(q.Y-p.V,2));}
    private byte[] Gray(VideoFrame f)
    {
        var result=new byte[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int p=((int)((y+.5)*f.Height/height)*f.Width+(int)((x+.5)*f.Width/width))*4;
            result[y*width+x]=(byte)((f.Pixels[p]*29+f.Pixels[p+1]*150+f.Pixels[p+2]*77)>>8);
        }
        return result;
    }
    private void FindPatches(byte[] image,Crop area)
    {
        // Spatial distribution prevents one changing digit from dominating the fit.
        for(int gy=0;gy<5;gy++)for(int gx=0;gx<7;gx++)
        {
            int x0=Math.Max(Radius+1,(int)((area.X+area.Width*gx/7)*width));
            int x1=Math.Min(width-Radius-2,(int)((area.X+area.Width*(gx+1)/7)*width));
            int y0=Math.Max(Radius+1,(int)((area.Y+area.Height*gy/5)*height));
            int y1=Math.Min(height-Radius-2,(int)((area.Y+area.Height*(gy+1)/5)*height));
            double best=700;int bx=-1,by=-1;
            for(int y=y0;y<=y1;y+=2)for(int x=x0;x<=x1;x+=2)
            {
                double xx=0,xy=0,yy=0;
                for(int dy=-3;dy<=3;dy+=2)for(int dx=-3;dx<=3;dx+=2)
                { int p=(y+dy)*width+x+dx;double a=image[p+1]-image[p-1],b=image[p+width]-image[p-width];xx+=a*a;xy+=a*b;yy+=b*b; }
                double score=(xx+yy-Math.Sqrt((xx-yy)*(xx-yy)+4*xy*xy))/2;
                if(score>best) {best=score;bx=x;by=y;}
            }
            if(bx<0 || patches.Any(p=>Math.Abs(p.X-bx)<8&&Math.Abs(p.Y-by)<8))continue;
            var values=new List<byte>();
            for(int dy=-Radius;dy<=Radius;dy++)for(int dx=-Radius;dx<=Radius;dx++)values.Add(image[(by+dy)*width+bx+dx]);
            patches.Add(new(bx,by,values.Average(v=>(double)v),values.ToArray()));
        }
    }
    private double Error(byte[] image,Patch patch,int x,int y)
    {
        double sum=0,error=0;int i=0;
        for(int dy=-Radius;dy<=Radius;dy++)for(int dx=-Radius;dx<=Radius;dx++)sum+=image[(y+dy)*width+x+dx];
        double offset=sum/patch.Values.Length-patch.Mean;
        for(int dy=-Radius;dy<=Radius;dy++)for(int dx=-Radius;dx<=Radius;dx++)error+=Math.Abs(image[(y+dy)*width+x+dx]-patch.Values[i++]-offset);
        return error/patch.Values.Length;
    }
    private double SubpixelError(byte[] image,Patch patch,double x,double y)
    {
        Span<double> values=stackalloc double[121];double sum=0;int i=0;
        int x0=(int)x,y0=(int)y;double fx=x-x0,fy=y-y0;
        for(int dy=-Radius;dy<=Radius;dy++)for(int dx=-Radius;dx<=Radius;dx++)
        {
            int p=(y0+dy)*width+x0+dx;
            double v=image[p]*(1-fx)*(1-fy)+image[p+1]*fx*(1-fy)+image[p+width]*(1-fx)*fy+image[p+width+1]*fx*fy;
            values[i++]=v;sum+=v;
        }
        double offset=sum/i-patch.Mean,error=0;
        for(int j=0;j<i;j++)error+=Math.Abs(values[j]-patch.Values[j]-offset);
        return error/i;
    }
    // Inverse sampling from the fixed reference into the current camera picture.
    // Exposed for deterministic camera-motion checks; uncovered edges are transparent.
    public static VideoFrame Warp(VideoFrame source,double a,double b,double tx,double ty)
    {
        int w=source.Width,h=source.Height;var pixels=new byte[w*h*4];
        Parallel.For(0,h,y=> {
            for(int x=0;x<w;x++)
            {
                double sx=a*x-b*y+tx,sy=b*x+a*y+ty;
                if(sx<0||sy<0||sx>w-1||sy>h-1)continue;
                int x0=(int)sx,y0=(int)sy,x1=Math.Min(w-1,x0+1),y1=Math.Min(h-1,y0+1);
                double fx=sx-x0,fy=sy-y0;
                int p0=(y0*w+x0)*4,p1=(y0*w+x1)*4,p2=(y1*w+x0)*4,p3=(y1*w+x1)*4,t=(y*w+x)*4;
                double v0=(1-fx)*(1-fy)*source.Pixels[p0+3],v1=fx*(1-fy)*source.Pixels[p1+3],v2=(1-fx)*fy*source.Pixels[p2+3],v3=fx*fy*source.Pixels[p3+3],alpha=v0+v1+v2+v3;
                pixels[t+3]=(byte)Math.Clamp((int)Math.Round(alpha),0,255);
                if(alpha<=0)continue;
                for(int c=0;c<3;c++)pixels[t+c]=(byte)Math.Clamp((int)Math.Round((source.Pixels[p0+c]*v0+source.Pixels[p1+c]*v1+source.Pixels[p2+c]*v2+source.Pixels[p3+c]*v3)/alpha),0,255);
            }
        });
        return new(w,h,pixels);
    }
}
