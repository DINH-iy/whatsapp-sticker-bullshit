using Microsoft.Maui.Networking;
using StickerApp.Shared.Services;
namespace StickerApp.Services;
public sealed class NativeInternetStatus : IInternetStatus, IDisposable {
    public bool Online => Connectivity.Current.NetworkAccess==NetworkAccess.Internet;
    public event Action? Changed;
    public NativeInternetStatus(){Connectivity.Current.ConnectivityChanged+=OnChanged;}
    private void OnChanged(object? sender,ConnectivityChangedEventArgs args)=>Changed?.Invoke();
    public void Dispose()=>Connectivity.Current.ConnectivityChanged-=OnChanged;
}
