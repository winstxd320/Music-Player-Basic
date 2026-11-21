using System.Text.Json;

namespace MiReproductorMAUI;

public class FavoritesStorageService
{
    private readonly string _filePath = Path.Combine(FileSystem.AppDataDirectory, "favorites.json");
    private readonly IFolderPicker _folderPicker;

    public FavoritesStorageService(IFolderPicker folderPicker)
    {
        _folderPicker = folderPicker;
    }

    public async Task<List<MusicTrack>> LoadValidFavoritesAsync()
    {
        if (!File.Exists(_filePath))
            return new List<MusicTrack>();

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            var savedTracks = JsonSerializer.Deserialize<List<MusicTrack>>(json) ?? new List<MusicTrack>();

            // Filtra únicamente los archivos que siguen existiendo en el dispositivo (en segundo plano)
            var validTracks = await Task.Run(() =>
                savedTracks
                    .Where(t => !string.IsNullOrWhiteSpace(t.UriString) && _folderPicker.FileExists(t.UriString))
                    .ToList());

            foreach (var track in validTracks)
            {
                track.IsFavorite = true;
            }

            // Si algún archivo fue borrado del dispositivo, actualiza el JSON guardado
            if (validTracks.Count != savedTracks.Count)
            {
                await SaveFavoritesAsync(validTracks);
            }

            return validTracks;
        }
        catch
        {
            return new List<MusicTrack>();
        }
    }

    public async Task SaveFavoritesAsync(IEnumerable<MusicTrack> favorites)
    {
        var json = JsonSerializer.Serialize(favorites.ToList());
        await File.WriteAllTextAsync(_filePath, json);
    }
}