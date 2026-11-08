using StickerApp.Core;
using StickerApp.Shared.Services;

namespace StickerApp.Services;

public sealed class NativeTapFeedback(AppLog log) : ITapFeedback
{
    private const string EnabledKey = "feedback.vibration.enabled";
    private const string IntensityKey = "feedback.vibration.intensity";
    private long lastTap;
    private bool unavailable;

    public TapFeedbackSettings Settings => new(
        Preferences.Default.Get(EnabledKey, true),
        Math.Clamp(Preferences.Default.Get(IntensityKey, 25), 1, 100));

    public void Configure(bool enabled, int intensity)
    {
        Preferences.Default.Set(EnabledKey, enabled);
        Preferences.Default.Set(IntensityKey, Math.Clamp(intensity, 1, 100));
    }

    public Task TapAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        var settings = Settings;
        if (!settings.Enabled || unavailable) return;
        var now = Environment.TickCount64;
        if (now - lastTap < 20) return;
        lastTap = now;
        try
        {
#if IOS
            // Fifty percent is twice the former maximum; devices cap physical intensity.
            using var generator = new UIKit.UIImpactFeedbackGenerator(UIKit.UIImpactFeedbackStyle.Heavy);
            generator.Prepare();
            generator.ImpactOccurred((System.Runtime.InteropServices.NFloat)(Math.Min(1.0, settings.Intensity / 50.0 * 0.8)));
#elif ANDROID
            var context = Android.App.Application.Context;
            if (Android.Provider.Settings.System.GetInt(context.ContentResolver,
                    Android.Provider.Settings.System.HapticFeedbackEnabled, 1) == 0) return;
            var vibrator = context.GetSystemService(Android.Content.Context.VibratorService) as Android.OS.Vibrator;
            if (vibrator?.HasVibrator != true) return;
            if (OperatingSystem.IsAndroidVersionAtLeast(26) && vibrator.HasAmplitudeControl)
            {
                // At 50% use 140/255 (twice the former 70/255 maximum).
                using var effect = Android.OS.VibrationEffect.CreateOneShot(16, Math.Min(255, settings.Intensity * 280 / 100));
                vibrator.Vibrate(effect);
            }
            else
            {
                // Older motors cannot vary amplitude; use the system's short key tap.
                Platform.CurrentActivity?.Window?.DecorView?.PerformHapticFeedback(Android.Views.FeedbackConstants.KeyboardTap);
            }
#endif
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Feedback is optional. Avoid retrying or logging on every button press.
            unavailable = true;
            log.Error("Touch feedback", exception);
        }
    });
}
