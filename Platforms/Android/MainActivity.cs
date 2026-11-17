using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace MiReproductorMAUI;

[Activity(Theme = "@style/Maui.SplashTheme",
          Icon = "@drawable/appicon",
          MainLauncher = true,
          LaunchMode = LaunchMode.SingleTop,
          ConfigurationChanges = ConfigChanges.ScreenSize |
                                 ConfigChanges.Orientation |
                                 ConfigChanges.UiMode |
                                 ConfigChanges.ScreenLayout |
                                 ConfigChanges.SmallestScreenSize |
                                 ConfigChanges.Density)]
public class xdMainActivity : MauiAppCompatActivity
{
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        // ESTO ES LO QUE RECIBE LA CARPETA
        if (requestCode == Platforms.Android.AndroidFolderPicker.RequestCode)
        {
            Platforms.Android.AndroidFolderPicker.HandleActivityResultAsync(data);
        }
    }
}