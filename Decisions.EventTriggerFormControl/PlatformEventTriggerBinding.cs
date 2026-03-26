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
    // Injected by PlatformEventTriggerControl so the folder picker can show
    // the current project's folders. Implemented explicitly so it is not a
    // public property — the AFF Build Data step won't enumerate it.
    [IgnoreDataMember]
    private IFormSurface? _surface;
    IFormSurface? ISurfaceAware.Surface { get => _surface; set => _surface = value; }

    /// <summary>Which platform event type to listen for.</summary>
    [WritableValue]
    [PropertyClassification(0, "Event Type", "Trigger")]
    public PlatformEventType EventType { get; set; }

    /// <summary>
    /// Optional. Only fire when the event's folder ID matches this folder.
    /// Leave empty to match any folder.
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
    [PropertyHiddenByValue("EventType", PlatformEventType.RefreshByFolder, true)]
    public string[]? KeyFilters { get; set; }

    public override string ToString()
    {
        string folderDisplay = string.Empty;
        if (!string.IsNullOrEmpty(FolderIdFilter))
        {
            try
            {
                var folder = new ORM<Folder>().Fetch(FolderIdFilter);
                folderDisplay = " (" + (folder?.FolderName ?? FolderIdFilter) + ")";
            }
            catch
            {
                folderDisplay = " (" + FolderIdFilter + ")";
            }
        }
        return $"{EventType}{folderDisplay}";
    }
}
