using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;

namespace Decisions.EventTriggerFormControl;

/// <summary>
/// Carries a single trigger binding when trigger configuration is passed at
/// runtime via a flow (Set Control Value / Build Array).
///
/// Unlike <see cref="PlatformEventTriggerBinding"/>, this type has no
/// folder-picker editor — the folder ID is entered as a plain string — so it
/// works cleanly in the flow designer without requiring a form surface.
/// </summary>
[Writable]
public class PlatformEventTriggerInput
{
    /// <summary>Which platform event type to listen for.</summary>
    [WritableValue]
    [PropertyClassification(0, "Event Type", "Trigger")]
    public PlatformEventType EventType { get; set; }

    /// <summary>
    /// Optional. Only fire when the event's folder ID matches this value.
    /// Leave empty to match any folder.
    /// </summary>
    [WritableValue]
    [PropertyClassification(1, "Folder ID Filter", "Trigger")]
    [PropertyHiddenByValue("EventType", PlatformEventType.RefreshByKey, true)]
    public string? FolderIdFilter { get; set; }

    /// <summary>
    /// Optional. Only fire when the event's keys contain at least one of these values.
    /// Leave empty to match any keys.
    /// Applies to: RefreshByKey, RefreshByFolderAndKey.
    /// </summary>
    [WritableValue]
    [PropertyClassification(2, "Key Filters", "Trigger")]
    [PropertyHiddenByValue("EventType", PlatformEventType.RefreshByFolder, true)]
    public string[]? KeyFilters { get; set; }

    public override string ToString() => $"{EventType}" +
        (string.IsNullOrEmpty(FolderIdFilter) ? "" : $" ({FolderIdFilter})");
}
