using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using System.Threading.Tasks;

namespace StickerApp;

[Activity(Theme = "@style/Maui.MainTheme.NoActionBar", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int AddStickerPackRequestCode = 200;
    private static MainActivity? current;
    private static TaskCompletionSource<bool>? addStickerPackResult;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        current = this;
        base.OnCreate(savedInstanceState);
    }

    internal static Task<bool> AddStickerPackAsync(string identifier, string authority, string name)
    {
        if (current is null)
            throw new InvalidOperationException("The Android activity is not available.");
        if (addStickerPackResult is not null)
            throw new InvalidOperationException("Another WhatsApp sticker-pack request is already in progress.");

        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        addStickerPackResult = result;
        var intent = new Intent("com.whatsapp.intent.action.ENABLE_STICKER_PACK");
        intent.PutExtra("sticker_pack_id", identifier);
        intent.PutExtra("sticker_pack_authority", authority);
        intent.PutExtra("sticker_pack_name", name);
        try
        {
            current.StartActivityForResult(intent, AddStickerPackRequestCode);
        }
        catch (ActivityNotFoundException)
        {
            addStickerPackResult = null;
            throw new InvalidOperationException("Install WhatsApp or WhatsApp Business to add sticker packs.");
        }
        return result.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != AddStickerPackRequestCode)
            return;

        var result = addStickerPackResult;
        addStickerPackResult = null;
        result?.TrySetResult(resultCode == Result.Ok);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(current, this))
            current = null;
        base.OnDestroy();
    }
}
