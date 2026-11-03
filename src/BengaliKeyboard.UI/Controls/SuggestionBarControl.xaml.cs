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

    public SuggestionBarControl()
    {
        InitializeComponent();
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
