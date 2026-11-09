namespace StickerApp.Shared.Services;
public sealed class ConnectionState {
    public bool Online {get;private set;}=true;
    public event Action? Changed;
    public void Update(bool online){if(Online==online)return;Online=online;Changed?.Invoke();}
}
public interface IInternetStatus { bool Online {get;} event Action? Changed; }
