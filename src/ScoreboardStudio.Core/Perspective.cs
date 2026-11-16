namespace ScoreboardStudio.Core;

public readonly record struct NormalPoint(double X, double Y);

public sealed record PerspectiveSettings
{
    public bool Enabled { get; set; }
    public NormalPoint TopLeft { get; set; } = new(0, 0);
    public NormalPoint TopRight { get; set; } = new(1, 0);
    public NormalPoint BottomRight { get; set; } = new(1, 1);
    public NormalPoint BottomLeft { get; set; } = new(0, 1);
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public NormalPoint[] Corners() => [TopLeft, TopRight, BottomRight, BottomLeft];

    public bool IsValid(out string reason)
    {
        reason = "";
        if (Width < 32 || Height < 32 || Width > 4096 || Height > 2160)
        { reason = "Choose an output width of 32–4096 and height of 32–2160 pixels."; return false; }
        var p = Corners();
        if (p.Any(c => !double.IsFinite(c.X) || !double.IsFinite(c.Y) || c.X < 0 || c.X > 1 || c.Y < 0 || c.Y > 1))
        { reason = "Keep all four corners inside the image."; return false; }
        double area = 0;
        for (int i = 0; i < 4; i++)
        {
            var a = p[i]; var b = p[(i+1)%4]; var c = p[(i+2)%4];
            if ((b.X-a.X)*(c.Y-b.Y) - (b.Y-a.Y)*(c.X-b.X) <= .00001)
            { reason = "Corners must form a convex shape in order: top left, top right, bottom right, bottom left."; return false; }
            area += a.X*b.Y - b.X*a.Y;
        }
        if (area < .01) { reason = "The selected scoreboard area is too small."; return false; }
        return true;
    }
}

public static class PerspectiveTransform
{
    // Inverse mapping: every output pixel samples a point on the source quad.
    // Projective (homography), not a bilinear corner warp: straight lines stay straight.
    public static VideoFrame Apply(VideoFrame source, PerspectiveSettings settings)
    {
        if (!settings.Enabled) return source;
        if (!settings.IsValid(out var reason)) throw new ArgumentException(reason);
        var p = settings.Corners();
        double dx1=p[1].X-p[2].X, dx2=p[3].X-p[2].X, dx3=p[0].X-p[1].X+p[2].X-p[3].X;
        double dy1=p[1].Y-p[2].Y, dy2=p[3].Y-p[2].Y, dy3=p[0].Y-p[1].Y+p[2].Y-p[3].Y;
        double g=0, h=0;
        if (Math.Abs(dx3)+Math.Abs(dy3)>1e-12)
        {
            double determinant=dx1*dy2-dx2*dy1;
            if (Math.Abs(determinant)<1e-12) throw new ArgumentException("The selected corners cannot be straightened.");
            g=(dx3*dy2-dx2*dy3)/determinant; h=(dx1*dy3-dx3*dy1)/determinant;
        }
        double a=p[1].X-p[0].X+g*p[1].X, b=p[3].X-p[0].X+h*p[3].X, c0=p[0].X;
        double d=p[1].Y-p[0].Y+g*p[1].Y, e=p[3].Y-p[0].Y+h*p[3].Y, f=p[0].Y;
        int width=settings.Width, height=settings.Height;
        var output=new byte[width*height*4];
        Parallel.For(0, height, y =>
        {
            double v=y/(double)(height-1);
            for (int x=0; x<width; x++)
            {
                double u=x/(double)(width-1), denominator=g*u+h*v+1;
                double sx=Math.Clamp((a*u+b*v+c0)/denominator*(source.Width-1),0,source.Width-1);
                double sy=Math.Clamp((d*u+e*v+f)/denominator*(source.Height-1),0,source.Height-1);
                int x0=(int)sx, y0=(int)sy, x1=Math.Min(x0+1,source.Width-1), y1=Math.Min(y0+1,source.Height-1);
                double fx=sx-x0, fy=sy-y0;
                int i00=(y0*source.Width+x0)*4, i10=(y0*source.Width+x1)*4, i01=(y1*source.Width+x0)*4, i11=(y1*source.Width+x1)*4;
                double w00=(1-fx)*(1-fy), w10=fx*(1-fy), w01=(1-fx)*fy, w11=fx*fy;
                int target=(y*width+x)*4;
                // Interpolate premultiplied colors, then return straight BGRA.
                double a00=source.Pixels[i00+3]*w00, a10=source.Pixels[i10+3]*w10, a01=source.Pixels[i01+3]*w01, a11=source.Pixels[i11+3]*w11;
                double alpha=a00+a10+a01+a11;
                output[target+3]=(byte)Math.Clamp((int)Math.Round(alpha),0,255);
                if (alpha<=0) continue;
                for (int channel=0; channel<3; channel++)
                    output[target+channel]=(byte)Math.Clamp((int)Math.Round((source.Pixels[i00+channel]*a00+source.Pixels[i10+channel]*a10+source.Pixels[i01+channel]*a01+source.Pixels[i11+channel]*a11)/alpha),0,255);
            }
        });
        return new(width,height,output);
    }
}
