namespace MiReproductorMAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder.UseMauiApp<App>();

#if ANDROID
        builder.Services.AddSingleton<IAudioPlayerService, Platforms.Android.AudioPlayerService>();
        builder.Services.AddSingleton<IFolderPicker, Platforms.Android.AndroidFolderPicker>();
#endif

        builder.Services.AddSingleton<FavoritesStorageService>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}