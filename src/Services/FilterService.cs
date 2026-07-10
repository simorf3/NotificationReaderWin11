using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NotificationReader.Models;

namespace NotificationReader.Services;

/// <summary>
/// Decides whether a given notification should be spoken, based on the user's
/// regex filter rules.
///
/// Rules:
///  * If there are NO enabled Include rules, everything is spoken by default,
///    and Exclude rules remove matching items.
///  * If there ARE enabled Include rules, only items matching at least one
///    Include rule are candidates for speaking.
///  * Exclude rules always take priority: if any Exclude rule matches, the item
///    is suppressed regardless of Include rules.
/// </summary>
public class FilterService
{
    private readonly SettingsService _settingsService;

    private readonly List<CompiledRule> _includeRules = new();
    private readonly List<CompiledRule> _excludeRules = new();

    public FilterService(SettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    private sealed class CompiledRule
    {
        public required Regex Regex { get; init; }
        public required FilterTarget Target { get; init; }
    }

    /// <summary>Recompiles the regex cache from current settings.</summary>
    public void Reload()
    {
        _includeRules.Clear();
        _excludeRules.Clear();

        var rules = _settingsService.Settings.FilterRules;
        if (rules == null)
        {
            return;
        }

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled || string.IsNullOrEmpty(rule.Pattern))
            {
                continue;
            }

            Regex regex;
            try
            {
                regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            catch (ArgumentException ex)
            {
                Logger.Log($"Skipping filter rule '{rule.Name}' with invalid regex.", ex);
                continue;
            }

            var compiled = new CompiledRule { Regex = regex, Target = rule.Target };
            if (rule.Action == FilterAction.Include)
            {
                _includeRules.Add(compiled);
            }
            else
            {
                _excludeRules.Add(compiled);
            }
        }
    }

    /// <summary>Returns true if the notification should be spoken.</summary>
    public bool ShouldSpeak(string appName, string body)
    {
        appName ??= string.Empty;
        body ??= string.Empty;

        // Exclude rules always win.
        foreach (var rule in _excludeRules)
        {
            if (Matches(rule, appName, body))
            {
                return false;
            }
        }

        // No include rules => speak everything not excluded.
        if (_includeRules.Count == 0)
        {
            return true;
        }

        // Include rules present => must match at least one.
        foreach (var rule in _includeRules)
        {
            if (Matches(rule, appName, body))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(CompiledRule rule, string appName, string body)
    {
        return rule.Target switch
        {
            FilterTarget.AppName => rule.Regex.IsMatch(appName),
            FilterTarget.Body => rule.Regex.IsMatch(body),
            FilterTarget.Both => rule.Regex.IsMatch(appName) || rule.Regex.IsMatch(body),
            _ => false
        };
    }
}
