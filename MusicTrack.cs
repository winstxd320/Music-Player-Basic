using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MiReproductorMAUI;

public class MusicTrack : INotifyPropertyChanged
{
    private bool _isFavorite;

    public string Title { get; set; } = string.Empty;
    public string UriString { get; set; } = string.Empty;

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
            }
        }
    }

    public string FavoriteIcon => IsFavorite ? "❤️" : "🤍";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}