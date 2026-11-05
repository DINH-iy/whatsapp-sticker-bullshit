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
        if (now - lastTap < 150) return;
        lastTap = now;
        try
        {
#if IOS
            // Light impact, deliberately bounded below full device intensity.
            using var generator = new UIKit.UIImpactFeedbackGenerator(UIKit.UIImpactFeedbackStyle.Light);
            generator.Prepare();
            generator.ImpactOccurred((System.Runtime.InteropServices.NFloat)(0.08 + settings.Intensity / 100.0 * 0.32));
#elif ANDROID
            var context = Android.App.Application.Context;
            if (Android.Provider.Settings.System.GetInt(context.ContentResolver,
                    Android.Provider.Settings.System.HapticFeedbackEnabled, 1) == 0) return;
            var vibrator = context.GetSystemService(Android.Content.Context.VibratorService) as Android.OS.Vibrator;
            if (vibrator?.HasVibrator != true) return;
            if (OperatingSystem.IsAndroidVersionAtLeast(26) && vibrator.HasAmplitudeControl)
            {
                // A single 8 ms pulse at 12–70/255 amplitude, never a long buzz.
                using var effect = Android.OS.VibrationEffect.CreateOneShot(8, 12 + settings.Intensity * 58 / 100);
                vibrator.Vibrate(effect);
            }
            else
            {
                // Older motors cannot vary amplitude; use the system's short key tap.
                Platform.CurrentActivity?.Window?.DecorView?.PerformHapticFeedback(Android.Views.HapticFeedbackConstants.KeyboardTap);
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
