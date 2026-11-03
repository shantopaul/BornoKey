using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BengaliKeyboard.Core.Enums;

namespace BengaliKeyboard.UI.Controls;

/// <summary>
/// Top draggable header bar containing the [ বাংলা | ENG ] language toggle and window mode controls.
/// </summary>
public partial class HeaderToolbarControl : UserControl
{
    public static readonly DependencyProperty ActiveLanguageProperty =
        DependencyProperty.Register(
            nameof(ActiveLanguage),
            typeof(LanguageMode),
            typeof(HeaderToolbarControl),
            new PropertyMetadata(LanguageMode.Bangla, OnActiveLanguageChanged));

    public static readonly DependencyProperty ToggleLanguageCommandProperty =
        DependencyProperty.Register(
            nameof(ToggleLanguageCommand),
            typeof(ICommand),
            typeof(HeaderToolbarControl),
            new PropertyMetadata(null, OnToggleLanguageCommandChanged));

    public event EventHandler? CompactModeRequested;

    public event EventHandler? MinimizeRequested;

    public event EventHandler? CloseRequested;

    public LanguageMode ActiveLanguage
    {
        get => (LanguageMode)GetValue(ActiveLanguageProperty);
        set => SetValue(ActiveLanguageProperty, value);
    }

    public ICommand? ToggleLanguageCommand
    {
        get => (ICommand?)GetValue(ToggleLanguageCommandProperty);
        set => SetValue(ToggleLanguageCommandProperty, value);
    }

    public HeaderToolbarControl()
    {
        InitializeComponent();
    }

    private static void OnActiveLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeaderToolbarControl control && e.NewValue is LanguageMode mode)
        {
            control.LanguageToggle.ActiveLanguage = mode;
        }
    }

    private static void OnToggleLanguageCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeaderToolbarControl control)
        {
            control.LanguageToggle.ToggleCommand = e.NewValue as ICommand;
        }
    }

    private void OnCompactClick(object sender, RoutedEventArgs e) =>
        CompactModeRequested?.Invoke(this, EventArgs.Empty);

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        MinimizeRequested?.Invoke(this, EventArgs.Empty);

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
