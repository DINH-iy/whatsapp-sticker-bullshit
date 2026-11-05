using StickerApp.Web.Components;
using StickerApp.Shared.Services;
using StickerApp.Web.Services;
using StickerApp.Core;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var diagnostics = new AppLog(Path.Combine(builder.Environment.ContentRootPath, ".data", "logs"));
builder.Services.AddSingleton(diagnostics);
builder.Logging.AddProvider(new AppLogProvider(diagnostics));
AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception exception) diagnostics.Error("Unhandled application exception", exception); };
TaskScheduler.UnobservedTaskException += (_, args) => diagnostics.Error("Unobserved background task exception", args.Exception);
diagnostics.Info("Web app startup", $"Stick it browser host; .NET {Environment.Version}");
if (builder.Environment.IsDevelopment())
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 64 * 1024 * 1024);

// Add device-specific services used by the StickerApp.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddSingleton(new LibraryStore(Path.Combine(builder.Environment.ContentRootPath, ".data")));
builder.Services.AddSingleton<IWhatsAppExporter, BrowserWhatsAppExporter>();
builder.Services.AddSingleton<IBulkImageImporter, BrowserBulkImageImporter>();
builder.Services.AddScoped<IPackFileSaver, BrowserPackFileSaver>();
builder.Services.AddScoped<PackSharingService>();
builder.Services.AddSingleton(_ => new PinterestImportClient());
builder.Services.AddSingleton<IPinterestShareInbox, EmptyPinterestShareInbox>();
builder.Services.AddScoped<PinterestShareState>();
builder.Services.AddScoped<StickerPreparationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// This first build is a local development tool; bind to localhost only.

app.MapStaticAssets();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(StickerApp.Shared._Imports).Assembly);

app.Run();
