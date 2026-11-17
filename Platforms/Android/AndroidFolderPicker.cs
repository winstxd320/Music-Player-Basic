using Android.Content;
using Android.Provider;
using System.Diagnostics;
using Uri = Android.Net.Uri;

namespace MiReproductorMAUI.Platforms.Android;

public class AndroidFolderPicker : IFolderPicker
{
    public const int RequestCode = 1001;
    private const string SavedFolderUriKey = "SavedFolderTreeUri";
    private static TaskCompletionSource<List<MusicTrack>>? _tcs;

    public Task<List<MusicTrack>> PickFolderAndGetTracksAsync()
    {
        if (_tcs != null) return _tcs.Task;

        _tcs = new TaskCompletionSource<List<MusicTrack>>();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var intent = new Intent(Intent.ActionOpenDocumentTree);
                intent.AddFlags(ActivityFlags.GrantReadUriPermission |
                                ActivityFlags.GrantPersistableUriPermission |
                                ActivityFlags.GrantPrefixUriPermission);
                Platform.CurrentActivity?.StartActivityForResult(intent, RequestCode);
            }
            catch (Exception ex)
            {
                _tcs?.TrySetException(ex);
                _tcs = null;
            }
        });

        return _tcs.Task;
    }

    public static Task HandleActivityResultAsync(Intent? data)
    {
        if (_tcs == null) return Task.CompletedTask;

        if (data?.Data == null)
        {
            Debug.WriteLine("[FolderPicker] Usuario canceló o no envió datos.");
            _tcs.TrySetResult(new List<MusicTrack>());
            _tcs = null;
            return Task.CompletedTask;
        }

        var treeUri = data.Data;

        try
        {
            Platform.AppContext.ContentResolver?.TakePersistableUriPermission(
                treeUri, ActivityFlags.GrantReadUriPermission);

            Preferences.Set(SavedFolderUriKey, treeUri.ToString());

            var tracks = ReadTracksFromTreeUri(treeUri);
            Debug.WriteLine($"[FolderPicker] Total archivos encontrados: {tracks.Count}");
            _tcs.TrySetResult(tracks);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FolderPicker] Error: {ex.Message}");
            _tcs.TrySetException(ex);
        }
        finally
        {
            _tcs = null;
        }

        return Task.CompletedTask;
    }

    public Task<List<MusicTrack>> LoadSavedFolderTracksAsync()
    {
        var savedUri = Preferences.Get(SavedFolderUriKey, string.Empty);
        if (string.IsNullOrEmpty(savedUri))
            return Task.FromResult(new List<MusicTrack>());

        try
        {
            var treeUri = Uri.Parse(savedUri);
            if (treeUri == null)
                return Task.FromResult(new List<MusicTrack>());

            var tracks = ReadTracksFromTreeUri(treeUri);
            return Task.FromResult(tracks);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FolderPicker] Error al cargar carpeta guardada: {ex.Message}");
            Preferences.Remove(SavedFolderUriKey);
            return Task.FromResult(new List<MusicTrack>());
        }
    }

    public bool FileExists(string uriString)
    {
        try
        {
            var uri = Uri.Parse(uriString);
            if (uri == null) return false;

            using var cursor = Platform.AppContext.ContentResolver?.Query(
                uri,
                new[] { DocumentsContract.Document.ColumnDocumentId },
                null, null, null);

            return cursor != null && cursor.MoveToFirst();
        }
        catch
        {
            return false;
        }
    }

    private static List<MusicTrack> ReadTracksFromTreeUri(Uri treeUri)
    {
        var tracks = new List<MusicTrack>();

        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(
            treeUri,
            DocumentsContract.GetTreeDocumentId(treeUri));

        var projection = new[]
        {
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnMimeType
        };

        using var cursor = Platform.AppContext.ContentResolver?.Query(childrenUri, projection, null, null, null);
        if (cursor == null) return tracks;

        while (cursor.MoveToNext())
        {
            var docId = cursor.GetString(0);
            var fileName = cursor.GetString(1) ?? "Desconocido";
            var mimeType = cursor.GetString(2);

            bool isAudio = (mimeType != null && mimeType.StartsWith("audio/")) ||
                           fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                           fileName.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                           fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                           fileName.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                           fileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);

            if (isAudio && docId != null)
            {
                var documentUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, docId);
                if (documentUri != null)
                {
                    tracks.Add(new MusicTrack
                    {
                        Title = Path.GetFileNameWithoutExtension(fileName),
                        UriString = documentUri.ToString()!
                    });
                }
            }
        }

        return tracks.OrderBy(t => t.Title).ToList();
    }
}