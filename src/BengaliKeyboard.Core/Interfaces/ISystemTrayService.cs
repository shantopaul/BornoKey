namespace BengaliKeyboard.Core.Interfaces;

/// <summary>
/// Manages the persistent Windows notification area (system tray) icon and context menu.
/// </summary>
public interface ISystemTrayService : IDisposable
{
    void Initialize();

    void UpdateTrayState();

    void ShowNotification(string title, string message);
}
