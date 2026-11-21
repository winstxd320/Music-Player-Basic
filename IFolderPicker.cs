namespace MiReproductorMAUI;

public interface IFolderPicker
{
    Task<List<MusicTrack>> LoadDeviceTracksAsync();
    bool FileExists(string uriString);
}