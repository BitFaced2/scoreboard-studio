using ScoreboardStudio.Core;
using System.IO;

internal static class CompositionChecks
{
    internal static void Register(Action<string,Action> check,bool ndi)
    {
        check("Initial output placement separates fields and preserves edits and deliberate unplacement",()=> {
            var fields=Enumerable.Range(0,24).Select(i=>new ScoreField {Name="Field "+i}).ToArray();
            Require(fields.All(f=>f.OutputFit=="Stretch"),"New fields should stretch to their output boxes.");
            fields[0].OutputFit="Contain";
            var crops=fields.Select(f=>f.Region).ToArray();
            Require(FieldComposition.PlaceNewFields(fields)==24,"Not all new fields were placed.");
            foreach(var a in fields)
            {
                var r=a.OutputRegion!;
                Require(r.X>=0&&r.Y>=0&&r.X+r.Width<=1&&r.Y+r.Height<=1,"Initial position outside canvas.");
                foreach(var b in fields.Where(b=>b.Id!=a.Id))
                {var s=b.OutputRegion!;Require(r.X+r.Width<=s.X||s.X+s.Width<=r.X||r.Y+r.Height<=s.Y||s.Y+s.Height<=r.Y,"Initial boxes overlap.");}
            }
            fields[0].OutputRegion=new(.2,.3,.2,.1);fields[1].OutputRegion=null;
            var positions=fields.Select(f=>f.OutputRegion).ToArray();
            Require(FieldComposition.PlaceNewFields(fields)==0&&fields.Select(f=>f.OutputRegion).SequenceEqual(positions),"Auto placement changed an edit or unplaced field.");
            string path=Path.Combine(Path.GetTempPath(),"placement-"+Guid.NewGuid()+".json");
            try
            {
                new Layout {Output=new(){Mode="Combined"},Fields=fields.Select(f=>f.Snapshot()).ToList()}.Save(path);
                var loaded=Layout.Load(path);FieldComposition.PlaceNewFields(loaded.Fields);
                Require(loaded.Fields.Select(f=>f.OutputRegion).SequenceEqual(positions),"Reload or snapshot lost intentional placement state.");
                Require(loaded.Fields[0].OutputFit=="Contain"&&loaded.Fields[1].OutputFit=="Stretch","Reload changed the saved sizing choice.");
                Require(loaded.Fields.Select(f=>f.Region).SequenceEqual(crops),"Placement changed source crops.");
            }
            finally {File.Delete(path);}
        });
        check("Combined canvas preserves transparency, placement, aspect ratio, tint and stacking",()=> {
            var canvas=new OutputSettings {Mode="Combined",Width=320,Height=180};
            var a=new ScoreField {OutputRegion=new(.1,.1,.25,.5),OutputFit="Contain",Cleanup=new(){RemoveBackground=false}};
            var b=new ScoreField {OutputRegion=new(.1,.1,.25,.5),OutputFit="Stretch",OutputColor="#0000FF"};
            var frames=new Dictionary<Guid,VideoFrame> {[a.Id]=Solid(80,20,255,0,0),[b.Id]=Solid(80,90,0,255,0,128)};
            var output=FieldComposition.Render(canvas,[a],frames);
            Require(At(output,40,20,3)==0&&At(output,40,60,2)==255&&At(output,40,60,3)==255,"Aspect fit or transparent margins failed.");
            output=FieldComposition.Render(canvas,[a,b],frames);
            Require(Math.Abs(At(output,40,60,0)-128)<2&&Math.Abs(At(output,40,60,2)-127)<2&&At(output,40,60,3)==255,"Straight-alpha blend or layer order failed.");
            Require(At(output,40,20,0)==255&&At(output,40,20,3)==128,"Tint did not preserve source alpha.");
            b.Enabled=false;Require(FieldComposition.Render(canvas,[a,b],frames).Pixels.SequenceEqual(FieldComposition.Render(canvas,[a],frames).Pixels),"Hidden field was transmitted.");
            a.OutputRegion=null;Require(FieldComposition.Render(canvas,[a,b],frames).Pixels.All(x=>x==0),"Unplaced field was transmitted.");
            a.OutputRegion=new(.99,.95,.1,.1);Require(FieldComposition.Render(canvas,[a],frames).Pixels.Any(x=>x>0),"Edge placement failed.");
            Require(FieldComposition.Render(canvas,[a],frames,false).Pixels.All(x=>x==0),"Source loss left visible composite pixels.");
        });
        check("Composition settings round-trip and legacy setups keep separate outputs",()=> {
            string path=Path.Combine(Path.GetTempPath(),"composition-"+Guid.NewGuid()+".json");
            try
            {
                var field=new ScoreField {Region=new(.1,.2,.3,.4),OutputRegion=new(.5,.6,.2,.1),OutputColor="#000000",OutputFit="Stretch",OutputSlot="Clock"};
                new Layout {Output=new() {Mode="Combined",Width=1280,Height=720,GuidePath="../scorebug/Tipoff.gtzip"},Fields=[field]}.Save(path);
                var loaded=Layout.Load(path);
                Require(loaded.Version==2&&loaded.Output.Mode=="Combined"&&loaded.Output.GuidePath=="../scorebug/Tipoff.gtzip"&&loaded.Output.Width==1280,"Canvas settings were lost.");
                var actual=loaded.Fields[0];Require(actual.Region==field.Region&&actual.OutputRegion==field.OutputRegion&&actual.OutputColor==field.OutputColor&&actual.OutputFit==field.OutputFit&&actual.OutputSlot==field.OutputSlot,"Field placement settings were lost.");
                File.WriteAllText(path,"{\"Version\":1,\"Fields\":[]}");Require(Layout.Load(path).Output.Mode=="Individual","Legacy setup switched output mode.");
                File.WriteAllText(path,"{\"Version\":2,\"Output\":{\"Mode\":\"Combined\",\"Width\":99999},\"Fields\":[]}");
                bool rejected=false;try{Layout.Load(path);}catch(InvalidDataException){rejected=true;}Require(rejected,"Unbounded canvas was accepted.");
            }
            finally {File.Delete(path);File.Delete(path+".tmp");}
        });
        check("Combined pipeline updates placement independently, caches paused frames and clears source loss",()=>Pipeline(false));
        if(ndi)check("A single combined NDI sender carries multiple editable fields and clears lost input",()=>Pipeline(true));
    }
    private static void Pipeline(bool ndi)
    {
        var canvas=new OutputSettings {Mode="Combined",Width=320,Height=180};
        var a=new ScoreField {Name="Left",Region=new(0,0,.5,1),OutputRegion=new(.1,.1,.2,.2),OutputFit="Stretch",Cleanup=new(){RemoveBackground=false,Color="Original"}};
        var b=new ScoreField {Name="Right",Region=new(.5,0,.5,1),OutputRegion=new(.6,.6,.2,.2),OutputFit="Stretch",Cleanup=new(){RemoveBackground=false,Color="Original"}};
        var frame=Solid(100,50,220,0,0);for(int y=0;y<50;y++)for(int x=50;x<100;x++){frame.Pixels[(y*100+x)*4]=200;frame.Pixels[(y*100+x)*4+2]=0;}
        var playback=new Playback {Snapshot=new(frame,"Playing",true)};
        using var engine=new VideoEngine();
        void Configure()=>engine.Configure(new("Video",null,null,[a.Snapshot(),b.Snapshot()],ndi,1,Playback:playback,OutputSettings:canvas));
        Configure();Wait(()=>engine.Preview.Combined is {} f&&At(f,40,25,2)==220);
        var first=engine.Preview.Combined;Thread.Sleep(100);Require(ReferenceEquals(first,engine.Preview.Combined),"Paused composite was rebuilt.");
        using var finder=ndi?new NdiDiscovery():null;NdiSource? source=null;
        if(ndi)Wait(()=> (source=finder!.GetSources().FirstOrDefault(s=>s.Name.Contains(VideoEngine.OutputName("Video","Combined")))) is not null);
        using var receiver=ndi?new NdiReceiver(source!):null;
        void Receive(Func<VideoFrame,bool> check) {if(receiver is not null)Wait(()=>receiver.Capture(50) is {} f&&check(f));}
        Receive(f=>f.Width==320&&f.Height==180&&At(f,40,25,2)>200&&At(f,205,120,0)>180&&At(f,150,80,3)<10);
        if(ndi)Require(engine.Preview.OutputCount==1,"Combined mode created more than one sender.");
        var sourceCrop=a.Region;a.OutputRegion=new(.3,.1,.2,.2);a.Name="Renamed";Configure();
        Wait(()=>engine.Preview.Combined is {} f&&At(f,40,25,3)==0&&At(f,110,25,2)==220);
        Require(a.Region==sourceCrop,"Output positioning changed the source crop.");
        Receive(f=>At(f,40,25,3)<10&&At(f,110,25,2)>200&&At(f,205,120,0)>180);
        playback.Snapshot=new(frame,"NO SIGNAL",false);Wait(()=>!engine.Preview.Healthy&&engine.Preview.Combined!.Pixels.All(v=>v==0));
        Receive(f=>f.Pixels.Where((_,i)=>i%4==3).All(v=>v<10));
        playback.Snapshot=new(frame,"Playing",true);Wait(()=>engine.Preview.Healthy&&At(engine.Preview.Combined!,110,25,2)==220);
        if(ndi)
        {
            canvas=canvas with {Mode="Individual"};Configure();Wait(()=>engine.Preview.OutputCount==2&&engine.Preview.Combined is null);
            canvas=canvas with {Mode="Combined"};Configure();Wait(()=>engine.Preview.OutputCount==1&&engine.Preview.Combined is not null);
        }
    }
    private sealed class Playback:IPlaybackSource
    {
        private PlaybackSnapshot snapshot=new(null,"",false);
        public PlaybackSnapshot Snapshot {get=>Volatile.Read(ref snapshot);set=>Volatile.Write(ref snapshot,value);}
    }
    private static VideoFrame Solid(int w,int h,byte r,byte g,byte b,byte a=255)
    {var data=new byte[w*h*4];for(int i=0;i<data.Length;i+=4){data[i]=b;data[i+1]=g;data[i+2]=r;data[i+3]=a;}return new(w,h,data);}
    private static byte At(VideoFrame f,int x,int y,int channel)=>f.Pixels[(y*f.Width+x)*4+channel];
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Wait(Func<bool> test)
    {var clock=System.Diagnostics.Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<10){if(test())return;Thread.Sleep(30);}throw new Exception("Combined output check timed out.");}
}
