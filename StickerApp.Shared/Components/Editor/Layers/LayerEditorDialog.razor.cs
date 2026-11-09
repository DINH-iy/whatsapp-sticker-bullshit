using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Components.Editor.Layers;

public partial class LayerEditorDialog
{
    [Parameter] public Guid? StickerId { get; set; }
    [Parameter] public Guid? PackId { get; set; }
    [Parameter] public bool StartWithText { get; set; }
    [Parameter] public bool StartWithShape { get; set; }
    private bool showShapes;
    private string shapeColor="#80d4b0", shapeOutlineColor="#1b1b1b";
    private double shapeThickness=0, shapeRounding=0;
    private bool shapeFilled=true;
    private static readonly (string Kind,string Name,string Glyph)[] Shapes=[("star","Star","★"),("circle","Circle","●"),("square","Square","■"),("triangle","Triangle","▲"),("cube","Cube","⬡"),("heart","Heart","♥"),("arrow","Arrow","➜")];
    [Parameter] public bool ReturnToEditor { get; set; }
    [Parameter] public string? InitialSource { get; set; }
    [Parameter] public string? InitialName { get; set; }
    [Parameter] public bool? InitialNsfw { get; set; }
    private bool nsfw;
    private List<CustomFont> fonts = [];
    private TextLayerEditor? textEditor;
    [Parameter] public IReadOnlyList<Sticker> Images { get; set; } = [];
    [Parameter] public IReadOnlyDictionary<Guid, string> Previews { get; set; } = new Dictionary<Guid, string>();
    [Parameter] public EventCallback OnCancel { get; set; }
    [Parameter] public EventCallback<Guid> OnSaved { get; set; }
    private ElementReference dialog, canvas;
    private IJSObjectReference? module, controller, dialogs, gifTools, gifSession;
    private DotNetObjectReference<LayerEditorDialog>? reference;
    private LayerEditorState state = new();
    private bool showAddMenu;
    private bool working = true, loading = true, keepRatio = true;
    private string name = "Layered sticker", libraryImageId = "", progress = "Opening layer editor…";
    private string? error;
    private string quickPanel = "";
    private string layerCategory = "adjust";
    private EditorLayoutPreferences layout = new();
    private IJSObjectReference? layoutModule;
    private bool layerImageToolActive;
    private bool focusQuickPanel, confirmQuickToolCancel;
    private Task TogglePreview() => Run("Updating preview…", async () =>
    {
        state.PreviewPlaying = !state.PreviewPlaying;
        await controller!.InvokeVoidAsync("preview", state.PreviewPlaying);
    });
    private string PanelTitle => quickPanel switch { "border" => "Object border", "rotate" => "Rotate layer", "opacity" => "Layer opacity", "tint" => "Layer color", _ => "Size & position" };
    private LayerPreview? Selected => state.Selected >= 0 && state.Selected < state.Layers.Count ? state.Layers[state.Selected] : null;
    private LayerPreview? TimelineLayer => Selected?.IsGif == true ? Selected : state.Layers.FirstOrDefault(layer => layer.IsGif);
    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public sealed class LayerExport
    {
        public string Png { get; set; } = "";
        public LayerComposition Document { get; set; } = new();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            if (focusQuickPanel && controller is not null) { focusQuickPanel = false; await controller.InvokeVoidAsync("focusPanel"); }
            return;
        }
        try
        {
            layoutModule = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/editor-layout.js");
            layout = await layoutModule.InvokeAsync<EditorLayoutPreferences>("read");
            dialogs = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/dialog.js");
            await dialogs.InvokeVoidAsync("open", dialog);
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/media/layers/layer-editor.js");
            reference = DotNetObjectReference.Create(this);
            controller = await module.InvokeAsync<IJSObjectReference>("create", canvas, reference);
            nsfw = InitialNsfw ?? Images.FirstOrDefault(image => image.Id == StickerId)?.Nsfw ?? false;
            LayerComposition? composition = null;
            if (StickerId is { } id)
            {
                name = Images.FirstOrDefault(image => image.Id == id)?.Name ?? name;
                composition = await Store.ReadCompositionAsync(id);
            }
            state = await controller.InvokeAsync<LayerEditorState>("open", composition);
            fonts = (await Store.ReadAsync()).Fonts;
            if (composition is null && (InitialSource is not null || StickerId is not null))
            {
                var image = Images.FirstOrDefault(image => image.Id == StickerId);
                var source = InitialSource ?? (image?.Animated == true ? await Store.ReadLayerAnimationAsync(image.Id) : image is not null ? await Store.PreviewAsync(image) : null);
                if (source is not null)
                {
                    name = InitialName ?? image?.Name ?? name;
                    state = await controller.InvokeAsync<LayerEditorState>("add", name, source, 512);
                }
            }
            showShapes=StartWithShape;
            if (StartWithText) state = await controller.InvokeAsync<LayerEditorState>("addText");
            if (Selected?.Text is not null) layerCategory = "text";
        }
        catch (Exception exception) when (IsExpected(exception)) { Report("Open layer editor", exception); }
        finally { working = loading = false; StateHasChanged(); }
    }

    [JSInvokable] public void PreviewChanged(bool playing) { state.PreviewPlaying = playing; StateHasChanged(); }

    [JSInvokable] public Task LayersChanged(LayerEditorState updated) => InvokeAsync(() => { state = updated; StateHasChanged(); });

    private string? layerEditSource, layerEditResult, layerEditId;
    private bool editingLayerBusy, layerEditAnimated;
    private double layerTrimStart, layerTrimEnd;
    private System.Text.Json.JsonElement? layerEditRecipe;
    private string layerInitialTool = "crop";
    private Task EditLayerImage(string tool = "crop") => Run("Opening image editor…", async () =>
    {
        if (Selected is not { } current) return;
        layerInitialTool = tool;
        layerEditId = current.Id;
        layerEditAnimated = current.IsGif; layerTrimStart = current.TrimStart; layerTrimEnd = current.TrimEnd;
        layerEditRecipe = null;
        layerEditSource = await controller!.InvokeAsync<string>("source");
        layerEditResult = await controller!.InvokeAsync<string>("poster");
        quickPanel = "";
    });
    private void LayerImageChanged(string png) => layerEditResult = png;
    private void CancelLayerImage() => layerEditSource = layerEditResult = layerEditId = null;
    private async Task AddTextToLayerImage()
    {
        if (editingLayerBusy || working) return;
        await ApplyLayerImage();
        if (layerEditSource is null) await AddText();
    }
    private Task ApplyLayerImage() => Run("Updating layer artwork…", async () =>
    {
        if (layerEditResult is null || layerEditId is null) return;
        string? animation = null;
        if (layerEditAnimated)
        {
            if (layerEditRecipe is null) throw new InvalidOperationException("Wait for the animation editor to open.");
            gifTools ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/media/animation/gif-editor.js");
            gifSession ??= await gifTools.InvokeAsync<IJSObjectReference>("createSession");
            animation = await gifSession.InvokeAsync<string>("editLayer", TimeSpan.FromMinutes(4), layerEditSource, layerTrimStart, layerTrimEnd, layerEditRecipe.Value);
        }
        state = await controller!.InvokeAsync<LayerEditorState>("replaceSource", layerEditId, layerEditResult, animation);
        CancelLayerImage();
    });

    private async Task FlushText()
    {
        if (Selected?.Text is not null && textEditor is not null) await textEditor.FlushAsync();
        if (queuedText is { } next)
        {
            queuedText = null;
            if (next.Font.StartsWith("custom-") && next.Font != Selected?.Text?.Font)
                await controller!.InvokeVoidAsync("setFonts", (object)new[] { await Store.ReadFontAsync(next.Font) });
            state = await controller!.InvokeAsync<LayerEditorState>("updateText", next);
        }
    }
    private void EnsureShapeOutline(){if(!shapeFilled && shapeThickness<=0)shapeThickness=12;}
    private void OpenShapes(){if(working || layerEditSource is not null || state.Layers.Count>=10)return;showAddMenu=false;showShapes=true;}
    private Task AddShape(string kind,string title)=>Run("Adding shape…",async()=>{
        await FlushText();
        await using var shapes=await JS.InvokeAsync<IJSObjectReference>("import","./_content/StickerApp.Shared/js/media/layers/shapes.js");
        var source=await shapes.InvokeAsync<string>("createShape",kind,shapeColor,new {thickness=shapeThickness,outlineColor=shapeOutlineColor,filled=shapeFilled,rounding=shapeRounding});
        state=await controller!.InvokeAsync<LayerEditorState>("add",title,source,300);
        showShapes=false;quickPanel="";layerCategory="adjust";
    });
    private Task AddText() => Run("Adding text…", async () => { showAddMenu = false; await FlushText(); state = await controller!.InvokeAsync<LayerEditorState>("addText"); quickPanel = ""; layerCategory = "text"; });
    private TextLayerOptions? queuedText;
    private async Task UpdateText(TextLayerOptions text)
    {
        queuedText = text;
        if (working) return;
        while (queuedText is { } next)
        {
            queuedText = null;
            await Run("Updating text…", async () =>
            {
                if (next.Font.StartsWith("custom-") && next.Font != Selected?.Text?.Font)
                    await controller!.InvokeVoidAsync("setFonts", (object)new[] { await Store.ReadFontAsync(next.Font) });
                state = await controller!.InvokeAsync<LayerEditorState>("updateText", next);
            });
        }
    }
    private Task PlaceText(string placement) => Run("Positioning text…", async () => state = await controller!.InvokeAsync<LayerEditorState>("placeText", placement));
    private Task ImportFont(InputFileChangeEventArgs args) => Run("Loading font…", async () =>
    {
        await FlushText();
        await using var stream = args.File.OpenReadStream(LibraryStore.MaxFontBytes);
        using var memory = new MemoryStream(); await stream.CopyToAsync(memory);
        var bytes = memory.ToArray(); LibraryStore.ValidateFont(bytes);
        var data = "data:font/ttf;base64," + Convert.ToBase64String(bytes);
        await controller!.InvokeVoidAsync("validateFont", "custom-" + Guid.NewGuid().ToString("N"), data);
        var font = await Store.ImportFontAsync(args.File.Name, bytes);
        fonts = (await Store.ReadAsync()).Fonts; await controller!.InvokeVoidAsync("setFonts", (object)new[] { font });
        if (Selected?.Text is { } text)
        {
            text = System.Text.Json.JsonSerializer.Deserialize<TextLayerOptions>(System.Text.Json.JsonSerializer.Serialize(text))!;
            text.Font = font.Id;
            state = await controller!.InvokeAsync<LayerEditorState>("updateText", text);
        }
    });

    private Task MarkLayer(int index) => Run("Selecting layers for grouping…", async () => { await FlushText(); state = await controller!.InvokeAsync<LayerEditorState>("mark", index); });
    private Task GroupLayers() => Run("Grouping layers…", async () => { await FlushText(); state = await controller!.InvokeAsync<LayerEditorState>("group", L["Group"]); });
    private Task UngroupLayers() => Run("Ungrouping layers…", async () => { state = await controller!.InvokeAsync<LayerEditorState>("ungroup"); });
    private Task Select(int index) => Run("Selecting layer…", async () => { await FlushText(); state = await controller!.InvokeAsync<LayerEditorState>("select", index); if (Selected?.Text is not null) layerCategory = "text"; });
    private Task Action(string action) => Run("Updating layers…", async () => { await FlushText(); state = await controller!.InvokeAsync<LayerEditorState>("action", action); });
    private Task ToggleTool(string tool) => Run("Opening layer tool…", async () =>
    {
        if (quickPanel == tool) { await CheckQuickToolCancel(); return; }
        await controller!.InvokeVoidAsync("beginTool");
        quickPanel = tool;
    });
    private Task RequestQuickToolCancel() => Run("Opening layer tool…", CheckQuickToolCancel);
    private async Task CheckQuickToolCancel()
    {
        if (await controller!.InvokeAsync<bool>("hasToolChanges")) confirmQuickToolCancel = true;
        else
        {
            confirmQuickToolCancel = false;
            state = await controller!.InvokeAsync<LayerEditorState>("cancelTool");
            quickPanel = "";
        }
    }
    private Task ApplyQuickTool() => Run("Applying layer changes…", async () =>
    {
        state = await controller!.InvokeAsync<LayerEditorState>("commitTool"); quickPanel = "";
    });
    private Task CancelQuickTool() => Run("Cancelling layer changes…", async () =>
    {
        confirmQuickToolCancel = false;
        state = await controller!.InvokeAsync<LayerEditorState>("cancelTool"); quickPanel = "";
    });
    private Task SetRotation(double angle) => Run("Rotating layer…", async () => state = await controller!.InvokeAsync<LayerEditorState>("update", "rotation", ((angle + 180) % 360 + 360) % 360 - 180, false));
    private Task RotateBy(int degrees) => Run("Rotating layer…", async () => state = await controller!.InvokeAsync<LayerEditorState>("rotateBy", degrees));
    private Task SetOpacity(int percent) => Run("Updating opacity…", async () => state = await controller!.InvokeAsync<LayerEditorState>("update", "opacity", percent / 100d, false));
    private Task ToggleTint(ChangeEventArgs args) => Tint(args.Value is true ? "#80d4b0" : null);
    private Task SetHue(double hue) => Run("Adjusting hue…", async () => state = await controller!.InvokeAsync<LayerEditorState>("update", "hue", hue));
    private Task SetColorStrength(int strength) => Run("Adjusting color strength…", async () => state = await controller!.InvokeAsync<LayerEditorState>("update", "colorStrength", strength / 100d));
    private Task Tint(string? color) => Run("Updating color…", async () => state = await controller!.InvokeAsync<LayerEditorState>("update", "tint", color));
    private Task AddLibraryImage() => Run("Adding layer…", async () =>
    {
        showAddMenu = false;
        if (!Guid.TryParse(libraryImageId, out var id) || !Previews.TryGetValue(id, out var source)) return;
        var image = Images.First(image => image.Id == id);
        if (image.Animated)
            source = await Store.ReadLayerAnimationAsync(id) ?? throw new InvalidOperationException("This animation source is unavailable. Import its GIF or animated WebP file again.");
        state = await controller!.InvokeAsync<LayerEditorState>("add", image.Name, source);
    }, showProgress: true);
    private Task AddFiles(InputFileChangeEventArgs args) => Run("Loading layer images…", async () =>
    {
        showAddMenu = false;
        foreach (var file in args.GetMultipleFiles(10))
        {
            if (state.Layers.Count >= 10) throw new InvalidOperationException("A composition supports up to 10 layers.");
            await using var stream = file.OpenReadStream(LibraryStore.MaxImportBytes);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var bytes = memory.ToArray();
            var info = MediaInspector.Inspect(bytes);
            var source = $"data:{info.MimeType};base64,{Convert.ToBase64String(bytes)}";
            state = await controller!.InvokeAsync<LayerEditorState>("add", Path.GetFileNameWithoutExtension(file.Name), source);
        }
    }, showProgress: true);
    private Task Save() => Run("Rendering sticker and saving layers…", async () =>
    {
        await FlushText();
        var export = await controller!.InvokeAsync<LayerExport>("export", TimeSpan.FromMinutes(4));
        if (!export.Png.StartsWith("data:image/png;base64,", StringComparison.Ordinal) && !export.Png.StartsWith("data:image/webp;base64,", StringComparison.Ordinal)) throw new InvalidDataException("Could not export this sticker.");
        var bytes = Convert.FromBase64String(export.Png[(export.Png.IndexOf(',') + 1)..]);
        var id = await Store.SaveCompositionAsync(StickerId, name, bytes, export.Document, PackId, nsfw);
        await OnSaved.InvokeAsync(id);
    }, showProgress: true);
    private async Task Run(string message, Func<Task> action, bool showProgress = false)
    {
        if (working || controller is null) return;
        working = true; loading = showProgress; error = null; progress = message;
        try { await action(); }
        catch (Exception exception) when (IsExpected(exception)) { Report("Layer editor", exception); }
        finally { working = loading = false; }
    }
    private static bool IsExpected(Exception exception) => exception is JSException or IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException;
    private void Report(string operation, Exception exception)
    {
        var id = Log.Error(operation, exception);
        error = $"{exception.Message.Split('\n')[0]} Reference: {id}.";
    }
    private Task Cancel() => working ? Task.CompletedTask : OnCancel.InvokeAsync();
    private Task HandleKey(KeyboardEventArgs args)
    {
        if (args.Key != "Escape") return Task.CompletedTask;
        if (layerEditSource is not null) { if (!editingLayerBusy) CancelLayerImage(); return Task.CompletedTask; }
        if (quickPanel != "") return RequestQuickToolCancel();
        return Cancel();
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (controller is not null) { await controller.InvokeVoidAsync("dispose"); await controller.DisposeAsync(); }
            if (gifSession is not null) { await gifSession.InvokeVoidAsync("dispose"); await gifSession.DisposeAsync(); }
            if (gifTools is not null) await gifTools.DisposeAsync();
            if (module is not null) await module.DisposeAsync();
            if (layoutModule is not null) await layoutModule.DisposeAsync();
            if (dialogs is not null) await dialogs.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        reference?.Dispose();
    }
}
