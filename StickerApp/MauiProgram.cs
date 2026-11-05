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
#if ANDROID
        builder.Services.AddSingleton<IBulkImageImporter, AndroidBulkImageImporter>();
        builder.Services.AddSingleton<IWhatsAppExporter, AndroidWhatsAppExporter>();
#elif IOS
        builder.Services.AddSingleton<IBulkImageImporter, IosBulkImageImporter>();
        builder.Services.AddSingleton<IWhatsAppExporter, IosWhatsAppExporter>();
#endif
        builder.Services.AddScoped<IPackFileSaver, NativePackFileSaver>();
        builder.Services.AddScoped<PackSharingService>();
        builder.Services.AddSingleton(_ => new PinterestImportClient());
        builder.Services.AddSingleton<PinterestShareState>();
        builder.Services.AddScoped<StickerPreparationService>();
#if IOS
        builder.Services.AddSingleton<IPinterestShareInbox, IosPinterestShareInbox>();
#else
        builder.Services.AddSingleton<IPinterestShareInbox, EmptyPinterestShareInbox>();
#endif

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
