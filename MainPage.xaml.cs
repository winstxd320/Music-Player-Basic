namespace MiReproductorMAUI;

public partial class MainPage : ContentPage
{
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IFolderPicker _folderPicker;
    private readonly FavoritesStorageService _favoritesService;
    private readonly IDispatcherTimer _progressTimer;

    private List<MusicTrack> _folderTracks = new();
    private List<MusicTrack> _favoriteTracks = new();
    private List<MusicTrack> _currentPlaylist = new();

    private int _currentIndex = -1;
    private bool _isFavoritesTabActive = false;
    private bool _isDraggingSlider = false;
    private bool _isInitialized = false;

    public MainPage(
        IAudioPlayerService audioPlayer,
        IFolderPicker folderPicker,
        FavoritesStorageService favoritesService)
    {
        InitializeComponent();

        _audioPlayer = audioPlayer;
        _folderPicker = folderPicker;
        _favoritesService = favoritesService;

        _audioPlayer.PlaybackCompleted += OnPlaybackCompleted;

        _progressTimer = Dispatcher.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromMilliseconds(500);
        _progressTimer.Tick += OnProgressTimerTick;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_isInitialized)
        {
            _isInitialized = true;
            await LoadPersistedDataAsync();
        }
    }

    private async Task LoadPersistedDataAsync()
    {
        _favoriteTracks = await _favoritesService.LoadValidFavoritesAsync();
        _folderTracks = await _folderPicker.LoadSavedFolderTracksAsync();
        SyncFavoriteFlags();
        SwitchTab(showFavorites: false);
    }

    private void SyncFavoriteFlags()
    {
        var favoriteUris = new HashSet<string>(_favoriteTracks.Select(f => f.UriString));
        foreach (var track in _folderTracks)
        {
            track.IsFavorite = favoriteUris.Contains(track.UriString);
        }
    }

    private void SwitchTab(bool showFavorites)
    {
        _isFavoritesTabActive = showFavorites;

        TabSongs.BackgroundColor = showFavorites ? Colors.Transparent : Color.FromArgb("#2A2A2A");
        LblTabSongs.TextColor = showFavorites ? Color.FromArgb("#9E9E9E") : Colors.White;
        IndicatorSongs.IsVisible = !showFavorites;

        TabFavorites.BackgroundColor = showFavorites ? Color.FromArgb("#2A2A2A") : Colors.Transparent;
        LblTabFavorites.TextColor = showFavorites ? Colors.White : Color.FromArgb("#9E9E9E");
        IndicatorFavorites.IsVisible = showFavorites;

        BtnSelectFolder.IsVisible = !showFavorites;
        LblListHeader.Text = showFavorites ? "MIS FAVORITOS" : "LISTA DE REPRODUCCIÓN";

        _currentPlaylist = showFavorites ? _favoriteTracks : _folderTracks;
        RefreshPlaylistView();
    }

    private void RefreshPlaylistView()
    {
        TracksCollectionView.ItemsSource = null;
        TracksCollectionView.ItemsSource = _currentPlaylist;
        UpdateTrackCounter();
    }

    private void OnTabSongsClicked(object? sender, TappedEventArgs e) => SwitchTab(false);
    private void OnTabFavoritesClicked(object? sender, TappedEventArgs e) => SwitchTab(true);

    private async void OnSelectFolderClicked(object? sender, EventArgs e)
    {
        try
        {
            var tracks = await _folderPicker.PickFolderAndGetTracksAsync();
            if (tracks.Count > 0)
            {
                _folderTracks = tracks;
                SyncFavoriteFlags();

                if (!_isFavoritesTabActive)
                {
                    _currentPlaylist = _folderTracks;
                    RefreshPlaylistView();
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo abrir la carpeta: {ex.Message}", "OK");
        }
    }

    private async void OnToggleFavoriteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not MusicTrack track)
            return;

        track.IsFavorite = !track.IsFavorite;

        if (track.IsFavorite)
        {
            if (!_favoriteTracks.Any(f => f.UriString == track.UriString))
            {
                _favoriteTracks.Add(new MusicTrack
                {
                    Title = track.Title,
                    UriString = track.UriString,
                    IsFavorite = true
                });
            }
        }
        else
        {
            _favoriteTracks.RemoveAll(f => f.UriString == track.UriString);

            var folderTrack = _folderTracks.FirstOrDefault(t => t.UriString == track.UriString);
            if (folderTrack != null)
            {
                folderTrack.IsFavorite = false;
            }
        }

        await _favoritesService.SaveFavoritesAsync(_favoriteTracks);

        if (_isFavoritesTabActive)
        {
            RefreshPlaylistView();
        }
    }

    private async void OnTrackSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MusicTrack selectedTrack)
            return;

        var index = _currentPlaylist.IndexOf(selectedTrack);
        if (index >= 0)
        {
            await PlayTrackAtIndexAsync(index);
        }
    }

    private async Task PlayTrackAtIndexAsync(int index)
    {
        if (index < 0 || index >= _currentPlaylist.Count)
            return;

        var track = _currentPlaylist[index];

        if (!_folderPicker.FileExists(track.UriString))
        {
            await DisplayAlert("Archivo no disponible", $"La canción \"{track.Title}\" fue eliminada del dispositivo.", "OK");

            _folderTracks.RemoveAll(t => t.UriString == track.UriString);
            _favoriteTracks.RemoveAll(t => t.UriString == track.UriString);
            await _favoritesService.SaveFavoritesAsync(_favoriteTracks);

            RefreshPlaylistView();
            return;
        }

        _currentIndex = index;

        try
        {
            await _audioPlayer.PlayAsync(track.UriString);

            // Si mientras cargaba el usuario ya cambió a otra canción, no sobrescribir la UI
            if (_currentIndex != index) return;

            LblCurrentTrack.Text = track.Title;
            UpdateTrackCounter();

            var totalMs = Math.Max(1, _audioPlayer.DurationMilliseconds);
            ProgressSlider.Maximum = totalMs;
            ProgressSlider.Value = 0;
            LblDuration.Text = FormatTime(totalMs);
            LblElapsed.Text = "0:00";

            BtnPlayPause.Text = "⏸";
            LblStatus.Text = "Reproduciendo";
            _progressTimer.Start();
        }
        catch (OperationCanceledException)
        {
            // Ignorar cancelación por cambio rápido de canción
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error de reproducción", ex.Message, "OK");
        }
    }

    private async void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        if (_audioPlayer.IsPlaying)
        {
            _audioPlayer.Pause();
            _progressTimer.Stop();
            BtnPlayPause.Text = "▶";
            LblStatus.Text = "En pausa";
        }
        else if (_currentIndex >= 0 && _currentIndex < _currentPlaylist.Count)
        {
            _audioPlayer.Resume();
            _progressTimer.Start();
            BtnPlayPause.Text = "⏸";
            LblStatus.Text = "Reproduciendo";
        }
        else if (_currentPlaylist.Count > 0)
        {
            await PlayTrackAtIndexAsync(0);
        }
    }

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        if (_currentPlaylist.Count == 0) return;

        int nextIndex = (_currentIndex + 1) % _currentPlaylist.Count;
        TracksCollectionView.SelectedItem = _currentPlaylist[nextIndex];
        await PlayTrackAtIndexAsync(nextIndex);
    }

    private async void OnPreviousClicked(object? sender, EventArgs e)
    {
        if (_currentPlaylist.Count == 0) return;

        int prevIndex = (_currentIndex - 1 + _currentPlaylist.Count) % _currentPlaylist.Count;
        TracksCollectionView.SelectedItem = _currentPlaylist[prevIndex];
        await PlayTrackAtIndexAsync(prevIndex);
    }

    private async void OnPlaybackCompleted()
    {
        _progressTimer.Stop();
        if (_currentPlaylist.Count > 0)
        {
            int nextIndex = (_currentIndex + 1) % _currentPlaylist.Count;
            TracksCollectionView.SelectedItem = _currentPlaylist[nextIndex];
            await PlayTrackAtIndexAsync(nextIndex);
        }
        else
        {
            BtnPlayPause.Text = "▶";
            LblStatus.Text = "Detenido";
        }
    }

    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        if (_isDraggingSlider || !_audioPlayer.IsPlaying) return;

        var pos = _audioPlayer.PositionMilliseconds;
        ProgressSlider.Value = pos;
        LblElapsed.Text = FormatTime(pos);
    }

    private void OnSliderDragStarted(object? sender, EventArgs e) => _isDraggingSlider = true;

    private void OnSliderDragCompleted(object? sender, EventArgs e)
    {
        _audioPlayer.SeekTo((int)ProgressSlider.Value);
        LblElapsed.Text = FormatTime((int)ProgressSlider.Value);
        _isDraggingSlider = false;
    }

    private void UpdateTrackCounter()
    {
        int total = _currentPlaylist.Count;
        int current = (_currentIndex >= 0 && _currentIndex < total) ? _currentIndex + 1 : 0;
        LblTrackCounter.Text = $"Pista {current} de {total}";
    }

    private static string FormatTime(int milliseconds)
    {
        var ts = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }
}