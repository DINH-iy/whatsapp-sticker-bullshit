using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using System.Threading.Tasks;

namespace StickerApp;

[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "stikerin", DataHost = "join")]
[Activity(Exported = true, LaunchMode = LaunchMode.SingleTop, Theme = "@style/Maui.MainTheme.NoActionBar", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int AddStickerPackRequestCode = 200;
    private const int PickFolderRequestCode = 201;
    private static MainActivity? current;
    private static TaskCompletionSource<bool>? addStickerPackResult;
    private static TaskCompletionSource<Android.Net.Uri>? pickFolderResult;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        current = this;
        base.OnCreate(savedInstanceState);
        StickerApp.Core.GroupInviteInbox.Receive(Intent?.DataString);
        // Native system bars use the neutral app background rather than MAUI's default purple.
        if (Window is { } window &&
            AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView) is { } controller)
            controller.AppearanceLightStatusBars = false;
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        StickerApp.Core.GroupInviteInbox.Receive(intent?.DataString);
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

    internal static Task<Android.Net.Uri> PickFolderAsync()
    {
        if (current is null)
            throw new InvalidOperationException("The Android activity is not available.");
        if (pickFolderResult is not null)
            throw new InvalidOperationException("Another folder request is already in progress.");

        var result = new TaskCompletionSource<Android.Net.Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        pickFolderResult = result;
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        try
        {
            current.StartActivityForResult(intent, PickFolderRequestCode);
        }
        catch (ActivityNotFoundException)
        {
            pickFolderResult = null;
            throw new InvalidOperationException("This device cannot open a folder picker.");
        }
        return result.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != AddStickerPackRequestCode)
        {
            if (requestCode != PickFolderRequestCode)
                return;
            var folderResult = pickFolderResult;
            pickFolderResult = null;
            if (resultCode != Result.Ok || data?.Data is null)
                folderResult?.TrySetException(new global::System.OperationCanceledException("Folder selection was cancelled."));
            else
            {
                var uri = data.Data;
                ContentResolver?.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
                folderResult?.TrySetResult(uri);
            }
            return;
        }

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
