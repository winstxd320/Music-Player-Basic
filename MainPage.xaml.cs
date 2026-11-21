using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace MiReproductorMAUI;

public partial class MainPage : ContentPage
{
    private enum ActiveTab
    {
        Tracks = 0,
        Albums = 1,
        Favorites = 2
    }

    public static MainPage? CurrentInstance { get; private set; }

    private readonly IAudioPlayerService _audioPlayer;
    private readonly IFolderPicker _folderPicker;
    private readonly FavoritesStorageService _favoritesService;
    private readonly IDispatcherTimer _progressTimer;
    private readonly Random _random = new();

    // Controles enlazados por FindByName
    private readonly Grid? _tabAlbums;
    private readonly Label? _lblTabAlbums;
    private readonly BoxView? _indicatorAlbums;
    private readonly Label? _lblCurrentAlbum;
    private readonly View? _btnBackToAlbums;
    private readonly Label? _lblEmptyTracks;
    private readonly CollectionView? _albumsCollectionView;
    private readonly Grid? _libraryContentContainer;

    private readonly Entry? _searchEntry;
    private readonly View? _btnClearSearch;
    private readonly ProgressBar? _miniProgressBar;
    private readonly Image? _imgMiniCover;
    private readonly Label? _lblMiniTrackTitle;
    private readonly Label? _lblMiniAlbum;
    private readonly Path? _pathMiniPlay;
    private readonly Path? _pathMiniPause;

    private readonly Grid? _fullPlayerOverlay;
    private readonly Path? _pathFullFavorite;
    private readonly Label? _lblCoverArtTitle;
    private readonly Image? _imgFullCover;
    private readonly Path? _pathFullPlay;
    private readonly Path? _pathFullPause;
    private readonly Path? _pathShuffle;
    private readonly BoxView? _dotShuffleActive;
    private readonly Path? _pathRepeat;
    private readonly BoxView? _dotRepeatActive;

    private List<MusicTrack> _allTracks = new();
    private List<MusicAlbum> _albums = new();
    private List<MusicTrack> _favoriteTracks = new();
    private List<MusicTrack> _currentPlaylist = new();

    private MusicAlbum? _selectedAlbum;
    private MusicTrack? _currentPlayingTrack;
    private int _currentIndex = -1;
    private ActiveTab _activeTab = ActiveTab.Tracks;

    private string _searchQuery = string.Empty;
    private bool _isShuffleEnabled = false;
    private bool _isRepeatOneEnabled = false;
    private bool _isDraggingSlider = false;
    private bool _isInitialized = false;
    private bool _isAnimatingTabSwipe = false;

    public MainPage(
        IAudioPlayerService audioPlayer,
        IFolderPicker folderPicker,
        FavoritesStorageService favoritesService)
    {
        InitializeComponent();
        CurrentInstance = this;

        _tabAlbums = this.FindByName<Grid>("TabAlbums");
        _lblTabAlbums = this.FindByName<Label>("LblTabAlbums");
        _indicatorAlbums = this.FindByName<BoxView>("IndicatorAlbums");
        _lblCurrentAlbum = this.FindByName<Label>("LblCurrentAlbum");
        _btnBackToAlbums = this.FindByName<View>("BtnBackToAlbums");
        _lblEmptyTracks = this.FindByName<Label>("LblEmptyTracks");
        _albumsCollectionView = this.FindByName<CollectionView>("AlbumsCollectionView");
        _libraryContentContainer = this.FindByName<Grid>("LibraryContentContainer");

        _searchEntry = this.FindByName<Entry>("SearchEntry");
        _btnClearSearch = this.FindByName<View>("BtnClearSearch");
        _miniProgressBar = this.FindByName<ProgressBar>("MiniProgressBar");
        _imgMiniCover = this.FindByName<Image>("ImgMiniCover");
        _lblMiniTrackTitle = this.FindByName<Label>("LblMiniTrackTitle");
        _lblMiniAlbum = this.FindByName<Label>("LblMiniAlbum");
        _pathMiniPlay = this.FindByName<Path>("PathMiniPlay");
        _pathMiniPause = this.FindByName<Path>("PathMiniPause");

        _fullPlayerOverlay = this.FindByName<Grid>("FullPlayerOverlay");
        _pathFullFavorite = this.FindByName<Path>("PathFullFavorite");
        _lblCoverArtTitle = this.FindByName<Label>("LblCoverArtTitle");
        _imgFullCover = this.FindByName<Image>("ImgFullCover");
        _pathFullPlay = this.FindByName<Path>("PathFullPlay");
        _pathFullPause = this.FindByName<Path>("PathFullPause");
        _pathShuffle = this.FindByName<Path>("PathShuffle");
        _dotShuffleActive = this.FindByName<BoxView>("DotShuffleActive");
        _pathRepeat = this.FindByName<Path>("PathRepeat");
        _dotRepeatActive = this.FindByName<BoxView>("DotRepeatActive");

        _audioPlayer = audioPlayer;
        _folderPicker = folderPicker;
        _favoritesService = favoritesService;

        _audioPlayer.PlaybackCompleted += OnPlaybackCompleted;
        _audioPlayer.NextTrackRequested += () => OnNextClicked(this, EventArgs.Empty);
        _audioPlayer.PreviousTrackRequested += () => OnPreviousClicked(this, EventArgs.Empty);
        _audioPlayer.PlaybackStateChanged += OnExternalPlaybackStateChanged;

        _progressTimer = Dispatcher.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromMilliseconds(400);
        _progressTimer.Tick += OnProgressTimerTick;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_isInitialized)
        {
            _isInitialized = true;
            await LoadDeviceMusicAndFavoritesAsync();
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (_fullPlayerOverlay != null && _fullPlayerOverlay.IsVisible)
        {
            _ = MinimizeFullPlayerAsync();
            return true;
        }

        if (_activeTab == ActiveTab.Albums && _selectedAlbum != null)
        {
            _selectedAlbum = null;
            SwitchTab(ActiveTab.Albums, resetAlbumSelection: true);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    public bool CanStartHorizontalTabDrag(float touchStartY, int screenHeightPx)
    {
        if (_isAnimatingTabSwipe)
            return false;

        if (_fullPlayerOverlay != null && _fullPlayerOverlay.IsVisible)
            return false;

        if (screenHeightPx > 0 && touchStartY > screenHeightPx * 0.87f)
            return false;

        return true;
    }

    public void OnHorizontalDragUpdated(float deltaXDp)
    {
        if (_libraryContentContainer == null || _isAnimatingTabSwipe)
            return;

        // Resistencia suave en los bordes (primera y última pestaña)
        bool atLeftEdge = _activeTab == ActiveTab.Tracks && deltaXDp > 0;
        bool atRightEdge = _activeTab == ActiveTab.Favorites && deltaXDp < 0;

        double damping = (atLeftEdge || atRightEdge) ? 0.18 : 0.55;
        double clampedTranslation = Math.Clamp(deltaXDp * damping, -140, 140);

        _libraryContentContainer.TranslationX = clampedTranslation;
        _libraryContentContainer.Opacity = Math.Clamp(1.0 - (Math.Abs(clampedTranslation) / 360.0), 0.65, 1.0);
    }

    public async void OnHorizontalDragCompleted(float totalDeltaXDp)
    {
        if (_libraryContentContainer == null || _isAnimatingTabSwipe)
            return;

        const float swipeThresholdDp = 46f;
        bool swipedLeft = totalDeltaXDp < -swipeThresholdDp;
        bool swipedRight = totalDeltaXDp > swipeThresholdDp;

        // Si desliza a la derecha estando dentro de un álbum, regresa suavemente a la lista de álbumes
        if (swipedRight && _activeTab == ActiveTab.Albums && _selectedAlbum != null)
        {
            _selectedAlbum = null;
            await CompleteSmoothTabSwipeAsync(ActiveTab.Albums, swipedLeft: false, resetAlbumSelection: true);
            return;
        }

        int currentTabIndex = (int)_activeTab;
        int nextTabIndex = swipedLeft ? currentTabIndex + 1 : (swipedRight ? currentTabIndex - 1 : currentTabIndex);

        if (nextTabIndex != currentTabIndex && nextTabIndex >= 0 && nextTabIndex <= 2)
        {
            await CompleteSmoothTabSwipeAsync((ActiveTab)nextTabIndex, swipedLeft, resetAlbumSelection: true);
        }
        else
        {
            // Regresar suavemente a su lugar si el gesto fue muy corto o en un extremo
            _isAnimatingTabSwipe = true;
            await Task.WhenAll(
                _libraryContentContainer.TranslateToAsync(0, 0, 130, Easing.CubicOut),
                _libraryContentContainer.FadeToAsync(1.0, 130, Easing.CubicOut));
            _isAnimatingTabSwipe = false;
        }
    }

    private async Task CompleteSmoothTabSwipeAsync(ActiveTab targetTab, bool swipedLeft, bool resetAlbumSelection)
    {
        if (_libraryContentContainer == null)
        {
            SwitchTab(targetTab, resetAlbumSelection);
            return;
        }

        _isAnimatingTabSwipe = true;
        try
        {
            // Cambiar la pestaña y visibilidad de inmediato sin destruir celdas del CollectionView
            SwitchTab(targetTab, resetAlbumSelection);

            // Entrar suavemente desde el lado correspondiente
            _libraryContentContainer.TranslationX = swipedLeft ? 55 : -55;
            _libraryContentContainer.Opacity = 0.72;

            await Task.WhenAll(
                _libraryContentContainer.TranslateToAsync(0, 0, 165, Easing.CubicOut),
                _libraryContentContainer.FadeToAsync(1.0, 165, Easing.CubicOut));
        }
        finally
        {
            _libraryContentContainer.TranslationX = 0;
            _libraryContentContainer.Opacity = 1;
            _isAnimatingTabSwipe = false;
        }
    }

    private async Task LoadDeviceMusicAndFavoritesAsync()
    {
        LblStatus.Text = "Escaneando música...";

        _allTracks = await _folderPicker.LoadDeviceTracksAsync();
        _favoriteTracks = await _favoritesService.LoadValidFavoritesAsync();

        SyncFavoriteFlagsAndMetadata();
        BuildAlbumsFromTracks();

        LblStatus.Text = _audioPlayer.IsPlaying ? "Reproduciendo" : "Detenido";
        SwitchTab(_activeTab, resetAlbumSelection: false);
    }

    private void SyncFavoriteFlagsAndMetadata()
    {
        var tracksByUri = _allTracks
            .GroupBy(t => t.UriString)
            .ToDictionary(g => g.Key, g => g.First());

        var favoriteUris = new HashSet<string>(_favoriteTracks.Select(f => f.UriString));
        var playingUri = _currentPlayingTrack?.UriString;

        foreach (var track in _allTracks)
        {
            track.IsFavorite = favoriteUris.Contains(track.UriString);
            track.IsPlaying = playingUri != null && track.UriString == playingUri;
        }

        foreach (var fav in _favoriteTracks)
        {
            fav.IsFavorite = true;
            fav.IsPlaying = playingUri != null && fav.UriString == playingUri;
            if (tracksByUri.TryGetValue(fav.UriString, out var deviceTrack))
            {
                fav.AlbumName = deviceTrack.AlbumName;
                fav.FolderPath = deviceTrack.FolderPath;
            }
        }
    }

    private void BuildAlbumsFromTracks()
    {
        _albums = _allTracks
            .GroupBy(t => string.IsNullOrWhiteSpace(t.FolderPath) ? t.AlbumName : t.FolderPath,
                     StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var albumName = string.IsNullOrWhiteSpace(first.AlbumName) ? "música" : first.AlbumName;
                return new MusicAlbum
                {
                    Name = albumName,
                    FolderPath = first.FolderPath,
                    Tracks = group.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList()
                };
            })
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (_selectedAlbum != null)
        {
            _selectedAlbum = _albums.FirstOrDefault(a =>
                string.Equals(a.FolderPath, _selectedAlbum.FolderPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Name, _selectedAlbum.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void SwitchTab(ActiveTab tab, bool resetAlbumSelection = true)
    {
        _activeTab = tab;
        if (resetAlbumSelection && tab == ActiveTab.Albums)
        {
            _selectedAlbum = null;
        }

        SetTabVisualState(LblTabSongs, IndicatorSongs, tab == ActiveTab.Tracks);
        SetTabVisualState(_lblTabAlbums, _indicatorAlbums, tab == ActiveTab.Albums);
        SetTabVisualState(LblTabFavorites, IndicatorFavorites, tab == ActiveTab.Favorites);

        ApplyCurrentViewAndFilter();
    }

    private static void SetTabVisualState(Label? tabLabel, BoxView? indicator, bool isActive)
    {
        if (tabLabel != null)
            tabLabel.TextColor = isActive ? Color.FromArgb("#B366FF") : Color.FromArgb("#8E889C");
        if (indicator != null)
            indicator.IsVisible = isActive;
    }

    private void ApplyCurrentViewAndFilter()
    {
        string query = _searchQuery.Trim();
        bool hasQuery = !string.IsNullOrEmpty(query);

        if (_activeTab == ActiveTab.Tracks)
        {
            if (_btnBackToAlbums != null) _btnBackToAlbums.IsVisible = false;
            if (_albumsCollectionView != null) _albumsCollectionView.IsVisible = false;
            TracksCollectionView.IsVisible = true;

            var filtered = FilterTracks(_allTracks, query);
            LblListHeader.Text = $"{filtered.Count} {(filtered.Count == 1 ? "Pista" : "Pistas")}";
            if (_lblEmptyTracks != null)
            {
                _lblEmptyTracks.Text = hasQuery
                    ? $"No se encontraron pistas para \"{query}\"."
                    : "No se encontraron pistas de audio en el almacenamiento del celular.";
            }

            _currentPlaylist = filtered;
            RefreshPlaylistView();
        }
        else if (_activeTab == ActiveTab.Albums)
        {
            if (_selectedAlbum == null)
            {
                if (_btnBackToAlbums != null) _btnBackToAlbums.IsVisible = false;
                TracksCollectionView.IsVisible = false;

                var filteredAlbums = hasQuery
                    ? _albums.Where(a =>
                        a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        a.Tracks.Any(t => t.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                        .ToList()
                    : _albums;

                LblListHeader.Text = $"{filteredAlbums.Count} {(filteredAlbums.Count == 1 ? "Álbum" : "Álbumes")}";

                if (_albumsCollectionView != null)
                {
                    _albumsCollectionView.IsVisible = true;
                    if (!ReferenceEquals(_albumsCollectionView.ItemsSource, filteredAlbums))
                    {
                        _albumsCollectionView.ItemsSource = filteredAlbums;
                    }
                }
            }
            else
            {
                if (_btnBackToAlbums != null) _btnBackToAlbums.IsVisible = true;
                if (_albumsCollectionView != null) _albumsCollectionView.IsVisible = false;
                TracksCollectionView.IsVisible = true;

                var filtered = FilterTracks(_selectedAlbum.Tracks, query);
                LblListHeader.Text = $"{_selectedAlbum.Name} · {filtered.Count} {(filtered.Count == 1 ? "Pista" : "Pistas")}";
                if (_lblEmptyTracks != null)
                {
                    _lblEmptyTracks.Text = hasQuery
                        ? $"No hay pistas en este álbum que coincidan con \"{query}\"."
                        : "No hay pistas en este álbum.";
                }

                _currentPlaylist = filtered;
                RefreshPlaylistView();
            }
        }
        else if (_activeTab == ActiveTab.Favorites)
        {
            if (_btnBackToAlbums != null) _btnBackToAlbums.IsVisible = false;
            if (_albumsCollectionView != null) _albumsCollectionView.IsVisible = false;
            TracksCollectionView.IsVisible = true;

            var filtered = FilterTracks(_favoriteTracks, query);
            LblListHeader.Text = $"{filtered.Count} {(filtered.Count == 1 ? "Favorito" : "Favoritos")}";
            if (_lblEmptyTracks != null)
            {
                _lblEmptyTracks.Text = hasQuery
                    ? $"No hay favoritos que coincidan con \"{query}\"."
                    : "Aún no has agregado canciones a favoritos.";
            }

            _currentPlaylist = filtered;
            RefreshPlaylistView();
        }
    }

    private static List<MusicTrack> FilterTracks(List<MusicTrack> source, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return source;

        return source
            .Where(t =>
                t.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                t.AlbumName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    private void RefreshPlaylistView(bool forceReset = false)
    {
        if (forceReset)
        {
            TracksCollectionView.ItemsSource = null;
        }

        if (!ReferenceEquals(TracksCollectionView.ItemsSource, _currentPlaylist))
        {
            TracksCollectionView.ItemsSource = _currentPlaylist;
        }

        if (_currentPlayingTrack != null)
        {
            _currentIndex = _currentPlaylist.FindIndex(t => t.UriString == _currentPlayingTrack.UriString);
        }
        else
        {
            _currentIndex = -1;
        }

        UpdateTrackCounter();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchQuery = e.NewTextValue ?? string.Empty;
        if (_btnClearSearch != null)
            _btnClearSearch.IsVisible = !string.IsNullOrEmpty(_searchQuery);

        ApplyCurrentViewAndFilter();
    }

    private void OnClearSearchClicked(object? sender, EventArgs e)
    {
        _searchQuery = string.Empty;
        if (_searchEntry != null)
            _searchEntry.Text = string.Empty;
        if (_btnClearSearch != null)
            _btnClearSearch.IsVisible = false;

        ApplyCurrentViewAndFilter();
    }

    private void OnTabSongsClicked(object? sender, TappedEventArgs e) => SwitchTab(ActiveTab.Tracks);
    private void OnTabAlbumsClicked(object? sender, TappedEventArgs e) => SwitchTab(ActiveTab.Albums, resetAlbumSelection: true);
    private void OnTabFavoritesClicked(object? sender, TappedEventArgs e) => SwitchTab(ActiveTab.Favorites);

    private void OnBackToAlbumsClicked(object? sender, EventArgs e)
    {
        _selectedAlbum = null;
        SwitchTab(ActiveTab.Albums, resetAlbumSelection: true);
    }

    private async void OnRescanClicked(object? sender, EventArgs e)
    {
        await LoadDeviceMusicAndFavoritesAsync();
    }

    private void OnAlbumCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not MusicAlbum album)
            return;

        _selectedAlbum = album;
        SwitchTab(ActiveTab.Albums, resetAlbumSelection: false);
    }

    private async void OnTrackCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not MusicTrack selectedTrack)
            return;

        var index = _currentPlaylist.FindIndex(t => t.UriString == selectedTrack.UriString);
        if (index >= 0)
        {
            await PlayTrackAtIndexAsync(index, expandFullPlayer: true);
        }
    }

    private async void OnMiniPlayerTapped(object? sender, TappedEventArgs e)
    {
        if (_currentPlayingTrack == null && _currentPlaylist.Count > 0)
        {
            await PlayTrackAtIndexAsync(0, expandFullPlayer: true);
            return;
        }

        await ExpandFullPlayerAsync();
    }

    private async void OnMinimizePlayerClicked(object? sender, EventArgs e)
    {
        await MinimizeFullPlayerAsync();
    }

    private async Task ExpandFullPlayerAsync()
    {
        if (_fullPlayerOverlay == null || _fullPlayerOverlay.IsVisible)
            return;

        _fullPlayerOverlay.TranslationY = this.Height > 0 ? this.Height : 700;
        _fullPlayerOverlay.Opacity = 0;
        _fullPlayerOverlay.IsVisible = true;

        await Task.WhenAll(
            _fullPlayerOverlay.TranslateToAsync(0, 0, 260, Easing.CubicOut),
            _fullPlayerOverlay.FadeToAsync(1, 200, Easing.CubicOut));
    }

    private async Task MinimizeFullPlayerAsync()
    {
        if (_fullPlayerOverlay == null || !_fullPlayerOverlay.IsVisible)
            return;

        double targetY = this.Height > 0 ? this.Height : 700;
        await Task.WhenAll(
            _fullPlayerOverlay.TranslateToAsync(0, targetY, 240, Easing.CubicIn),
            _fullPlayerOverlay.FadeToAsync(0, 200, Easing.CubicIn));

        _fullPlayerOverlay.IsVisible = false;
        _fullPlayerOverlay.TranslationY = 0;
        _fullPlayerOverlay.Opacity = 1;
    }

    private async void OnToggleFavoriteTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not MusicTrack track)
            return;

        await ToggleFavoriteForTrackAsync(track);
    }

    private async void OnFullPlayerFavoriteClicked(object? sender, EventArgs e)
    {
        if (_currentPlayingTrack == null)
            return;

        await ToggleFavoriteForTrackAsync(_currentPlayingTrack);
    }

    private async Task ToggleFavoriteForTrackAsync(MusicTrack track)
    {
        bool newFavoriteState = !track.IsFavorite;
        track.IsFavorite = newFavoriteState;

        foreach (var deviceTrack in _allTracks.Where(t => t.UriString == track.UriString))
        {
            deviceTrack.IsFavorite = newFavoriteState;
        }

        if (_currentPlayingTrack != null && _currentPlayingTrack.UriString == track.UriString)
        {
            _currentPlayingTrack.IsFavorite = newFavoriteState;
            UpdateFullPlayerFavoriteButton(_currentPlayingTrack);
        }

        if (newFavoriteState)
        {
            if (!_favoriteTracks.Any(f => f.UriString == track.UriString))
            {
                _favoriteTracks.Add(new MusicTrack
                {
                    Title = track.Title,
                    UriString = track.UriString,
                    AlbumName = track.AlbumName,
                    FolderPath = track.FolderPath,
                    IsFavorite = true,
                    IsPlaying = _currentPlayingTrack?.UriString == track.UriString
                });
            }
        }
        else
        {
            _favoriteTracks.RemoveAll(f => f.UriString == track.UriString);
        }

        await _favoritesService.SaveFavoritesAsync(_favoriteTracks);

        if (_activeTab == ActiveTab.Favorites)
        {
            // Al agregar o quitar un elemento de la lista en la pestaña Favoritos, refrescar el listado
            RefreshPlaylistView(forceReset: true);
            LblListHeader.Text = $"{_currentPlaylist.Count} {(_currentPlaylist.Count == 1 ? "Favorito" : "Favoritos")}";
        }
    }

    private void UpdateFullPlayerFavoriteButton(MusicTrack track)
    {
        if (_pathFullFavorite == null)
            return;

        _pathFullFavorite.Fill = new SolidColorBrush(track.FavoriteFillColor);
        _pathFullFavorite.Stroke = new SolidColorBrush(track.FavoriteStrokeColor);
    }

    private void OnShuffleClicked(object? sender, EventArgs e)
    {
        _isShuffleEnabled = !_isShuffleEnabled;
        if (_pathShuffle != null)
        {
            _pathShuffle.Stroke = new SolidColorBrush(
                _isShuffleEnabled ? Color.FromArgb("#C084FC") : Color.FromArgb("#7A6C94"));
        }
        if (_dotShuffleActive != null)
        {
            _dotShuffleActive.IsVisible = _isShuffleEnabled;
        }
    }

    private void OnRepeatClicked(object? sender, EventArgs e)
    {
        _isRepeatOneEnabled = !_isRepeatOneEnabled;
        if (_pathRepeat != null)
        {
            _pathRepeat.Stroke = new SolidColorBrush(
                _isRepeatOneEnabled ? Color.FromArgb("#C084FC") : Color.FromArgb("#7A6C94"));
        }
        if (_dotRepeatActive != null)
        {
            _dotRepeatActive.IsVisible = _isRepeatOneEnabled;
        }
    }

    private async Task PlayTrackAtIndexAsync(int index, bool expandFullPlayer = false)
    {
        if (index < 0 || index >= _currentPlaylist.Count)
            return;

        var track = _currentPlaylist[index];

        if (!_folderPicker.FileExists(track.UriString))
        {
            await DisplayAlertAsync("Archivo no disponible", $"La canción \"{track.Title}\" ya no existe en el dispositivo.", "OK");

            _allTracks.RemoveAll(t => t.UriString == track.UriString);
            _favoriteTracks.RemoveAll(t => t.UriString == track.UriString);
            BuildAlbumsFromTracks();
            await _favoritesService.SaveFavoritesAsync(_favoriteTracks);

            RefreshPlaylistView(forceReset: true);
            return;
        }

        _currentIndex = index;
        _currentPlayingTrack = track;

        MarkPlayingTrackHighlight(track.UriString);
        UpdateTrackMetadataInUI(track);

        if (expandFullPlayer)
        {
            _ = ExpandFullPlayerAsync();
        }

        try
        {
            await _audioPlayer.PlayAsync(track.UriString, track.Title, track.AlbumName);

            if (_currentIndex != index) return;

            var totalMs = Math.Max(1, _audioPlayer.DurationMilliseconds);
            ProgressSlider.Maximum = totalMs;
            ProgressSlider.Value = 0;
            if (_miniProgressBar != null) _miniProgressBar.Progress = 0;

            LblDuration.Text = FormatTime(totalMs);
            LblElapsed.Text = "0:00";

            SetPlayPauseButtonsUI(isPlaying: true);
            _progressTimer.Start();

            await LoadTrackCoverArtInUIAsync(track);
        }
        catch (OperationCanceledException)
        {
            // Ignorar cancelación por cambio rápido de canción
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error de reproducción", ex.Message, "OK");
        }
    }

    private void MarkPlayingTrackHighlight(string playingUri)
    {
        foreach (var t in _allTracks)
            t.IsPlaying = t.UriString == playingUri;
        foreach (var f in _favoriteTracks)
            f.IsPlaying = f.UriString == playingUri;
    }

    private void UpdateTrackMetadataInUI(MusicTrack track)
    {
        LblCurrentTrack.Text = track.Title;
        if (_lblCurrentAlbum != null)
            _lblCurrentAlbum.Text = track.AlbumName;
        if (_lblCoverArtTitle != null)
            _lblCoverArtTitle.Text = track.Title.ToUpperInvariant();

        if (_lblMiniTrackTitle != null)
            _lblMiniTrackTitle.Text = track.Title;
        if (_lblMiniAlbum != null)
            _lblMiniAlbum.Text = track.AlbumName;

        UpdateFullPlayerFavoriteButton(track);
        UpdateTrackCounter();
    }

    private async Task LoadTrackCoverArtInUIAsync(MusicTrack track)
    {
        var artBytes = await Task.Run(() => _audioPlayer.GetAlbumArtBytes(track.UriString));
        if (_currentPlayingTrack?.UriString != track.UriString)
            return;

        if (artBytes != null && artBytes.Length > 0)
        {
            if (_imgFullCover != null)
            {
                _imgFullCover.Source = ImageSource.FromStream(() => new MemoryStream(artBytes));
                _imgFullCover.IsVisible = true;
            }
            if (_imgMiniCover != null)
            {
                _imgMiniCover.Source = ImageSource.FromStream(() => new MemoryStream(artBytes));
                _imgMiniCover.IsVisible = true;
            }
        }
        else
        {
            if (_imgFullCover != null)
            {
                _imgFullCover.Source = null;
                _imgFullCover.IsVisible = false;
            }
            if (_imgMiniCover != null)
            {
                _imgMiniCover.Source = null;
                _imgMiniCover.IsVisible = false;
            }
        }
    }

    private void SetPlayPauseButtonsUI(bool isPlaying)
    {
        if (_pathFullPlay != null) _pathFullPlay.IsVisible = !isPlaying;
        if (_pathFullPause != null) _pathFullPause.IsVisible = isPlaying;

        if (_pathMiniPlay != null) _pathMiniPlay.IsVisible = !isPlaying;
        if (_pathMiniPause != null) _pathMiniPause.IsVisible = isPlaying;

        LblStatus.Text = isPlaying ? "Reproduciendo" : "En pausa";
    }

    private async void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        if (_audioPlayer.IsPlaying)
        {
            _audioPlayer.Pause();
            _progressTimer.Stop();
            SetPlayPauseButtonsUI(isPlaying: false);
        }
        else if (_currentPlayingTrack != null)
        {
            _audioPlayer.Resume();
            _progressTimer.Start();
            SetPlayPauseButtonsUI(isPlaying: true);
        }
        else if (_currentPlaylist.Count > 0)
        {
            await PlayTrackAtIndexAsync(0, expandFullPlayer: true);
        }
    }

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        if (_currentPlaylist.Count == 0) return;

        int nextIndex;
        if (_isShuffleEnabled && _currentPlaylist.Count > 1)
        {
            do
            {
                nextIndex = _random.Next(0, _currentPlaylist.Count);
            } while (nextIndex == _currentIndex);
        }
        else
        {
            nextIndex = (_currentIndex + 1) % _currentPlaylist.Count;
        }

        await PlayTrackAtIndexAsync(nextIndex, expandFullPlayer: false);
    }

    private async void OnPreviousClicked(object? sender, EventArgs e)
    {
        if (_currentPlaylist.Count == 0) return;

        int prevIndex = _currentIndex <= 0 ? _currentPlaylist.Count - 1 : _currentIndex - 1;
        await PlayTrackAtIndexAsync(prevIndex, expandFullPlayer: false);
    }

    private async void OnPlaybackCompleted()
    {
        _progressTimer.Stop();
        if (_currentPlaylist.Count > 0)
        {
            if (_isRepeatOneEnabled && _currentIndex >= 0 && _currentIndex < _currentPlaylist.Count)
            {
                await PlayTrackAtIndexAsync(_currentIndex, expandFullPlayer: false);
                return;
            }

            int nextIndex;
            if (_isShuffleEnabled && _currentPlaylist.Count > 1)
            {
                do
                {
                    nextIndex = _random.Next(0, _currentPlaylist.Count);
                } while (nextIndex == _currentIndex);
            }
            else
            {
                nextIndex = (_currentIndex + 1) % _currentPlaylist.Count;
            }

            await PlayTrackAtIndexAsync(nextIndex, expandFullPlayer: false);
        }
        else
        {
            SetPlayPauseButtonsUI(isPlaying: false);
            LblStatus.Text = "Detenido";
        }
    }

    private void OnExternalPlaybackStateChanged()
    {
        if (_audioPlayer.IsPlaying)
        {
            SetPlayPauseButtonsUI(isPlaying: true);
            _progressTimer.Start();
        }
        else if (_currentPlayingTrack != null)
        {
            SetPlayPauseButtonsUI(isPlaying: false);
            _progressTimer.Stop();
        }

        if (!_isDraggingSlider && _currentPlayingTrack != null)
        {
            UpdateProgressBarsFromPlayer();
        }
    }

    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        if (_isDraggingSlider || !_audioPlayer.IsPlaying) return;
        UpdateProgressBarsFromPlayer();
    }

    private void UpdateProgressBarsFromPlayer()
    {
        var pos = _audioPlayer.PositionMilliseconds;
        var max = Math.Max(1.0, ProgressSlider.Maximum);

        ProgressSlider.Value = Math.Clamp(pos, 0, max);
        LblElapsed.Text = FormatTime(pos);

        if (_miniProgressBar != null)
        {
            _miniProgressBar.Progress = Math.Clamp(pos / max, 0.0, 1.0);
        }
    }

    private void OnSliderDragStarted(object? sender, EventArgs e) => _isDraggingSlider = true;

    private void OnSliderDragCompleted(object? sender, EventArgs e)
    {
        int targetMs = (int)ProgressSlider.Value;
        _audioPlayer.SeekTo(targetMs);
        LblElapsed.Text = FormatTime(targetMs);

        if (_miniProgressBar != null && ProgressSlider.Maximum > 0)
        {
            _miniProgressBar.Progress = Math.Clamp(targetMs / ProgressSlider.Maximum, 0.0, 1.0);
        }

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
