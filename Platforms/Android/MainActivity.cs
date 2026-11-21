using Android.App;
using Android.Content.PM;
using Android.Views;

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
public class MainActivity : MauiAppCompatActivity
{
    private float _touchStartX;
    private float _touchStartY;
    private bool _trackingTouch;
    private bool _isHorizontalDragging;
    private bool _ignoreCurrentGesture;

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e == null)
            return base.DispatchTouchEvent(e);

        var mainPage = MainPage.CurrentInstance;
        if (mainPage == null)
            return base.DispatchTouchEvent(e);

        float density = Resources?.DisplayMetrics?.Density ?? 2.5f;
        int screenHeightPx = Resources?.DisplayMetrics?.HeightPixels ?? 2000;

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _touchStartX = e.GetX();
                _touchStartY = e.GetY();
                _isHorizontalDragging = false;
                _ignoreCurrentGesture = !mainPage.CanStartHorizontalTabDrag(_touchStartY, screenHeightPx);
                _trackingTouch = !_ignoreCurrentGesture;
                break;

            case MotionEventActions.Move:
                if (!_trackingTouch || _ignoreCurrentGesture)
                    break;

                float deltaX = e.GetX() - _touchStartX;
                float deltaY = e.GetY() - _touchStartY;

                if (!_isHorizontalDragging)
                {
                    float dragSlopPx = 14f * density;

                    // Si el usuario empezó a hacer scroll vertical en la lista, no interceptar este gesto
                    if (Math.Abs(deltaY) > dragSlopPx && Math.Abs(deltaY) > Math.Abs(deltaX))
                    {
                        _ignoreCurrentGesture = true;
                        _trackingTouch = false;
                        break;
                    }

                    // Si el movimiento es claramente horizontal, cancelar el toque en el CollectionView y tomar el control suave
                    if (Math.Abs(deltaX) > dragSlopPx && Math.Abs(deltaX) > Math.Abs(deltaY) * 1.35f)
                    {
                        _isHorizontalDragging = true;

                        var cancelEvent = MotionEvent.Obtain(e);
                        if (cancelEvent != null)
                        {
                            cancelEvent.Action = MotionEventActions.Cancel;
                            base.DispatchTouchEvent(cancelEvent);
                            cancelEvent.Recycle();
                        }
                    }
                }

                if (_isHorizontalDragging)
                {
                    mainPage.OnHorizontalDragUpdated(deltaX / density);
                    return true;
                }
                break;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_isHorizontalDragging)
                {
                    float totalDeltaX = e.GetX() - _touchStartX;
                    _isHorizontalDragging = false;
                    _trackingTouch = false;
                    mainPage.OnHorizontalDragCompleted(totalDeltaX / density);
                    return true;
                }
                _trackingTouch = false;
                _ignoreCurrentGesture = false;
                break;
        }

        return base.DispatchTouchEvent(e);
    }
}