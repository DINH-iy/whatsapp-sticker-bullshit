namespace StickerApp.Core;

public sealed record FactoryResetProgress(string Stage, int CompletedFiles = 0, int TotalFiles = 0);
