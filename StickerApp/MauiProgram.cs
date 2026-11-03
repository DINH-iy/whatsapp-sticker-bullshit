using Microsoft.Extensions.Logging;
using StickerApp.Shared.Services;
using StickerApp.Services;
using StickerApp.Core;

namespace StickerApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Add device-specific services used by the StickerApp.Shared project
        builder.Services.AddSingleton<IFormFactor, FormFactor>();
        builder.Services.AddSingleton(new LibraryStore(Path.Combine(FileSystem.AppDataDirectory, "library")));
        builder.Services.AddSingleton<IWhatsAppExporter, AndroidWhatsAppExporter>();

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
