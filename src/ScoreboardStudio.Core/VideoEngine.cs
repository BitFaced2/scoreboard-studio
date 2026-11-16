using System.Diagnostics;

namespace ScoreboardStudio.Core;

public sealed record EnginePlan(string Kind, NdiSource? Source, VideoFrame? Still, ScoreField[] Fields, bool Output, int Generation, PerspectiveSettings? Perspective = null, IPlaybackSource? Playback = null);
public sealed record EnginePreview(VideoFrame? Source, IReadOnlyDictionary<Guid, VideoFrame> Fields, string Status, bool Healthy, int OutputCount, double ProcessingMs, VideoFrame? RawSource = null, int Generation = -1);

// All receivers and senders belong to this worker; the UI only exchanges immutable snapshots.
public sealed class VideoEngine : IDisposable
{
    private EnginePlan plan = new("None", null, null, [], false, 0);
    private EnginePreview preview = new(null, new Dictionary<Guid, VideoFrame>(), "Choose a source", false, 0, 0);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    public EnginePreview Preview => Volatile.Read(ref preview);
    public VideoEngine() => worker = Task.Run(Run);
    public void Configure(EnginePlan value) => Volatile.Write(ref plan, value);
    private async Task Run()
    {
        NdiReceiver? receiver = null;
        var senders = new Dictionary<Guid, NdiSender>();
        var clock = Stopwatch.StartNew();
        int generation = -1;
        VideoFrame? latest = null;
        double lastReceived = -10, retryAt = 0;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                double start = clock.Elapsed.TotalMilliseconds;
                var current = Volatile.Read(ref plan);
                try
                {
                    if (current.Generation != generation)
                    {
                        receiver?.Dispose(); receiver = null;
                        foreach (var sender in senders.Values)
                        { try { sender.Send(new(2, 2, new byte[16])); } catch { } sender.Dispose(); }
                        senders.Clear();
                        generation = current.Generation; latest = null; lastReceived = -10; retryAt = 0;
                    }
                    bool healthy = false;
                    string status = "Choose a source";
                    switch (current.Kind)
                    {
                        case "Image": latest = current.Still; healthy = latest is not null; status = "STILL IMAGE • Setup only"; break;
                        case "Video":
                            var playback = current.Playback?.Snapshot;
                            latest = playback?.Frame;
                            healthy = playback?.Healthy ?? false;
                            status = playback?.Status ?? "Opening video";
                            break;
                        case "NDI":
                            if (receiver is null && clock.Elapsed.TotalSeconds >= retryAt)
                            { receiver = new NdiReceiver(current.Source!); retryAt = clock.Elapsed.TotalSeconds + 3; }
                            var frame = receiver?.Capture(20);
                            if (frame is not null) { latest = frame; lastReceived = clock.Elapsed.TotalSeconds; }
                            healthy = latest is not null && clock.Elapsed.TotalSeconds - lastReceived < 1.5;
                            status = healthy ? "LIVE • " + current.Source!.Name : "NO SIGNAL • Outputs are transparent; waiting for source";
                            break;
                    }
                    var working = latest is not null && current.Perspective is { Enabled: true } correction
                        ? PerspectiveTransform.Apply(latest, correction) : latest;
                    var fields = new Dictionary<Guid, VideoFrame>();
                    foreach (var field in current.Fields)
                    {
                        if (working is null) continue;
                        var processed = FrameProcessor.Process(working, field.Region, field.Cleanup);
                        if (!healthy) Array.Clear(processed.Pixels);
                        fields[field.Id] = processed;
                    }
                    var wanted = current.Output && latest is not null
                        ? current.Fields.Where(f => f.Enabled && fields.ContainsKey(f.Id)).ToArray() : [];
                    foreach (var id in senders.Keys.ToArray())
                    {
                        var field = wanted.FirstOrDefault(f => f.Id == id);
                        if (field is null || senders[id].Name != OutputName(current.Kind, field.Name))
                        {
                            // Clear the receiver's cached picture before stopping the sender.
                            senders[id].Send(new(2, 2, new byte[16])); senders[id].Dispose(); senders.Remove(id);
                        }
                    }
                    foreach (var field in wanted)
                    {
                        if (!senders.ContainsKey(field.Id)) senders[field.Id] = new NdiSender(OutputName(current.Kind, field.Name));
                        senders[field.Id].Send(fields[field.Id]);
                    }
                    Volatile.Write(ref preview, new(working, fields, status, healthy, senders.Count, clock.Elapsed.TotalMilliseconds - start, latest, generation));
                }
                catch (Exception ex)
                {
                    foreach (var sender in senders.Values)
                    { try { sender.Send(new(2, 2, new byte[16])); } catch { } sender.Dispose(); }
                    senders.Clear(); receiver?.Dispose(); receiver = null; latest = null;
                    retryAt = clock.Elapsed.TotalSeconds + 3;
                    Volatile.Write(ref preview, new(null, new Dictionary<Guid, VideoFrame>(), "ERROR • " + ex.Message, false, 0, 0, Generation:generation));
                    await Task.Delay(1000, cancellation.Token);
                }
                int delay = Math.Max(1, 33 - (int)(clock.Elapsed.TotalMilliseconds - start));
                await Task.Delay(delay, cancellation.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            receiver?.Dispose();
            foreach (var sender in senders.Values)
            { try { sender.Send(new(2, 2, new byte[16])); } catch { } sender.Dispose(); }
        }
    }
    public static string OutputName(string kind, string field) => $"Scoreboard{(kind == "Image" ? " STILL" : "")} - {Layout.CleanName(field)}";
    public void Dispose() { cancellation.Cancel(); worker.GetAwaiter().GetResult(); cancellation.Dispose(); }
}
