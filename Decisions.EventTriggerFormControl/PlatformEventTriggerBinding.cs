using System;
using System.Runtime.Serialization;
using Decisions.Silverlight.UI.Forms;
using DecisionsFramework.Data.ORMapper;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.ServiceLayer.Services.Folder;

namespace Decisions.EventTriggerFormControl;

/// <summary>
/// Configures a single platform event trigger binding.
/// One PlatformEventTriggerControl can hold many of these, one per distinct trigger scenario.
/// </summary>
[Writable]
public class PlatformEventTriggerBinding : ISurfaceAware
{
    // Injected by PlatformEventTriggerControl so the folder picker can derive project
    // context and show only the current project's folders. Not persisted (no [WritableValue]).
    [PropertyHidden]
    [IgnoreDataMember]
    public IFormSurface? Surface { get; set; }

    /// <summary>Which platform event type to listen for.</summary>
    [WritableValue]
    [PropertyClassification(0, "Event Type", "Trigger")]
    public PlatformEventType EventType { get; set; }

    /// <summary>
    /// Optional. Only fire when the event's folder ID matches this folder.
    /// Leave empty to match any folder.
    /// Applies to: FolderChanged, RefreshByFolder, RefreshByFolderAndKey, ContainedEntityChanged.
    /// </summary>
    [WritableValue]
    [PropertyClassification(1, "Folder Filter", "Trigger")]
    [FolderPickerEditor]
    [PropertyHiddenByValue("EventType", PlatformEventType.RefreshByKey, true)]
    public string? FolderIdFilter { get; set; }

    /// <summary>
    /// Optional. Only fire when the event's keys contain at least one of these values.
    /// Leave empty to match any keys.
    /// Applies to: RefreshByKey, RefreshByFolderAndKey.
    /// </summary>
    [WritableValue]
    [PropertyClassification(2, "Key Filters", "Trigger")]
    [PropertyHiddenByValue("EventType", PlatformEventType.FolderChanged, true)]
    [PropertyHiddenByValue("EventType", PlatformEventType.RefreshByFolder, true)]
    [PropertyHiddenByValue("EventType", PlatformEventType.ContainedEntityChanged, true)]
    [PropertyHiddenByValue("EventType", PlatformEventType.ContainedEntityChangedInTree, true)]
    public string[]? KeyFilters { get; set; }

    /// <summary>
    /// Minimum time that must elapse between successive firings of this binding.
    /// Set to zero (default) to fire every time.
    /// Mirrors the "Minimum Refresh Interval" behaviour on page controls.
    /// </summary>
    [WritableValue]
    [PropertyClassification(3, "Minimum Refresh Interval", "Trigger")]
    public TimeSpan MinimumRefreshInterval { get; set; } = TimeSpan.Zero;

    public override string ToString()
    {
        string folderDisplay = "any folder";
        if (!string.IsNullOrEmpty(FolderIdFilter))
        {
            try
            {
                var folder = new ORM<Folder>().Fetch(FolderIdFilter);
                folderDisplay = folder?.FolderName ?? FolderIdFilter;
            }
            catch
            {
                folderDisplay = FolderIdFilter;
            }
        }
        return $"{EventType} ({folderDisplay})";
    }
}
