using ScoreboardStudio.Core;

internal static class StabilizationChecks
{
    internal static VideoFrame Board()
    {
        int w=640,h=360;var pixels=new byte[w*h*4];var random=new Random(173);
        var texture=new byte[w/4*h/4];random.NextBytes(texture);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            int p=(y*w+x)*4;byte value=(byte)(35+texture[(y/4)*(w/4)+x/4]*.7);
            pixels[p]=pixels[p+1]=pixels[p+2]=value;pixels[p+3]=255;
        }
        return new(w,h,pixels);
    }
    internal static void MotionAndRecovery()
    {
        var reference=Board();var tracker=new CameraStabilizer();
        Require(tracker.Process(reference,null).Healthy,"Textured reference was rejected.");
        foreach(var (angle,scale,x,y) in new[]{(0.0,1.0,12.0,-8.0),(.018,1.02,-8.0,6.0),(-.025,.99,10.0,4.0)})
        {
            double a=Math.Cos(angle)*scale,b=Math.Sin(angle)*scale,den=a*a+b*b;
            var frame=CameraStabilizer.Warp(reference,a/den,-b/den,(-a*x-b*y)/den,(b*x-a*y)/den);
            // A changing number must not move the reference or require rediscovery.
            for(int yy=150;yy<195;yy++)for(int xx=270;xx<340;xx++)for(int c=0;c<3;c++)frame.Pixels[(yy*640+xx)*4+c]=255;
            var result=tracker.Process(frame,null);
            Console.WriteLine($"  Motion: {tracker.Transform}; expected {a},{b},{x},{y}");
            Require(result.Healthy,result.Status);
            double error=0,uncorrected=0;int count=0;
            for(int yy=40;yy<320;yy+=2)for(int xx=40;xx<600;xx+=2)
            {
                if(xx>240&&xx<370&&yy>120&&yy<230)continue;
                int p=(yy*640+xx)*4;error+=Math.Abs(result.Frame!.Pixels[p]-reference.Pixels[p]);uncorrected+=Math.Abs(frame.Pixels[p]-reference.Pixels[p]);count++;
            }
            Console.WriteLine($"  Stabilized error: {error/count:0.0}/255 ({result.Status})");
            Require(error<uncorrected*.65,"Stabilization did not substantially improve alignment.");
            foreach(var (px,py) in new[]{(40.0,40.0),(600.0,40.0),(600.0,320.0),(40.0,320.0)})
            {
                var t=tracker.Transform;
                double dx=(t.A-a)*px-(t.B-b)*py+t.X-x,dy=(t.B-b)*px+(t.A-a)*py+t.Y-y;
                Require(Math.Sqrt(dx*dx+dy*dy)<1.5,"Recovered camera geometry differs by more than 1.5 source pixels.");
            }
        }
        var blank=new VideoFrame(640,360,new byte[640*360*4]);
        Require(!tracker.Process(blank,null).Healthy,"Blank camera frame was accepted as tracked.");
        Require(tracker.Process(reference with {Pixels=(byte[])reference.Pixels.Clone()},null).Healthy,"Reference did not recover after loss.");
        Require(!new CameraStabilizer().Process(blank,null).Healthy,"Blank reference should require a new reference.");
        Require(!tracker.Process(new(320,180,new byte[320*180*4]),null).Healthy,"Changed resolution should require realignment.");
    }
    internal static void EngineKeepsFields()
    {
        var source=new TestPlayback {Snapshot=new(Board(),"Playing",true)};
        var field=new ScoreField {Region=new(.2,.2,.3,.3),Cleanup=new(){RemoveBackground=false}};
        var region=field.Region;
        using var engine=new VideoEngine();
        var settings=new StabilizationSettings(true,1);
        engine.Configure(new("Video",null,null,[field],false,1,Playback:source,Stabilization:settings));
        Wait(()=>engine.Preview.Healthy&&engine.Preview.Fields.Count==1);
        var first=engine.Preview.Fields[field.Id];
        Thread.Sleep(120);
        Require(ReferenceEquals(first,engine.Preview.Fields[field.Id]),"Unchanged video frames were reprocessed.");
        source.Snapshot=new(CameraStabilizer.Warp(Board(),1,0,-10,6),"Playing",true);
        Wait(()=>engine.Preview.Healthy&&!ReferenceEquals(first,engine.Preview.Fields[field.Id]));
        Require(field.Region==region,"Tracking changed the field crop.");
        source.Snapshot=new(new(640,360,new byte[640*360*4]),"Playing",true);
        Wait(()=>!engine.Preview.Healthy);
        Require(engine.Preview.Fields[field.Id].Pixels.All(p=>p==0),"Tracking loss retained visible output.");
        source.Snapshot=new(Board(),"Playing",true);
        Wait(()=>engine.Preview.Healthy);
        Require(field.Region==region,"Recovery changed the field crop.");
    }
    private sealed class TestPlayback:IPlaybackSource
    {
        private PlaybackSnapshot snapshot=new(null,"",false);
        public PlaybackSnapshot Snapshot {get=>Volatile.Read(ref snapshot);set=>Volatile.Write(ref snapshot,value);}
    }
    private static void Require(bool condition,string message) {if(!condition)throw new Exception(message);}
    private static void Wait(Func<bool> ready)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        while(timer.Elapsed.TotalSeconds<8) {if(ready())return;Thread.Sleep(30);}
        throw new Exception("Timed out waiting for stabilization pipeline.");
    }
}
