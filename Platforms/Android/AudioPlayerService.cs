using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using AudioAttributes = Android.Media.AudioAttributes;
using Uri = Android.Net.Uri;

namespace MiReproductorMAUI.Platforms.Android;

public sealed class AudioPlayerService : IAudioPlayerService
{
    public const string ActionPrevious = "com.reproductor.mireproductor.ACTION_PREVIOUS";
    public const string ActionPlayPause = "com.reproductor.mireproductor.ACTION_PLAY_PAUSE";
    public const string ActionNext = "com.reproductor.mireproductor.ACTION_NEXT";

    public const string ChannelId = "music_playback_channel";
    public const int NotificationId = 2001;

    private MediaPlayer? _player;
    private TaskCompletionSource<bool>? _prepareCompletion;
    private MediaSession? _mediaSession;
    private MediaActionReceiver? _actionReceiver;

    private string _currentTitle = "Music Basic Player";
    private string _currentSubtitle = "Reproduciendo música";
    private Bitmap? _currentAlbumArt;

    public event Action? PlaybackCompleted;
    public event Action? NextTrackRequested;
    public event Action? PreviousTrackRequested;
    public event Action? PlaybackStateChanged;

    public static AudioPlayerService? CurrentInstance { get; private set; }

    public AudioPlayerService()
    {
        CurrentInstance = this;
        InitializeMediaSessionAndReceiver();
    }

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

    private void InitializeMediaSessionAndReceiver()
    {
        var context = Platform.AppContext;
        CreateNotificationChannel(context);

        _mediaSession = new MediaSession(context, "MiReproductorMediaSession");
        _mediaSession.SetCallback(new MediaSessionCallback(this));
        _mediaSession.Active = true;

        _actionReceiver = new MediaActionReceiver(this);
        var filter = new IntentFilter();
        filter.AddAction(ActionPrevious);
        filter.AddAction(ActionPlayPause);
        filter.AddAction(ActionNext);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            context.RegisterReceiver(_actionReceiver, filter, ReceiverFlags.NotExported);
        }
        else
        {
            context.RegisterReceiver(_actionReceiver, filter);
        }
    }

    private static void CreateNotificationChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        if (manager == null)
            return;

        if (manager.GetNotificationChannel(ChannelId) == null)
        {
            var channel = new NotificationChannel(
                ChannelId,
                "Reproducción de música",
                NotificationImportance.Low)
            {
                Description = "Controles de reproducción de música en segundo plano"
            };
            channel.SetShowBadge(false);
            manager.CreateNotificationChannel(channel);
        }
    }

    public async Task PlayAsync(string filePath, string title = "", string subtitle = "")
    {
        StopPlayerOnly();

        _currentTitle = string.IsNullOrWhiteSpace(title) ? "Canción en reproducción" : title;
        _currentSubtitle = string.IsNullOrWhiteSpace(subtitle) ? "Music Basic Player" : subtitle;

        var player = new MediaPlayer();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _player = player;
        _prepareCompletion = tcs;

        var audioAttributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Music)!
            .Build()!;
        player.SetAudioAttributes(audioAttributes);
        player.SetWakeMode(Platform.AppContext, WakeLockFlags.Partial);

        player.Prepared += (_, _) =>
        {
            try
            {
                if (_player == player)
                {
                    player.Start();
                    UpdateMediaSessionAndNotification();
                    NotifyStateChanged();
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
                var uri = Uri.Parse(filePath)!;
                player.SetDataSource(Platform.AppContext, uri);
            }
            else
            {
                player.SetDataSource(filePath);
            }

            _currentAlbumArt = await Task.Run(() => ExtractAlbumArt(filePath));

            player.PrepareAsync();
            await tcs.Task;
        }
        catch (TaskCanceledException)
        {
            // Se canceló porque el usuario eligió otra canción rápidamente
        }
        catch
        {
            if (_player == player)
                StopPlayerOnly();
            throw;
        }
    }

    public void Pause()
    {
        if (IsPlaying)
        {
            _player?.Pause();
            UpdateMediaSessionAndNotification();
            NotifyStateChanged();
        }
    }

    public void Resume()
    {
        if (_player is not null && !IsPlaying)
        {
            _player.Start();
            UpdateMediaSessionAndNotification();
            NotifyStateChanged();
        }
    }

    public void SeekTo(int positionMilliseconds)
    {
        if (_player is null) return;

        var boundedPosition = Math.Clamp(positionMilliseconds, 0, Math.Max(0, DurationMilliseconds));
        _player.SeekTo(boundedPosition);
        UpdateMediaSessionAndNotification();
        NotifyStateChanged();
    }

    public void Stop()
    {
        StopPlayerOnly();
        StopForegroundService();
        NotifyStateChanged();
    }

    private void StopPlayerOnly()
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

    internal void TriggerNextTrack()
    {
        MainThread.BeginInvokeOnMainThread(() => NextTrackRequested?.Invoke());
    }

    internal void TriggerPreviousTrack()
    {
        MainThread.BeginInvokeOnMainThread(() => PreviousTrackRequested?.Invoke());
    }

    internal void TogglePlayPause()
    {
        if (IsPlaying)
            Pause();
        else
            Resume();
    }

    private void NotifyStateChanged()
    {
        MainThread.BeginInvokeOnMainThread(() => PlaybackStateChanged?.Invoke());
    }

    public byte[]? GetAlbumArtBytes(string filePath)
    {
        return ExtractAlbumArtBytes(filePath);
    }

    private static byte[]? ExtractAlbumArtBytes(string filePath)
    {
        try
        {
            using var retriever = new MediaMetadataRetriever();
            if (filePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = Uri.Parse(filePath);
                if (uri != null)
                {
                    retriever.SetDataSource(Platform.AppContext, uri);
                }
            }
            else
            {
                retriever.SetDataSource(filePath);
            }

            var artBytes = retriever.GetEmbeddedPicture();
            if (artBytes != null && artBytes.Length > 0)
            {
                return artBytes;
            }
        }
        catch
        {
            // Ignorar si el archivo no tiene carátula incrustada
        }

        return null;
    }

    private static Bitmap? ExtractAlbumArt(string filePath)
    {
        var artBytes = ExtractAlbumArtBytes(filePath);
        if (artBytes != null && artBytes.Length > 0)
        {
            var bmp = BitmapFactory.DecodeByteArray(artBytes, 0, artBytes.Length);
            if (bmp != null)
                return bmp;
        }

        try
        {
            return BitmapFactory.DecodeResource(Platform.AppContext.Resources, Resource.Drawable.appicon);
        }
        catch
        {
            return null;
        }
    }

    internal Notification BuildCurrentNotification()
    {
        var context = Platform.AppContext;
        CreateNotificationChannel(context);

        if (_mediaSession != null)
        {
            _mediaSession.Active = true;

            var metaBuilder = new MediaMetadata.Builder()
                .PutString(MediaMetadata.MetadataKeyTitle, _currentTitle)!
                .PutString(MediaMetadata.MetadataKeyArtist, _currentSubtitle)!
                .PutString(MediaMetadata.MetadataKeyAlbum, _currentSubtitle)!
                .PutLong(MediaMetadata.MetadataKeyDuration, Math.Max(0, DurationMilliseconds))!;

            if (_currentAlbumArt != null)
            {
                metaBuilder.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, _currentAlbumArt);
                metaBuilder.PutBitmap(MediaMetadata.MetadataKeyArt, _currentAlbumArt);
            }

            _mediaSession.SetMetadata(metaBuilder.Build());

            long actions = PlaybackState.ActionPlay |
                           PlaybackState.ActionPause |
                           PlaybackState.ActionPlayPause |
                           PlaybackState.ActionSkipToPrevious |
                           PlaybackState.ActionSkipToNext |
                           PlaybackState.ActionSeekTo;

            var stateCode = IsPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused;
            float speed = IsPlaying ? 1.0f : 0.0f;

            var stateBuilder = new PlaybackState.Builder()
                .SetActions(actions)!
                .SetState(stateCode, PositionMilliseconds, speed, SystemClock.ElapsedRealtime())!;

            _mediaSession.SetPlaybackState(stateBuilder.Build());
        }

        var pendingFlags = PendingIntentFlags.UpdateCurrent |
                           (OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.Immutable : 0);

        var openAppIntent = new Intent(context, typeof(MainActivity));
        openAppIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var contentPendingIntent = PendingIntent.GetActivity(context, 0, openAppIntent, pendingFlags);

        var prevPendingIntent = PendingIntent.GetBroadcast(
            context, 1, new Intent(ActionPrevious).SetPackage(context.PackageName), pendingFlags);
        var playPausePendingIntent = PendingIntent.GetBroadcast(
            context, 2, new Intent(ActionPlayPause).SetPackage(context.PackageName), pendingFlags);
        var nextPendingIntent = PendingIntent.GetBroadcast(
            context, 3, new Intent(ActionNext).SetPackage(context.PackageName), pendingFlags);

        Notification.Builder builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(context, ChannelId)
            : new Notification.Builder(context);

        var mediaStyle = new Notification.MediaStyle();
        if (_mediaSession != null)
        {
            mediaStyle.SetMediaSession(_mediaSession.SessionToken);
        }
        mediaStyle.SetShowActionsInCompactView(0, 1, 2);

        int playPauseIcon = IsPlaying
            ? global::Android.Resource.Drawable.IcMediaPause
            : global::Android.Resource.Drawable.IcMediaPlay;
        string playPauseTitle = IsPlaying ? "Pausar" : "Reproducir";

        var prevIcon = global::Android.Graphics.Drawables.Icon.CreateWithResource(context, global::Android.Resource.Drawable.IcMediaPrevious);
        var playPauseActionIcon = global::Android.Graphics.Drawables.Icon.CreateWithResource(context, playPauseIcon);
        var nextIcon = global::Android.Graphics.Drawables.Icon.CreateWithResource(context, global::Android.Resource.Drawable.IcMediaNext);

        builder.SetStyle(mediaStyle)
               .SetSmallIcon(Resource.Drawable.appicon)
               .SetContentTitle(_currentTitle)
               .SetContentText(_currentSubtitle)
               .SetContentIntent(contentPendingIntent)
               .SetVisibility(NotificationVisibility.Public)
               .SetOngoing(IsPlaying)
               .SetOnlyAlertOnce(true)
               .AddAction(new Notification.Action.Builder(prevIcon, "Anterior", prevPendingIntent).Build())
               .AddAction(new Notification.Action.Builder(playPauseActionIcon, playPauseTitle, playPausePendingIntent).Build())
               .AddAction(new Notification.Action.Builder(nextIcon, "Siguiente", nextPendingIntent).Build());

        if (_currentAlbumArt != null)
        {
            builder.SetLargeIcon(_currentAlbumArt);
        }

        return builder.Build();
    }

    private void UpdateMediaSessionAndNotification()
    {
        try
        {
            var context = Platform.AppContext;
            var notification = BuildCurrentNotification();

            var serviceIntent = new Intent(context, typeof(MediaPlaybackService));
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(serviceIntent);
            }
            else
            {
                context.StartService(serviceIntent);
            }

            var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
            manager?.Notify(NotificationId, notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayerService] Error actualizando notificación: {ex.Message}");
        }
    }

    private static void StopForegroundService()
    {
        try
        {
            var context = Platform.AppContext;
            var serviceIntent = new Intent(context, typeof(MediaPlaybackService));
            context.StopService(serviceIntent);
        }
        catch { }
    }

    private sealed class MediaSessionCallback : MediaSession.Callback
    {
        private readonly AudioPlayerService _service;

        public MediaSessionCallback(AudioPlayerService service)
        {
            _service = service;
        }

        public override void OnPlay() => _service.Resume();
        public override void OnPause() => _service.Pause();
        public override void OnStop() => _service.Stop();
        public override void OnSkipToNext() => _service.TriggerNextTrack();
        public override void OnSkipToPrevious() => _service.TriggerPreviousTrack();
        public override void OnSeekTo(long pos) => _service.SeekTo((int)pos);
    }

    private sealed class MediaActionReceiver : BroadcastReceiver
    {
        private readonly AudioPlayerService _service;

        public MediaActionReceiver(AudioPlayerService service)
        {
            _service = service;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            switch (intent?.Action)
            {
                case ActionPrevious:
                    _service.TriggerPreviousTrack();
                    break;
                case ActionPlayPause:
                    _service.TogglePlayPause();
                    break;
                case ActionNext:
                    _service.TriggerNextTrack();
                    break;
            }
        }
    }
}

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class MediaPlaybackService : Service
{
    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var playerService = AudioPlayerService.CurrentInstance;
        if (playerService != null)
        {
            var notification = playerService.BuildCurrentNotification();
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                StartForeground(AudioPlayerService.NotificationId, notification, ForegroundService.TypeMediaPlayback);
            }
            else
            {
                StartForeground(AudioPlayerService.NotificationId, notification);
            }
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
        {
            StopForeground(StopForegroundFlags.Remove);
        }
        else
        {
#pragma warning disable CA1422
            StopForeground(true);
#pragma warning restore CA1422
        }

        base.OnDestroy();
    }
}
