using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BengaliKeyboard.Core.Enums;

namespace BengaliKeyboard.UI.Controls;

/// <summary>
/// Interactive [ বাংলা | ENG ] pill toggle indicator for one-click language mode switching.
/// </summary>
public partial class LanguageToggleControl : UserControl
{
    public static readonly DependencyProperty ActiveLanguageProperty =
        DependencyProperty.Register(
            nameof(ActiveLanguage),
            typeof(LanguageMode),
            typeof(LanguageToggleControl),
            new PropertyMetadata(LanguageMode.Bangla, OnActiveLanguageChanged));

    public static readonly DependencyProperty ToggleCommandProperty =
        DependencyProperty.Register(
            nameof(ToggleCommand),
            typeof(ICommand),
            typeof(LanguageToggleControl),
            new PropertyMetadata(null));

    public LanguageMode ActiveLanguage
    {
        get => (LanguageMode)GetValue(ActiveLanguageProperty);
        set => SetValue(ActiveLanguageProperty, value);
    }

    public ICommand? ToggleCommand
    {
        get => (ICommand?)GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    public LanguageToggleControl()
    {
        InitializeComponent();
        UpdatePillVisuals();
    }

    private static void OnActiveLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LanguageToggleControl control)
        {
            control.UpdatePillVisuals();
        }
    }

    private void UpdatePillVisuals()
    {
        var isBangla = ActiveLanguage == LanguageMode.Bangla;
        if (isBangla)
        {
            BanglaPillBorder.SetResourceReference(Border.BackgroundProperty, "Brush.Accent.Primary");
            EnglishPillBorder.Background = Brushes.Transparent;
        }
        else
        {
            BanglaPillBorder.Background = Brushes.Transparent;
            EnglishPillBorder.SetResourceReference(Border.BackgroundProperty, "Brush.Accent.Primary");
        }
    }

    private void OnToggleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ToggleCommand?.CanExecute(null) == true)
        {
            ToggleCommand.Execute(null);
            e.Handled = true;
        }
    }
}
