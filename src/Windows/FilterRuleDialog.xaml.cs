using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using NotificationReader.Models;

namespace NotificationReader.Windows;

/// <summary>Modal dialog for adding or editing a single <see cref="FilterRule"/>.</summary>
public partial class FilterRuleDialog : Window
{
    private readonly string _editingId;

    /// <summary>The resulting rule when the dialog is accepted; null otherwise.</summary>
    public FilterRule? Result { get; private set; }

    /// <summary>Add mode.</summary>
    public FilterRuleDialog() : this(null)
    {
    }

    /// <summary>Edit mode when <paramref name="existing"/> is provided.</summary>
    public FilterRuleDialog(FilterRule? existing)
    {
        InitializeComponent();

        TargetBox.ItemsSource = Enum.GetValues(typeof(FilterTarget));
        ActionBox.ItemsSource = Enum.GetValues(typeof(FilterAction));

        if (existing != null)
        {
            _editingId = existing.Id;
            Title = "Edit Filter Rule";
            NameBox.Text = existing.Name;
            PatternBox.Text = existing.Pattern;
            TargetBox.SelectedItem = existing.Target;
            ActionBox.SelectedItem = existing.Action;
            EnabledBox.IsChecked = existing.IsEnabled;
        }
        else
        {
            _editingId = Guid.NewGuid().ToString();
            Title = "Add Filter Rule";
            TargetBox.SelectedItem = FilterTarget.Body;
            ActionBox.SelectedItem = FilterAction.Exclude;
        }
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        string pattern = PatternBox.Text ?? string.Empty;
        string sample = SampleBox.Text ?? string.Empty;

        try
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            bool match = regex.IsMatch(sample);
            TestResultText.Text = match ? "Match ✓" : "No Match ✗";
            TestResultText.Foreground = match ? Brushes.Green : Brushes.DarkOrange;
        }
        catch (ArgumentException ex)
        {
            TestResultText.Text = "Invalid regex: " + ex.Message;
            TestResultText.Foreground = Brushes.Red;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string pattern = PatternBox.Text ?? string.Empty;

        // Validate the regex compiles before accepting.
        try
        {
            _ = new Regex(pattern);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this,
                "The pattern is not a valid regular expression:\n\n" + ex.Message,
                "Invalid Pattern",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "Please enter a name for the rule.", "Missing Name",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new FilterRule
        {
            Id = _editingId,
            Name = NameBox.Text.Trim(),
            Pattern = pattern,
            Target = TargetBox.SelectedItem is FilterTarget t ? t : FilterTarget.Body,
            Action = ActionBox.SelectedItem is FilterAction a ? a : FilterAction.Exclude,
            IsEnabled = EnabledBox.IsChecked ?? true
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
