namespace MiReproductorMAUI;

public interface IAudioPlayerService
{
    event Action? PlaybackCompleted;
    Task PlayAsync(string filePath);
    void Pause();
    void Resume();
    void SeekTo(int positionMilliseconds);
    int DurationMilliseconds { get; }
    int PositionMilliseconds { get; }
    bool IsPlaying { get; }
    void Stop();
}
