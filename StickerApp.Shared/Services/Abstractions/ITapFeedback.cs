namespace StickerApp.Shared.Services;

public record TapFeedbackSettings(bool Enabled, int Intensity);

// Only native hosts register this service; the browser has no vibration bridge.
public interface ITapFeedback
{
    TapFeedbackSettings Settings { get; }
    void Configure(bool enabled, int intensity);
    Task TapAsync();
}
