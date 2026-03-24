using System.ComponentModel;

namespace Decisions.EventTriggerFormControl;

/// <summary>
/// The types of server-side platform events that can trigger an Active Form Flow.
/// </summary>
public enum PlatformEventType
{
    [Description("Folder Changed")]
    FolderChanged,

    [Description("Refresh By Folder")]
    RefreshByFolder,

    [Description("Refresh By Key")]
    RefreshByKey,

    [Description("Refresh By Folder And Key")]
    RefreshByFolderAndKey,

    /// <summary>
    /// Fires when Business Data Type entities are created, updated, or deleted
    /// directly inside the configured folder.
    /// Equivalent to "Contained Entity Change" on page controls.
    /// </summary>
    [Description("Folder Contained Entity Changed")]
    ContainedEntityChanged,

    /// <summary>
    /// Fires when Business Data Type entities are created, updated, or deleted
    /// anywhere within the configured folder or any of its sub-folders.
    /// </summary>
    [Description("Folder Tree Contained Entity Changed")]
    ContainedEntityChangedInTree,
}
