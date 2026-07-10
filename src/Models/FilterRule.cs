using System;

namespace NotificationReader.Models;

/// <summary>What a matching rule does.</summary>
public enum FilterAction
{
    /// <summary>Suppress the notification (do not speak).</summary>
    Exclude,

    /// <summary>Allow-list the notification (only these are spoken when any include rule exists).</summary>
    Include
}

/// <summary>Which field a rule's pattern is tested against.</summary>
public enum FilterTarget
{
    AppName,
    Body,
    Both
}

/// <summary>A single regex-based notification filter rule.</summary>
public class FilterRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    /// <summary>Regular expression pattern to match.</summary>
    public string Pattern { get; set; } = string.Empty;

    public FilterTarget Target { get; set; } = FilterTarget.Body;

    public FilterAction Action { get; set; } = FilterAction.Exclude;

    public bool IsEnabled { get; set; } = true;
}
