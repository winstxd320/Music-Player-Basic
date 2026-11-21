using Android;
using Android.Content;
using Android.Provider;
using System.Diagnostics;
using Uri = Android.Net.Uri;

namespace MiReproductorMAUI.Platforms.Android;

public class AudioStoragePermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(33)
            ? new[] { ("android.permission.READ_MEDIA_AUDIO", true) }
            : new[] { (Manifest.Permission.ReadExternalStorage, true) };
}

public class AndroidFolderPicker : IFolderPicker
{
    public async Task<List<MusicTrack>> LoadDeviceTracksAsync()
    {
        var hasPermission = await EnsureAudioPermissionAsync();
        if (!hasPermission)
        {
            Debug.WriteLine("[MusicScanner] Permiso de lectura de audio denegado.");
            return new List<MusicTrack>();
        }

        return await Task.Run(ScanDeviceAudioTracks);
    }

    private static async Task<bool> EnsureAudioPermissionAsync()
    {
        try
        {
            var status = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var current = await Permissions.CheckStatusAsync<AudioStoragePermission>();
                if (current != PermissionStatus.Granted)
                {
                    current = await Permissions.RequestAsync<AudioStoragePermission>();
                }

                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    var notifStatus = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
                    if (notifStatus != PermissionStatus.Granted)
                    {
                        await Permissions.RequestAsync<Permissions.PostNotifications>();
                    }
                }

                return current;
            });

            return status == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicScanner] Error solicitando permisos: {ex.Message}");
            return false;
        }
    }

    public bool FileExists(string uriString)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uriString))
                return false;

            if (!uriString.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                return File.Exists(uriString);

            var uri = Uri.Parse(uriString);
            if (uri == null) return false;

            using var cursor = Platform.AppContext.ContentResolver?.Query(
                uri, null, null, null, null);

            return cursor != null && cursor.MoveToFirst();
        }
        catch
        {
            return false;
        }
    }

    private static List<MusicTrack> ScanDeviceAudioTracks()
    {
        var tracks = new List<MusicTrack>();
        var collectionUri = MediaStore.Audio.Media.ExternalContentUri;
        if (collectionUri == null)
            return tracks;

        var projectionList = new List<string>
        {
            MediaStore.Audio.Media.InterfaceConsts.Id,
            MediaStore.Audio.Media.InterfaceConsts.DisplayName,
            MediaStore.Audio.Media.InterfaceConsts.Title,
            MediaStore.Audio.Media.InterfaceConsts.Data
        };

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            projectionList.Add("bucket_display_name");
            projectionList.Add("relative_path");
        }

        var projection = projectionList.ToArray();

        try
        {
            using var cursor = Platform.AppContext.ContentResolver?.Query(
                collectionUri,
                projection,
                null,
                null,
                null);

            if (cursor == null)
                return tracks;

            int idCol = cursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Id);
            int displayNameCol = cursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.DisplayName);
            int titleCol = cursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Title);
            int dataCol = cursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Data);
            int bucketCol = cursor.GetColumnIndex("bucket_display_name");
            int relativePathCol = cursor.GetColumnIndex("relative_path");

            while (cursor.MoveToNext())
            {
                if (idCol < 0) continue;

                long id = cursor.GetLong(idCol);
                string? displayName = displayNameCol >= 0 ? cursor.GetString(displayNameCol) : null;
                string? title = titleCol >= 0 ? cursor.GetString(titleCol) : null;
                string? fullPath = dataCol >= 0 ? cursor.GetString(dataCol) : null;
                string? bucketName = bucketCol >= 0 ? cursor.GetString(bucketCol) : null;
                string? relativePath = relativePathCol >= 0 ? cursor.GetString(relativePathCol) : null;

                // Ignorar sonidos internos del sistema Android
                if (!string.IsNullOrEmpty(fullPath) &&
                    fullPath.StartsWith("/system/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Validar que sea un archivo de audio reconocido si tenemos nombre o ruta
                string nameToCheck = displayName ?? fullPath ?? string.Empty;
                if (!string.IsNullOrEmpty(nameToCheck) && !IsSupportedAudioFile(nameToCheck))
                {
                    continue;
                }

                var contentUri = ContentUris.WithAppendedId(collectionUri, id);
                if (contentUri == null) continue;

                string trackTitle = !string.IsNullOrWhiteSpace(displayName)
                    ? Path.GetFileNameWithoutExtension(displayName)
                    : (!string.IsNullOrWhiteSpace(title) ? title : $"Pista {id}");

                var (folderPath, albumName) = ExtractFolderAndAlbum(fullPath, relativePath, bucketName);

                tracks.Add(new MusicTrack
                {
                    Title = trackTitle,
                    UriString = contentUri.ToString()!,
                    AlbumName = albumName,
                    FolderPath = folderPath
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MusicScanner] Error escaneando MediaStore: {ex.Message}");
        }

        return tracks
            .OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsSupportedAudioFile(string fileName)
    {
        return fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".aac", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".wma", StringComparison.OrdinalIgnoreCase);
    }

    private static (string folderPath, string albumName) ExtractFolderAndAlbum(
        string? fullPath,
        string? relativePath,
        string? bucketName)
    {
        if (!string.IsNullOrWhiteSpace(fullPath))
        {
            var dir = Path.GetDirectoryName(fullPath.Replace('\\', '/'))?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(dir))
            {
                var trimmed = dir.TrimEnd('/');
                var lastSlash = trimmed.LastIndexOf('/');
                var folderName = lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;

                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    return (trimmed, MapFriendlyFolderName(folderName));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            var trimmedRel = relativePath.Replace('\\', '/').Trim('/');
            if (!string.IsNullOrWhiteSpace(trimmedRel))
            {
                var lastSlash = trimmedRel.LastIndexOf('/');
                var folderName = lastSlash >= 0 ? trimmedRel[(lastSlash + 1)..] : trimmedRel;
                return (trimmedRel, MapFriendlyFolderName(folderName));
            }
        }

        if (!string.IsNullOrWhiteSpace(bucketName))
        {
            var friendly = MapFriendlyFolderName(bucketName);
            return (friendly, friendly);
        }

        return ("Almacenamiento interno", "Almacenamiento interno");
    }

    private static string MapFriendlyFolderName(string rawName)
    {
        return rawName == "0" ? "Almacenamiento interno" : rawName;
    }
}