using System.ComponentModel;
using System.Windows;
using BengaliKeyboard.UI.ViewModels;

namespace BengaliKeyboard.UI.Windows;

/// <summary>
/// Standalone Windows 11-style 2-pane Settings &amp; Customization Window (760 × 540 px per Design.md Section 5.7).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _allowClose;

    public SettingsViewModel ViewModel => _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = _viewModel;

        InitializeComponent();
    }

    public void PrepareForShutdown()
    {
        _allowClose = true;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && Application.Current is not null)
        {
            e.Cancel = true;
            await _viewModel.SaveAndApplyAsync().ConfigureAwait(true);
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
