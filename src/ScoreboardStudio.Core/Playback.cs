namespace ScoreboardStudio.Core;

public sealed record PlaybackSnapshot(VideoFrame? Frame, string Status, bool Healthy);

public interface IPlaybackSource
{
    PlaybackSnapshot Snapshot { get; }
}
