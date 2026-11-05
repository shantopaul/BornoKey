using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.UI.Controls;

/// <summary>
/// Displays 3–5 real-time word completions and next-word predictions directly above the keyboard grid.
/// </summary>
public partial class SuggestionBarControl : UserControl
{
    public static readonly DependencyProperty CandidatesProperty =
        DependencyProperty.Register(
            nameof(Candidates),
            typeof(IEnumerable),
            typeof(SuggestionBarControl),
            new PropertyMetadata(null, OnCandidatesChanged));

    public static readonly DependencyProperty CandidateSelectedCommandProperty =
        DependencyProperty.Register(
            nameof(CandidateSelectedCommand),
            typeof(ICommand),
            typeof(SuggestionBarControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CandidateFontSizeProperty =
        DependencyProperty.Register(
            nameof(CandidateFontSize),
            typeof(double),
            typeof(SuggestionBarControl),
            new PropertyMetadata(14.5));

    public IEnumerable? Candidates
    {
        get => (IEnumerable?)GetValue(CandidatesProperty);
        set => SetValue(CandidatesProperty, value);
    }

    public ICommand? CandidateSelectedCommand
    {
        get => (ICommand?)GetValue(CandidateSelectedCommandProperty);
        set => SetValue(CandidateSelectedCommandProperty, value);
    }

    public double CandidateFontSize
    {
        get => (double)GetValue(CandidateFontSizeProperty);
        set => SetValue(CandidateFontSizeProperty, value);
    }

    public SuggestionBarControl()
    {
        InitializeComponent();
    }

    public void ApplyResponsiveLayout(double uniformScale)
    {
        var clampedScale = Math.Clamp(uniformScale, 0.60, 1.65);
        RootSuggestionBorder.Height = Math.Clamp(Math.Round(36.0 * clampedScale), 26.0, 48.0);
        CandidateFontSize = Math.Clamp(Math.Round(14.5 * clampedScale, 1), 11.0, 20.0);
    }

    private static void OnCandidatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SuggestionBarControl control)
        {
            control.CandidatesItemsControl.ItemsSource = e.NewValue as IEnumerable;
        }
    }

    private void OnCandidateButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SuggestionCandidate candidate } &&
            CandidateSelectedCommand?.CanExecute(candidate) == true)
        {
            CandidateSelectedCommand.Execute(candidate);
        }
    }
}
