namespace MiReproductorMAUI;

public interface IFolderPicker
{
    Task<List<MusicTrack>> PickFolderAndGetTracksAsync();
    Task<List<MusicTrack>> LoadSavedFolderTracksAsync();
    bool FileExists(string uriString);
}