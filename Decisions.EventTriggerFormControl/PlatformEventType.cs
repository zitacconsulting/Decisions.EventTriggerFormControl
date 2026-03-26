using System.ComponentModel;

namespace Decisions.EventTriggerFormControl;

/// <summary>
/// The types of server-side platform events that can trigger an Active Form Flow.
/// </summary>
public enum PlatformEventType
{
    /// <summary>
    /// Fires when entities are created, updated, or deleted in the configured folder,
    /// OR when a server-side flow explicitly sends a folder refresh signal for that folder.
    /// </summary>
    [Description("Refresh By Folder")]
    RefreshByFolder,

    /// <summary>
    /// Fires when a server-side flow sends a named refresh signal (key).
    /// </summary>
    [Description("Refresh By Key")]
    RefreshByKey,

    /// <summary>
    /// Fires when a server-side flow sends a named refresh signal (key) for a specific folder.
    /// </summary>
    [Description("Refresh By Folder And Key")]
    RefreshByFolderAndKey,
}
