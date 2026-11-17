using Android.Media;

namespace MiReproductorMAUI.Platforms.Android;

public sealed class AudioPlayerService : IAudioPlayerService
{
    private MediaPlayer? _player;
    private TaskCompletionSource<bool>? _prepareCompletion;

    public event Action? PlaybackCompleted;

    public int DurationMilliseconds
    {
        get
        {
            try { return _player?.Duration ?? 0; }
            catch { return 0; }
        }
    }

    public int PositionMilliseconds
    {
        get
        {
            try { return _player?.CurrentPosition ?? 0; }
            catch { return 0; }
        }
    }

    public bool IsPlaying
    {
        get
        {
            try { return _player?.IsPlaying ?? false; }
            catch { return false; }
        }
    }

    public async Task PlayAsync(string filePath)
    {
        // Detener cualquier reproducción o preparación previa sin lanzar error
        Stop();

        var player = new MediaPlayer();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _player = player;
        _prepareCompletion = tcs;

        player.Prepared += (_, _) =>
        {
            try
            {
                if (_player == player)
                {
                    player.Start();
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetCanceled();
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        };

        player.Error += (_, args) =>
        {
            tcs.TrySetException(new Exception($"Error de MediaPlayer ({args.What}, {args.Extra})"));
            args.Handled = true;
        };

        player.Completion += (_, _) =>
        {
            MainThread.BeginInvokeOnMainThread(() => PlaybackCompleted?.Invoke());
        };

        try
        {
            if (filePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = global::Android.Net.Uri.Parse(filePath)!;
                player.SetDataSource(Platform.AppContext, uri);
            }
            else
            {
                player.SetDataSource(filePath);
            }

            player.PrepareAsync();
            await tcs.Task;
        }
        catch (TaskCanceledException)
        {
            // Se canceló porque el usuario eligió otra canción rápidamente; no es un error
        }
        catch
        {
            if (_player == player)
                Stop();
            throw;
        }
    }

    public void Pause()
    {
        if (IsPlaying)
            _player?.Pause();
    }

    public void Resume()
    {
        if (_player is not null && !IsPlaying)
            _player.Start();
    }

    public void SeekTo(int positionMilliseconds)
    {
        if (_player is null) return;

        var boundedPosition = Math.Clamp(positionMilliseconds, 0, Math.Max(0, DurationMilliseconds));
        _player.SeekTo(boundedPosition);
    }

    public void Stop()
    {
        var oldTcs = _prepareCompletion;
        _prepareCompletion = null;
        oldTcs?.TrySetCanceled();

        var oldPlayer = _player;
        _player = null;

        if (oldPlayer is not null)
        {
            try
            {
                if (oldPlayer.IsPlaying)
                    oldPlayer.Stop();
            }
            catch { }

            try
            {
                oldPlayer.Release();
                oldPlayer.Dispose();
            }
            catch { }
        }
    }
}