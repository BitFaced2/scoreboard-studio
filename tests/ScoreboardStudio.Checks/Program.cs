using System.Diagnostics;
using System.IO;
using ScoreboardStudio.Core;

int passed = 0;
var failures = new List<string>();
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
VideoFrame Solid(int w, int h, byte r, byte g, byte b, byte a = 255)
{
    var pixels = new byte[w * h * 4];
    for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = b; pixels[i+1] = g; pixels[i+2] = r; pixels[i+3] = a; }
    return new(w,h,pixels);
}
var whole = new Crop(0,0,1,1);
var plain = new Cleanup { RemoveSpeckles = false };

Check("Saturated red LEDs survive cleanup, dark background becomes transparent", () =>
{
    var frame = Solid(8,4,0,0,0);
    for (int i = 0; i < 4; i++) frame.Pixels[i*4+2] = 255;
    var result = FrameProcessor.Process(frame, whole, plain);
    Require(result.Pixels[3] == 255 && result.Pixels[19] == 0, "Foreground/background alpha was incorrect.");
    Require(result.Pixels[0] == 255 && result.Pixels[1] == 255, "White conversion failed.");
});
Check("Crop uses exact source pixels and clamps to image boundaries", () =>
{
    var f = Solid(10,10,0,0,0); f.Pixels[(3*10+2)*4+2] = 123;
    var cropped = FrameProcessor.Crop(f, new(.2,.3,.4,.5));
    Require(cropped.Width == 4 && cropped.Height == 5 && cropped.Pixels[2] == 123, "Crop coordinates were incorrect.");
    var edge = FrameProcessor.Crop(f, new(1,1,5,5));
    Require(edge.Width == 1 && edge.Height == 1, "Edge crop did not stay inside source.");
});
Check("White-highlight isolation rejects red glow and blue board without changing original pixels", () =>
{
    var frame = new VideoFrame(4,1,[0,20,255,255, 240,120,40,255, 245,250,255,255, 0,0,0,255]);
    var clean = FrameProcessor.Process(frame, whole, plain with { Isolation="White highlights", Threshold=170, Softness=40 });
    Require(clean.Pixels[3]==0 && clean.Pixels[7]==0 && clean.Pixels[11]==255, "Glow/board survived or digit disappeared.");
    Require(frame.Pixels[0]==0 && frame.Pixels[1]==20 && frame.Pixels[2]==255,"Cleanup modified its source.");
});
Check("Perspective identity and disabled correction preserve the image", () =>
{
    var frame=Solid(64,64,120,80,40);
    Require(ReferenceEquals(frame,PerspectiveTransform.Apply(frame,new())),"Disabled transform should bypass processing.");
    var same=PerspectiveTransform.Apply(frame,new() { Enabled=true, Width=64,Height=64 });
    Require(same.Pixels.SequenceEqual(frame.Pixels),"Identity correction changed pixels.");
});
Check("Projective correction maps known trapezoid corners and interior accurately", () =>
{
    var frame=Solid(128,128,0,0,0);
    for(int y=0;y<128;y++) for(int x=0;x<128;x++) { int i=(y*128+x)*4; frame.Pixels[i+2]=(byte)(x*2); frame.Pixels[i+1]=(byte)(y*2); }
    NormalPoint Project(double u,double v) => new((.7*u+.05*v+.1)/(.2*u+.1*v+1),(.05*u+.75*v+.1)/(.2*u+.1*v+1));
    var settings=new PerspectiveSettings { Enabled=true,Width=65,Height=65,TopLeft=Project(0,0),TopRight=Project(1,0),BottomRight=Project(1,1),BottomLeft=Project(0,1) };
    var corrected=PerspectiveTransform.Apply(frame,settings);
    foreach(var (x,y) in new[] {(0,0),(64,0),(64,64),(0,64),(32,32),(16,48)})
    {
        var expected=Project(x/64.0,y/64.0); int i=(y*65+x)*4;
        Require(Math.Abs(corrected.Pixels[i+2]-expected.X*254)<1.1 && Math.Abs(corrected.Pixels[i+1]-expected.Y*254)<1.1,$"Incorrect projective mapping at {x},{y}.");
    }
});
Check("Perspective resampling handles transparency without colored fringes", () =>
{
    var frame=Solid(64,64,255,0,0);
    for(int y=0;y<64;y++) for(int x=32;x<64;x++) { int i=(y*64+x)*4; frame.Pixels[i]=255;frame.Pixels[i+2]=0;frame.Pixels[i+3]=0; }
    var corrected=PerspectiveTransform.Apply(frame,new() { Enabled=true,Width=63,Height=64 });
    int center=(32*63+31)*4;
    Require(corrected.Pixels[center+2]==255 && corrected.Pixels[center]==0 && corrected.Pixels[center+3] is >=127 and <=128,"Transparent colors leaked into interpolated edge.");
});
Check("Invalid perspective corner order and dimensions are rejected", () =>
{
    Require(!new PerspectiveSettings { TopRight=new(0,1),BottomLeft=new(1,0) }.IsValid(out _),"Crossed corners accepted.");
    Require(!new PerspectiveSettings { TopLeft=new(double.NaN,0) }.IsValid(out _),"NaN corner accepted.");
    Require(!new PerspectiveSettings { Width=99999 }.IsValid(out _),"Oversized correction accepted.");
});
Check("Odd-width crops pad transparently without stretching", () =>
{
    var result = FrameProcessor.Process(Solid(3,2,255,255,255), whole, plain);
    Require(result.Width == 4 && result.Height == 2 && result.Pixels[11] == 255 && result.Pixels[15] == 0, "Even-width padding was incorrect.");
});
Check("Source alpha and original color are retained", () =>
{
    var result = FrameProcessor.Process(Solid(4,4,240,50,10,100), whole, plain with { Color = "Original", RemoveBackground = false });
    Require(result.Pixels[0] == 10 && result.Pixels[1] == 50 && result.Pixels[2] == 240 && result.Pixels[3] == 100, "Original RGBA changed.");
});
Check("Custom colors use straight alpha and brightness does not change the mask", () =>
{
    var result = FrameProcessor.Process(Solid(4,4,255,0,0), whole, plain with { Color = "Custom", CustomColor = "#4080C0", Brightness = .5 });
    Require(result.Pixels[0] == 96 && result.Pixels[1] == 64 && result.Pixels[2] == 32 && result.Pixels[3] == 255, "Color or alpha was incorrect.");
});
Check("Speckle removal preserves a digit block but removes an isolated point", () =>
{
    var f = Solid(12,12,0,0,0);
    void Light(int x,int y) { f.Pixels[(y*12+x)*4+2] = 255; }
    Light(1,1); for(int y=5;y<9;y++) for(int x=5;x<9;x++) Light(x,y);
    var result = FrameProcessor.Process(f, whole, new());
    Require(result.Pixels[(1*12+1)*4+3] == 0 && result.Pixels[(6*12+6)*4+3] == 255, "Speckle removal damaged foreground or retained noise.");
});
Check("Thickness grows the silhouette and smoothing keeps intermediate alpha", () =>
{
    var f = Solid(10,10,0,0,0); f.Pixels[(5*10+5)*4+2] = 255;
    var thick = FrameProcessor.Process(f, whole, plain with { Thickness = 1 });
    Require(thick.Pixels[(5*10+4)*4+3] == 255, "Dilation did not expand digit.");
    var original = FrameProcessor.Process(f, whole, plain with { Thickness = 1, Color = "Original" });
    Require(original.Pixels[(5*10+4)*4+2] == 255, "Original-color thickness introduced black edges.");
    var smooth = FrameProcessor.Process(f, whole, plain with { Smooth = true });
    Require(smooth.Pixels[(5*10+5)*4+3] is > 0 and < 255, "Smoothing did not create soft edges.");
});
Check("Detector proposes separated regions without hard-coded coordinates", () =>
{
    var f = Solid(640,360,0,0,0);
    foreach (var (left,top) in new[] {(35,55),(370,210)})
        for(int y=top;y<top+45;y++) for(int x=left;x<left+75;x++) f.Pixels[(y*640+x)*4+2] = 255;
    var regions = FieldDetector.Detect(f);
    Require(regions.Count == 2, $"Expected two groups, got {regions.Count}.");
    Require(FieldDetector.Detect(Solid(640,360,0,0,0)).Count == 0, "Blank frame yielded suggestions.");
});
Check("Detector separates all six synthetic test fields", () =>
{
    var regions = FieldDetector.Detect(SyntheticBoard.Render(10),90);
    Require(regions.Count == 6, $"Expected six test regions, got {regions.Count}.");
});
Check("Automatic detection ignores bright room surfaces and groups narrow digit fragments", () =>
{
    var f=Solid(640,360,40,40,40);
    void Block(int left,int top,int width,int height,byte r,byte g,byte b)
    {for(int y=top;y<top+height;y++)for(int x=left;x<left+width;x++){int p=(y*640+x)*4;f.Pixels[p]=b;f.Pixels[p+1]=g;f.Pixels[p+2]=r;}}
    Block(0,0,150,360,230,230,230);
    Block(220,100,5,45,255,170,30);Block(249,100,45,45,255,170,30);
    Block(400,100,70,45,255,170,30);
    var detected=FieldDetector.DetectAutomatic(f);
    Require(detected.Isolation=="Bright colors" && detected.Regions.Count==2,"Room highlights or split digits corrupted automatic fields.");
    Require(detected.Regions.All(r=>r.X>150/640.0),"Bright wall was accepted as a field.");
    Require(FieldDetector.DetectAutomatic(Solid(320,180,0,0,0)).Regions.Count==0,"Blank frame created automatic fields.");
});
Check("Setup round trip preserves fields and rejects malformed or future layouts", () =>
{
    string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json");
    try
    {
        var fields = SyntheticBoard.Fields(); fields[0].Cleanup = new() { Color="Custom", CustomColor="#123456", Thickness=2 };
        fields[0].Cleanup.Isolation="White highlights";
        new Layout { Name="Arena", SourceName="Camera A", ImagePath="scoreboard.png", Perspective=new() { Enabled=true,TopLeft=new(.1,.1) }, Fields=fields }.Save(path);
        var read = Layout.Load(path);
        Require(read.Fields.Count==6 && read.SourceName=="Camera A" && read.Fields[0].Cleanup.CustomColor=="#123456" && read.Fields[0].Id==fields[0].Id, "Settings did not round-trip.");
        Require(read.Perspective.Enabled && read.Perspective.TopLeft.X==.1 && read.ImagePath=="scoreboard.png" && read.Fields[0].Cleanup.Isolation=="White highlights", "Perspective/image/isolation settings did not round-trip.");
        read.ImagePath=null;read.VideoPath="../footage/game.mp4";read.Save(path);
        Require(Layout.Load(path).VideoPath=="../footage/game.mp4","Video path did not round-trip.");
        read.ImagePath="scoreboard.png";read.Save(path);
        bool bothRejected=false;try {Layout.Load(path);}catch(InvalidDataException){bothRejected=true;}
        Require(bothRejected,"Ambiguous image and video source was accepted.");
        File.WriteAllText(path,"{\"Fields\":[]}");
        var old = Layout.Load(path);
        Require(!old.Perspective.Enabled,"Old layouts unexpectedly enabled perspective.");
        File.WriteAllText(path,"{\"Version\":99,\"Fields\":[]}");
        bool rejected=false; try { Layout.Load(path); } catch(InvalidDataException) { rejected=true; }
        Require(rejected,"Future version was silently accepted.");
        File.WriteAllText(path,"{\"Fields\":[null]}");
        rejected=false; try { Layout.Load(path); } catch(InvalidDataException) { rejected=true; }
        Require(rejected,"Null field was accepted.");
    }
    finally { File.Delete(path); File.Delete(path+".tmp"); }
});
Check("Video engine waits without generating fields or outputs until a source is provided", () =>
{
    using var engine = new VideoEngine();
    engine.Configure(new("None",null,null,[],false,1));
    Thread.Sleep(120);
    Require(engine.Preview.Source is null && !engine.Preview.Healthy && engine.Preview.Fields.Count==0 && engine.Preview.OutputCount==0,"Empty startup generated video or outputs.");
    Require(engine.Preview.Status=="Choose a source","Empty startup did not prompt for a source.");
});
Check("Video engine processes a still source with no NDI dependency", () =>
{
    using var engine = new VideoEngine();
    engine.Configure(new("Image",null,SyntheticBoard.Render(10),SyntheticBoard.Fields().ToArray(),false,1));
    var clock=Stopwatch.StartNew();
    while(engine.Preview.Fields.Count!=6 && clock.Elapsed.TotalSeconds<5) Thread.Sleep(50);
    Require(engine.Preview.Healthy && engine.Preview.Fields.Count==6,"Still-image pipeline did not start.");
    Console.WriteLine($"  Six-field processing: {engine.Preview.ProcessingMs:0.0} ms");
});
Check("Video engine applies perspective before field cropping and keeps raw preview", () =>
{
    using var engine = new VideoEngine();
    var raw=Solid(200,100,255,255,255);
    var field=new ScoreField { Region=new(0,0,.5,.5) };
    engine.Configure(new("Image",null,raw,[field],false,1,new() { Enabled=true,Width=100,Height=50 }));
    var clock=Stopwatch.StartNew();
    while(engine.Preview.Fields.Count!=1 && clock.Elapsed.TotalSeconds<5) Thread.Sleep(25);
    Require(engine.Preview.RawSource?.Width==200 && engine.Preview.Source?.Width==100,"Raw/corrected sources were not kept separate.");
    Require(engine.Preview.Fields[field.Id] is { Width:50,Height:25 },"Crop happened before perspective correction.");
});

Check("Stabilization corrects camera motion, rejects lost tracking, and reacquires the original reference", StabilizationChecks.MotionAndRecovery);
Check("Stabilization keeps live pixels on tracking loss, preserves crops, and clears only actual source loss", ()=>StabilizationChecks.EngineKeepsFields());
int updaterIndex=Array.IndexOf(args,"--update-exe");
UpdateChecks.Register(Check,updaterIndex>=0&&updaterIndex+1<args.Length?args[updaterIndex+1]:null);

if(args.Contains("--ndi"))
{
    Check("NDI stays connected and carries changing pixels through tracking loss and recovery", ()=>StabilizationChecks.EngineKeepsFields(true));
    Check("NDI discovery, video receive, alpha output, signal loss, and recovery", () =>
    {
        Require(NdiRuntime.Initialize(),NdiRuntime.Status);
        Console.WriteLine("  Runtime: " + NdiRuntime.LibraryPath);
        string name = "Scoreboard check " + Guid.NewGuid().ToString("N")[..8];
        using var sender = new NdiSender(name);
        using var finder = new NdiDiscovery();
        var frame = Solid(320,80,255,0,0);
        for(int y=0;y<80;y++) for(int x=0;x<80;x++) frame.Pixels[(y*320+x)*4+3]=0;
        NdiSource? found=null;
        var clock=Stopwatch.StartNew();
        while(clock.Elapsed.TotalSeconds<12 && found is null)
        { sender.Send(frame); found=finder.GetSources().FirstOrDefault(s=>s.Name.Contains(name)); Thread.Sleep(35); }
        Require(found is not null,"NDI source not discovered within 12 seconds.");
        using var receiver = new NdiReceiver(found!);
        VideoFrame? received=null; clock.Restart();
        while(clock.Elapsed.TotalSeconds<8 && received is null) { sender.Send(frame); received=receiver.Capture(50); }
        Require(received is { Width:320,Height:80 },"NDI dimensions did not round-trip.");
        Require(received!.Pixels[(40*320+30)*4+3] < 10 && received.Pixels[(40*320+200)*4+3] > 240,"NDI alpha did not round-trip.");
        using var engine = new VideoEngine();
        var field = new ScoreField { Name="Integration " + name, Region=whole, Cleanup=plain };
        engine.Configure(new("NDI",found,null,[field],true,1));
        clock.Restart();
        while(clock.Elapsed.TotalSeconds<10 && engine.Preview.OutputCount!=1) { sender.Send(frame); Thread.Sleep(33); }
        Require(engine.Preview.Healthy && engine.Preview.OutputCount==1,"NDI pipeline failed to send cleaned field.");
        NdiSource? outputSource=null; clock.Restart();
        while(clock.Elapsed.TotalSeconds<10 && outputSource is null)
        { sender.Send(frame); outputSource=finder.GetSources().FirstOrDefault(s=>s.Name.Contains("Scoreboard - Integration "+name)); Thread.Sleep(33); }
        Require(outputSource is not null,"Cleaned field output was not discoverable.");
        using var outputReceiver = new NdiReceiver(outputSource!);
        VideoFrame? cleaned=null; clock.Restart();
        while(clock.Elapsed.TotalSeconds<8 && cleaned is null) { sender.Send(frame); cleaned=outputReceiver.Capture(50); }
        Require(cleaned is { Width:320,Height:80 } && cleaned.Pixels[(40*320+200)*4]>230,"Cleaned NDI output was not white.");
        clock.Restart();
        while(clock.Elapsed.TotalSeconds<6 && engine.Preview.Healthy) Thread.Sleep(100);
        Require(!engine.Preview.Healthy && engine.Preview.Status.Contains("NO SIGNAL"),"Missing source was not flagged.");
        Require(engine.Preview.Fields[field.Id].Pixels.All(p=>p==0),"Lost source left stale digits in output.");
        bool transparent=false; clock.Restart();
        while(clock.Elapsed.TotalSeconds<6 && !transparent)
        { var blank=outputReceiver.Capture(100); transparent=blank is not null && blank.Pixels.Where((_,i)=>i%4==3).All(a=>a<10); }
        Require(transparent,"Downstream NDI receiver retained stale digits after source loss.");
        clock.Restart();
        while(clock.Elapsed.TotalSeconds<8 && !engine.Preview.Healthy) { sender.Send(frame); Thread.Sleep(33); }
        Require(engine.Preview.Healthy,"Source did not recover automatically.");
    });
}
int imageIndex = Array.IndexOf(args, "--image");
if (imageIndex >= 0 && args.Length > imageIndex + 3)
    Check("Real scoreboard processing", () => ImageChecks.Inspect(args[imageIndex+1], args[imageIndex+2], args[imageIndex+3]));
Console.WriteLine($"\n{passed} passed, {failures.Count} failed.");
foreach(var failure in failures) Console.Error.WriteLine(failure);
return failures.Count==0?0:1;
