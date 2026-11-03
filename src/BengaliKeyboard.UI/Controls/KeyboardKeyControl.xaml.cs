using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.UI.Controls;

/// <summary>
/// Reusable Ridmik-style virtual keycap control supporting primary &amp; secondary glyphs,
/// semantic key styling, and bounded press-and-hold repeat for Backspace/Space.
/// </summary>
public partial class KeyboardKeyControl : UserControl
{
    public static readonly DependencyProperty KeyModelProperty =
        DependencyProperty.Register(
            nameof(KeyModel),
            typeof(KeyDefinition),
            typeof(KeyboardKeyControl),
            new PropertyMetadata(null, OnKeyModelChanged));

    public static readonly DependencyProperty KeyCommandProperty =
        DependencyProperty.Register(
            nameof(KeyCommand),
            typeof(ICommand),
            typeof(KeyboardKeyControl),
            new PropertyMetadata(null));

    private DispatcherTimer? _repeatTimer;
    private bool _isPressed;
    private bool _isHovered;

    public KeyDefinition? KeyModel
    {
        get => (KeyDefinition?)GetValue(KeyModelProperty);
        set => SetValue(KeyModelProperty, value);
    }

    public ICommand? KeyCommand
    {
        get => (ICommand?)GetValue(KeyCommandProperty);
        set => SetValue(KeyCommandProperty, value);
    }

    public KeyboardKeyControl()
    {
        InitializeComponent();
        Unloaded += OnControlUnloaded;
    }

    private static void OnKeyModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyboardKeyControl control)
        {
            control.UpdateKeyVisuals();
        }
    }

    private void UpdateKeyVisuals()
    {
        var key = KeyModel;
        if (key is null)
        {
            PrimaryGlyphText.Text = string.Empty;
            SecondaryGlyphText.Text = string.Empty;
            ToolTip = null;
            return;
        }

        PrimaryGlyphText.Text = key.DisplayLabel;
        SecondaryGlyphText.Text = key.AlternateOutput ?? string.Empty;
        SecondaryGlyphText.Visibility = string.IsNullOrEmpty(key.AlternateOutput)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ToolTip = string.IsNullOrWhiteSpace(key.Tooltip) ? null : key.Tooltip;

        PrimaryGlyphText.FontSize = key.KeyType switch
        {
            KeyType.Space => 13.5,
            KeyType.LayoutSwitch or KeyType.LanguageSwitch => 13.5,
            KeyType.Number => 16.5,
            _ => 19.0
        };

        ApplyCurrentSurfaceBrush();
    }

    private void ApplyCurrentSurfaceBrush()
    {
        var keyType = KeyModel?.KeyType ?? KeyType.Character;

        string resourceKey;
        if (keyType == KeyType.Enter)
        {
            resourceKey = _isPressed
                ? "Brush.Accent.Hover"
                : _isHovered
                    ? "Brush.Accent.Hover"
                    : "Brush.Accent.Primary";
        }
        else if (IsSpecialKey(keyType))
        {
            resourceKey = _isPressed
                ? "Brush.Key.Special.Pressed"
                : _isHovered
                    ? "Brush.Key.Special.Hover"
                    : "Brush.Key.Special.Background";
        }
        else
        {
            resourceKey = _isPressed
                ? "Brush.Key.Regular.Pressed"
                : _isHovered
                    ? "Brush.Key.Regular.Hover"
                    : "Brush.Key.Regular.Background";
        }

        if (TryFindResource(resourceKey) is Brush brush)
        {
            KeySurfaceBorder.Background = brush;
        }
    }

    private static bool IsSpecialKey(KeyType keyType) =>
        keyType is KeyType.Shift
            or KeyType.Backspace
            or KeyType.LanguageSwitch
            or KeyType.LayoutSwitch
            or KeyType.EmojiSwitch
            or KeyType.ClipboardSwitch;

    private void OnKeyMouseEnter(object sender, MouseEventArgs e)
    {
        _isHovered = true;
        ApplyCurrentSurfaceBrush();
    }

    private void OnKeyMouseLeave(object sender, MouseEventArgs e)
    {
        _isHovered = false;
        _isPressed = false;
        StopRepeatTimer();
        ApplyCurrentSurfaceBrush();
    }

    private void OnKeyMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPressed = true;
        ApplyCurrentSurfaceBrush();
        ExecuteKeyCommand();

        var keyType = KeyModel?.KeyType;
        if (keyType is KeyType.Backspace or KeyType.Space)
        {
            StartRepeatTimer();
        }

        e.Handled = true;
    }

    private void OnKeyMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPressed = false;
        StopRepeatTimer();
        ApplyCurrentSurfaceBrush();
        e.Handled = true;
    }

    private void ExecuteKeyCommand()
    {
        var key = KeyModel;
        var command = KeyCommand;
        if (key is not null && command?.CanExecute(key) == true)
        {
            command.Execute(key);
        }
    }

    private void StartRepeatTimer()
    {
        StopRepeatTimer();
        _repeatTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(380)
        };
        _repeatTimer.Tick += OnRepeatTimerTick;
        _repeatTimer.Start();
    }

    private void OnRepeatTimerTick(object? sender, EventArgs e)
    {
        if (!_isPressed || !IsVisible)
        {
            StopRepeatTimer();
            return;
        }

        if (_repeatTimer is not null)
        {
            _repeatTimer.Interval = TimeSpan.FromMilliseconds(50);
        }

        ExecuteKeyCommand();
    }

    private void StopRepeatTimer()
    {
        if (_repeatTimer is not null)
        {
            _repeatTimer.Stop();
            _repeatTimer.Tick -= OnRepeatTimerTick;
            _repeatTimer = null;
        }
    }

    private void OnControlUnloaded(object sender, RoutedEventArgs e)
    {
        _isPressed = false;
        _isHovered = false;
        StopRepeatTimer();
    }
}
