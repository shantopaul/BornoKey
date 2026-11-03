namespace BengaliKeyboard.Core.Interfaces;

/// <summary>
/// Manages Windows auto-startup registration (Start with Windows).
/// </summary>
public interface IStartupService
{
    bool IsStartupEnabled();

    bool SetStartupEnabled(bool enable);
}
