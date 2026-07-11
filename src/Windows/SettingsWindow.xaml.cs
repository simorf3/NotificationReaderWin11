using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using NotificationReader.Models;
using NotificationReader.Services;

// Disambiguate WPF vs WinForms (both enabled project-wide via implicit usings).
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace NotificationReader.Windows;

/// <summary>
/// Window for managing regex filter rules. Editing works against an in-memory
/// copy; changes are only persisted when the user clicks Save.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly FilterService _filterService;

    private readonly ObservableCollection<FilterRule> _rules = new();
    private readonly ObservableCollection<AppToggle> _apps = new();

    /// <summary>Row model for the per-app "Read aloud" list (public so WPF can bind to it).</summary>
    public sealed class AppToggle
    {
        public string AppName { get; set; } = string.Empty;
        public bool ReadAloud { get; set; } = true;
    }

    public SettingsWindow(SettingsService settingsService, FilterService filterService)
    {
        _settingsService = settingsService;
        _filterService = filterService;

        InitializeComponent();

        LoadRules();
        RulesGrid.ItemsSource = _rules;

        LoadApps();
        AppsGrid.ItemsSource = _apps;
    }

    private void LoadApps()
    {
        _apps.Clear();

        var known = _settingsService.Settings.KnownApps ?? new System.Collections.Generic.List<string>();
        var muted = _settingsService.Settings.MutedApps ?? new System.Collections.Generic.List<string>();

        foreach (var app in known.Where(a => !string.IsNullOrWhiteSpace(a))
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(a => a, StringComparer.OrdinalIgnoreCase))
        {
            bool isMuted = muted.Any(m => string.Equals(m, app, StringComparison.OrdinalIgnoreCase));
            _apps.Add(new AppToggle { AppName = app, ReadAloud = !isMuted });
        }
    }

    private void LoadRules()
    {
        _rules.Clear();
        // Work on clones so Cancel discards edits cleanly.
        foreach (var rule in _settingsService.Settings.FilterRules)
        {
            _rules.Add(Clone(rule));
        }
    }

    private static FilterRule Clone(FilterRule r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Pattern = r.Pattern,
        Target = r.Target,
        Action = r.Action,
        IsEnabled = r.IsEnabled
    };

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FilterRuleDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _rules.Add(dialog.Result);
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not FilterRule selected)
        {
            MessageBox.Show(this, "Please select a rule to edit.", "Edit Rule",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new FilterRuleDialog(selected) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            int index = _rules.IndexOf(selected);
            if (index >= 0)
            {
                _rules[index] = dialog.Result;
            }
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not FilterRule selected)
        {
            MessageBox.Show(this, "Please select a rule to delete.", "Delete Rule",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Delete the rule '{selected.Name}'?",
            "Delete Rule",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            _rules.Remove(selected);
        }
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not FilterRule selected)
        {
            MessageBox.Show(this, "Please select a rule to test.", "Test Pattern",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = new TextInputDialog(
            "Test Pattern",
            $"Enter sample text to test against pattern:\n\n{selected.Pattern}")
        {
            Owner = this
        };

        if (input.ShowDialog() != true)
        {
            return;
        }

        string sample = input.InputText ?? string.Empty;
        try
        {
            var regex = new Regex(selected.Pattern, RegexOptions.IgnoreCase);
            bool match = regex.IsMatch(sample);
            MessageBox.Show(this,
                match ? "Match ✓" : "No Match ✗",
                "Test Result",
                MessageBoxButton.OK,
                match ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this,
                "Invalid regex pattern:\n\n" + ex.Message,
                "Test Result",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Commit any in-progress DataGrid edits (e.g. the checkboxes).
        RulesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        RulesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        AppsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        AppsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        _settingsService.Settings.FilterRules = _rules.Select(Clone).ToList();

        // Persist muted apps (those with "Read aloud" unchecked).
        _settingsService.Settings.MutedApps = _apps
            .Where(a => !a.ReadAloud)
            .Select(a => a.AppName)
            .ToList();

        await _settingsService.SaveAsync();
        _filterService.Reload();

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing this window must never terminate the (tray-only) app.
        // ShutdownMode is OnExplicitShutdown, so simply closing is safe.
        base.OnClosing(e);
    }
}
