using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Graphics;

namespace MiReproductorMAUI;

public class MusicTrack : INotifyPropertyChanged
{
    private bool _isFavorite;
    private bool _isPlaying;

    public string Title { get; set; } = string.Empty;
    public string UriString { get; set; } = string.Empty;
    public string AlbumName { get; set; } = "música";
    public string FolderPath { get; set; } = string.Empty;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite != value)
            {
                _isFavorite = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FavoriteIcon));
                OnPropertyChanged(nameof(FavoriteColor));
                OnPropertyChanged(nameof(FavoriteFillColor));
                OnPropertyChanged(nameof(FavoriteStrokeColor));
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CardBackgroundColor));
                OnPropertyChanged(nameof(CardBorderColor));
                OnPropertyChanged(nameof(NoteIconColor));
            }
        }
    }

    public string FavoriteIcon => IsFavorite ? "♥" : "♡";
    public Color FavoriteColor => IsFavorite ? Color.FromArgb("#FF3B5C") : Color.FromArgb("#9E94AD");
    public Color FavoriteFillColor => IsFavorite ? Color.FromArgb("#FF3B5C") : Colors.Transparent;
    public Color FavoriteStrokeColor => IsFavorite ? Color.FromArgb("#FF3B5C") : Color.FromArgb("#9E94AD");

    public Color CardBackgroundColor => IsPlaying ? Color.FromArgb("#36204A") : Color.FromArgb("#221F2A");
    public Color CardBorderColor => IsPlaying ? Color.FromArgb("#8B3DFF") : Color.FromArgb("#2D2938");
    public Color NoteIconColor => IsPlaying ? Color.FromArgb("#D8B4FE") : Color.FromArgb("#8E889C");

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class MusicAlbum
{
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public List<MusicTrack> Tracks { get; set; } = new();
    public int TrackCount => Tracks.Count;
    public string TrackCountText => Tracks.Count == 1 ? "1 pista" : $"{Tracks.Count} pistas";
}