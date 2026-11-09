using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
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

#if IOS
        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios.SceneOpenUrl((scene, contexts) =>
        {
            var handled = false;
            if (contexts is not null)
                foreach (var context in contexts) handled |= GroupInviteInbox.Receive(context.Url.AbsoluteString);
            return handled;
        })));
#endif

        var diagnostics = new AppLog(Path.Combine(FileSystem.AppDataDirectory, "logs"));
        builder.Services.AddSingleton(diagnostics);
        builder.Logging.AddProvider(new AppLogProvider(diagnostics));
        diagnostics.Info("App startup", $"Stik-er-in {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString}); {DeviceInfo.Current.Platform}; OS {DeviceInfo.Current.VersionString}; .NET {Environment.Version}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) => {
            if (args.ExceptionObject is Exception exception) diagnostics.Error("Unhandled application exception", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => diagnostics.Error("Unobserved background task exception", args.Exception);

        // Add device-specific services used by the StickerApp.Shared project
        builder.Services.AddSingleton<IFormFactor, FormFactor>();
        builder.Services.AddSingleton<IAppRestart, NativeAppRestart>();
        builder.Services.AddSingleton<ConnectionState>();
        builder.Services.AddSingleton<IInternetStatus, NativeInternetStatus>();
        builder.Services.AddSingleton(new LibraryStore(Path.Combine(FileSystem.AppDataDirectory, "library")));
#if ANDROID
        builder.Services.AddSingleton<IBulkImageImporter, AndroidBulkImageImporter>();
        builder.Services.AddSingleton<IWhatsAppExporter, AndroidWhatsAppExporter>();
#elif IOS
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<MainPage, ThemedPageHandler>());
        builder.Services.AddSingleton<INativeAppearance, IosNativeAppearance>();
        builder.Services.AddSingleton<IBulkImageImporter, IosBulkImageImporter>();
        builder.Services.AddSingleton<IWhatsAppExporter, IosWhatsAppExporter>();
#endif
#if ANDROID || IOS
        builder.Services.AddSingleton<ITapFeedback, NativeTapFeedback>();
#endif
        builder.Services.AddScoped<IPackFileSaver, NativePackFileSaver>();
        builder.Services.AddScoped<PackSharingService>();
        builder.Services.AddScoped<StickerApp.Shared.Services.AppLanguage>();
        builder.Services.AddSingleton(_ => new PinterestImportClient());
        builder.Services.AddSingleton<PinterestShareState>();
        builder.Services.AddScoped<StickerPreparationService>();
        builder.Services.AddSingleton<ForegroundActivity>();
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
