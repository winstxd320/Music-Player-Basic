namespace MiReproductorMAUI;

public interface IAudioPlayerService
{
    event Action? PlaybackCompleted;
    event Action? NextTrackRequested;
    event Action? PreviousTrackRequested;
    event Action? PlaybackStateChanged;

    Task PlayAsync(string filePath, string title = "", string subtitle = "");
    byte[]? GetAlbumArtBytes(string filePath);
    void Pause();
    void Resume();
    void SeekTo(int positionMilliseconds);
    int DurationMilliseconds { get; }
    int PositionMilliseconds { get; }
    bool IsPlaying { get; }
    void Stop();
}
