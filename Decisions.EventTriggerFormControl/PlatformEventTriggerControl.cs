using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using DecisionsFramework.ComponentData;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Form;
using DecisionsFramework.Design.Form.ComponentData;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.ServiceLayer;
using DecisionsFramework.ServiceLayer.Services.ConfigurationStorage;
using DecisionsFramework.ServiceLayer.Services.ContextData;
using DecisionsFramework.Utilities.Data;
using Decisions.Silverlight.UI.Forms;
using Silverdark.Components;

namespace Decisions.EventTriggerFormControl;

// ── Initializer ──────────────────────────────────────────────────────────────

public class PlatformEventTriggerControlInitializer : IBootInitializable, IInitializable, IInializableOrder
{
    public int Ordinal => 10000;

    public InitializableHost[] Environments => new[]
    {
        InitializableHost.Unmanaged,
        InitializableHost.User,
        InitializableHost.Control,
    };

    public InitializablePhase Phase => InitializablePhase.ApplicationBoot;

    public string Name => "Register Event Trigger Form Control";

    public void Initialize()
    {
        ConfigurationStorageService.RegisterModulesToolboxElement(
            "Event Trigger",
            typeof(PlatformEventTriggerControl).AssemblyQualifiedName!,
            "Trigger",
            "Decisions.EventTriggerFormControl",
            ElementType.FormElement);
    }
}

// ── Server event ─────────────────────────────────────────────────────────────

internal sealed class PlatformEventValueChangedEvent : IDecisionsControlServerEvent
{
    public string EventName => "Value Changed";
    public FormTriggerType? Event => FormTriggerType.ValueChanged;
}

// ── Conditional required attribute ───────────────────────────────────────────

/// <summary>Marks a property as required when Set Triggers From Flow Data is enabled.</summary>
internal sealed class RequiredWhenTriggersFromFlowDataAttribute : AbstractPropertyRequiredAttribute
{
    public RequiredWhenTriggersFromFlowDataAttribute()
        : base("Triggers Data Name is required when Set Triggers From Flow Data is enabled.") { }

    public override bool IsRequired(object owner) =>
        owner is PlatformEventTriggerControl ctrl && ctrl.TriggersFromFlowData;
}

// ── Control ───────────────────────────────────────────────────────────────────

/// <summary>
/// Form control that bridges server-side platform events to Active Form Flows.
///
/// Renders an ON/OFF toggle button at both design time and runtime. Clicking
/// toggles auto-refresh without firing any AFF. Platform events only fire AFFs
/// when the toggle is ON.
///
/// To hide the button entirely, set Initial Visibility → Hidden in Behaviour.
/// </summary>
[Writable]
public class PlatformEventTriggerControl
    : DataContentBase<PlatformEventTriggerControl, string>, IExposeDataProducersOnSetFormControlStep
{
    // The event outputs (EventType, FolderId, Keys) are read-only values that
    // the control fires outward — they should not appear as settable inputs in
    // the "Set Control Value" step.
    bool IExposeDataProducersOnSetFormControlStep.ExposeDataProducers() => false;
    public PlatformEventTriggerControl()
    {
        RequestedWidth  = 200.0;
        RequestedHeight = 32.0;
    }

    // ── Trigger configuration ─────────────────────────────────────────────────

    private PlatformEventTriggerBinding[]? _triggers;

    /// <summary>
    /// Minimum time (in milliseconds) between successive firings of this control,
    /// regardless of which binding triggered it. Set to 0 (default) to fire every time.
    /// </summary>
    [ClientOption]
    [WritableValue]
    [PropertyClassification(1, "Minimum Refresh Interval (ms)", "Trigger Settings")]
    public int MinimumRefreshIntervalMs { get; set; } = 0;

    /// <summary>
    /// When ticked, the AFF fires immediately when the user turns the toggle ON.
    /// The AFF receives EventType = "ReEnable" to distinguish from platform-event fires.
    /// </summary>
    [ClientOption]
    [WritableValue]
    [PropertyClassification(2, "Fire On Re-enable", "Trigger Settings")]
    public bool FireOnReEnable { get; set; } = false;

    private bool _triggersFromFlowData;

    /// <summary>
    /// When ticked, hides the static trigger list and reads the trigger configuration
    /// from a form data name at runtime. This lets an AFF pass a typed
    /// PlatformEventTriggerBinding[] array via Set Control Value to change what
    /// the control listens for without reloading the form.
    /// </summary>
    [ClientOption]
    [WritableValue]
    [PropertyClassification(3, "Set Triggers From Flow Data", "Trigger Settings")]
    [RefreshProperties(RefreshProperties.All)]
    public bool TriggersFromFlowData
    {
        get => _triggersFromFlowData;
        set
        {
            _triggersFromFlowData = value;
            OnPropertyChanged(nameof(TriggersFromFlowData));
            // Notify all properties so the designer hides/shows Triggers and TriggersDataName.
            OnPropertyChanged(null);
        }
    }

    /// <summary>
    /// The form data name that carries the trigger configuration when
    /// Set Triggers From Flow Data is ticked. The AFF can map a
    /// PlatformEventTriggerBinding[] variable directly via Set Control Value.
    /// </summary>
    [ClientOption]
    [WritableValue]
    [PropertyClassification(4, "Triggers Data Name", "Trigger Settings")]
    [PropertyHiddenByValue("TriggersFromFlowData", true, false)]
    [FormDataPickerEditor(false)]
    [RequiredWhenTriggersFromFlowData]
    public string? TriggersDataName { get; set; }

    [WritableValue]
    [PropertyClassification(0, "Platform Event Triggers", "Trigger Settings")]
    [PropertyHiddenByValue("TriggersFromFlowData", true, true)]
    public PlatformEventTriggerBinding[]? Triggers
    {
        get
        {
            if (_triggers != null && Surface != null)
                foreach (var b in _triggers) ((ISurfaceAware)b).Surface = Surface;
            return _triggers;
        }
        set
        {
            _triggers = value;
            if (_triggers != null && Surface != null)
                foreach (var b in _triggers) ((ISurfaceAware)b).Surface = Surface;
            OnPropertyChanged(nameof(Triggers));
        }
    }

    /// <summary>
    /// Trigger configuration serialized to JSON for the JS control.
    /// [ClientOption] on complex [Writable] arrays only sends type metadata —
    /// this property explicitly serializes the values the JS needs.
    /// Only used when TriggersFromFlowData is false.
    /// </summary>
    [ClientOption]
    [PropertyHidden]
    public string TriggersJson
    {
        get
        {
            if (_triggers == null || _triggers.Length == 0) return "[]";
            return JsonSerializer.Serialize(_triggers.Select(t => new
            {
                eventType      = (int)t.EventType,
                folderIdFilter = t.FolderIdFilter,
                keyFilters     = t.KeyFilters,
            }));
        }
    }

    // ── Button labels ─────────────────────────────────────────────────────────

    private string _enabledLabel = "⚡ Auto Refresh: ON";
    [ClientOption] [WritableValue]
    [PropertyClassification(10, "Enabled Label", "Toggle Button")]
    [PropertyHiddenByValue("EnabledTextFromDataName", true, true)]
    public string EnabledLabel
    {
        get => _enabledLabel;
        set { _enabledLabel = value; OnPropertyChanged(nameof(EnabledLabel)); }
    }

    private bool _enabledTextFromDataName;
    [ClientOption] [WritableValue]
    [PropertyClassification(11, "Enabled Text from Data", "Toggle Button")]
    public bool EnabledTextFromDataName
    {
        get => _enabledTextFromDataName;
        set { _enabledTextFromDataName = value; OnPropertyChanged(nameof(EnabledTextFromDataName)); }
    }

    private string? _enabledTextDataName;
    [ClientOption] [WritableValue]
    [PropertyClassification(12, "Enabled Text Data Name", "Toggle Button")]
    [PropertyHiddenByValue("EnabledTextFromDataName", true, false)]
    [FormDataPickerEditor(false)]
    public string? EnabledTextDataName
    {
        get => _enabledTextDataName;
        set { _enabledTextDataName = value; OnPropertyChanged(nameof(EnabledTextDataName)); }
    }

    private string _disabledLabel = "⏸ Auto Refresh: OFF";
    [ClientOption] [WritableValue]
    [PropertyClassification(13, "Disabled Label", "Toggle Button")]
    [PropertyHiddenByValue("DisabledTextFromDataName", true, true)]
    public string DisabledLabel
    {
        get => _disabledLabel;
        set { _disabledLabel = value; OnPropertyChanged(nameof(DisabledLabel)); }
    }

    private bool _disabledTextFromDataName;
    [ClientOption] [WritableValue]
    [PropertyClassification(14, "Disabled Text from Data", "Toggle Button")]
    public bool DisabledTextFromDataName
    {
        get => _disabledTextFromDataName;
        set { _disabledTextFromDataName = value; OnPropertyChanged(nameof(DisabledTextFromDataName)); }
    }

    private string? _disabledTextDataName;
    [ClientOption] [WritableValue]
    [PropertyClassification(15, "Disabled Text Data Name", "Toggle Button")]
    [PropertyHiddenByValue("DisabledTextFromDataName", true, false)]
    [FormDataPickerEditor(false)]
    public string? DisabledTextDataName
    {
        get => _disabledTextDataName;
        set { _disabledTextDataName = value; OnPropertyChanged(nameof(DisabledTextDataName)); }
    }

    // ── Button appearance ─────────────────────────────────────────────────────

    private string? _cssClass;
    [ClientOption] [WritableValue]
    [PropertyClassification(20, "CSS Class", "View")]
    [CSSClassPickerEditor("CssClassList")]
    public string? CssClass
    {
        get => _cssClass;
        set { _cssClass = value; OnPropertyChanged(nameof(CssClass)); }
    }

    private string? _enabledBgColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(21, "Enabled Background Color", "View")]
    [ColorPickerEditor]
    public string? EnabledBackgroundColor
    {
        get => _enabledBgColor;
        set { _enabledBgColor = value; OnPropertyChanged(nameof(EnabledBackgroundColor)); }
    }

    private string? _enabledBorderColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(22, "Enabled Border Color", "View")]
    [ColorPickerEditor]
    public string? EnabledBorderColor
    {
        get => _enabledBorderColor;
        set { _enabledBorderColor = value; OnPropertyChanged(nameof(EnabledBorderColor)); }
    }

    private string? _enabledTextColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(23, "Enabled Text Color", "View")]
    [ColorPickerEditor]
    public string? EnabledTextColor
    {
        get => _enabledTextColor;
        set { _enabledTextColor = value; OnPropertyChanged(nameof(EnabledTextColor)); }
    }

    private string? _disabledBgColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(24, "Disabled Background Color", "View")]
    [ColorPickerEditor]
    public string? DisabledBackgroundColor
    {
        get => _disabledBgColor;
        set { _disabledBgColor = value; OnPropertyChanged(nameof(DisabledBackgroundColor)); }
    }

    private string? _disabledBorderColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(25, "Disabled Border Color", "View")]
    [ColorPickerEditor]
    public string? DisabledBorderColor
    {
        get => _disabledBorderColor;
        set { _disabledBorderColor = value; OnPropertyChanged(nameof(DisabledBorderColor)); }
    }

    private string? _disabledTextColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(26, "Disabled Text Color", "View")]
    [ColorPickerEditor]
    public string? DisabledTextColor
    {
        get => _disabledTextColor;
        set { _disabledTextColor = value; OnPropertyChanged(nameof(DisabledTextColor)); }
    }

    private int? _borderWidth;
    [ClientOption] [WritableValue]
    [PropertyClassification(27, "Border Width (px)", "View")]
    public int? BorderWidth
    {
        get => _borderWidth;
        set { _borderWidth = value; OnPropertyChanged(nameof(BorderWidth)); }
    }

    private int? _cornerRadius;
    [ClientOption] [WritableValue]
    [PropertyClassification(28, "Corner Radius (px)", "View")]
    public int? CornerRadius
    {
        get => _cornerRadius;
        set { _cornerRadius = value; OnPropertyChanged(nameof(CornerRadius)); }
    }

    // ── ISilverFormEventsProvider ─────────────────────────────────────────────

    public override FormTriggerType[] ProvidedEvents => new[] { FormTriggerType.ValueChanged };

    // ── Value exchange ────────────────────────────────────────────────────────
    // The JS increments a counter and calls raiseEvent(DataChangedEvent) each time
    // a platform event fires (and the toggle is ON). SetValue always returns true
    // so the framework fires any configured AFFs.

    // Event data stored by SetValue() and returned via ControlValues so the
    // framework picks them up and writes them into formDataDictionary after each
    // SetControlValue() call (FormSessionInfo.UpdateControlData line 634).
    private string _currentEventType   = "";
    private string _currentFolderId    = "";
    private string _currentKeys        = "";

    // When TriggersFromFlowData is on and SetControlValue receives new bindings,
    // we encode the trigger JSON into the primary DataName DataPair using the
    // "TRIGGERS:" prefix.  This is the only reliable way to get data to the JS
    // control: the client framework routes each DataPair by DataPair.Name, so a
    // DataPair whose Name != DataName would be silently dropped by the router.
    private bool   _triggersPendingSync = false;
    private string _currentTriggersJson = "[]";

    public override DataPair[] ControlValues
    {
        get
        {
            // When a trigger update is pending, encode the JSON into the primary
            // value so the JS setValue can detect the "TRIGGERS:" prefix and
            // resubscribe.  Clear the flag immediately so that subsequent reads
            // (e.g. from a different code path) return the normal counter "0".
            string primary = "0";
            if (_triggersPendingSync)
            {
                primary = "TRIGGERS:" + _currentTriggersJson;
                _triggersPendingSync = false;
            }
            return new[]
            {
                new DataPair(DataName,                primary),
                new DataPair(DataName + "_EventType", _currentEventType),
                new DataPair(DataName + "_FolderId",  _currentFolderId),
                new DataPair(DataName + "_Keys",      _currentKeys),
            };
        }
    }

    /// <summary>
    /// Called by DataFlowHandler.UpdateControl whenever a Set Control Value step
    /// (or a data-flow) pushes new values for this control's InputData names.
    /// We intercept the TriggersDataName key and store the serialized JSON so
    /// ControlValues can forward it to the JS via the primary DataPair.
    /// </summary>
    public override void SetControlValue(FormDataDictionary dataDictionary)
    {
        _triggersPendingSync = false;  // reset before base call
        base.SetControlValue(dataDictionary);

        if (!TriggersFromFlowData || string.IsNullOrEmpty(TriggersDataName))
            return;
        if (!dataDictionary.ContainsKey(TriggersDataName))
            return;

        var raw = dataDictionary[TriggersDataName];
        PlatformEventTriggerInput[]? bindings =
            raw as PlatformEventTriggerInput[]
            ?? (raw as IEnumerable<PlatformEventTriggerInput>)?.ToArray();

        _currentTriggersJson = bindings is { Length: > 0 }
            ? JsonSerializer.Serialize(bindings.Select(t => new
              {
                  eventType      = (int)t.EventType,
                  folderIdFilter = t.FolderIdFilter,
                  keyFilters     = t.KeyFilters,
              }))
            : "[]";
        _triggersPendingSync = true;
    }

    protected override string GetValue() => "0";

    /// <summary>
    /// Receives the encoded payload from JS getValue():
    ///   "{counter}\x1F{EventType}\x1F{FolderId}\x1F{Keys}"
    /// Parses and stores the event scalar fields so ControlValues can return
    /// them to the framework immediately after this call returns.
    /// </summary>
    protected override bool SetValue(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            var parts = value.Split('\x1F');
            if (parts.Length >= 4)
            {
                _currentEventType = parts[1];
                _currentFolderId  = parts[2];
                _currentKeys      = parts[3];
            }
        }
        return true;
    }

    protected override string GetConvertedValue(object value) => value?.ToString() ?? string.Empty;


    // ── Input / output data declarations ──────────────────────────────────────
    // InputData serves two roles:
    //   • Items with isReadOnly: true  → visible in the AFF data explorer (outputs)
    //   • Items with isReadOnly: false → visible as inputs in the Set Control Value step

    public override DataDescription[] InputData
    {
        get
        {
            // Only settable inputs — appear as value inputs in the Set Control Value step.
            // The read-only event outputs (EventType, FolderId, Keys) are declared in
            // OutcomeScenarios so they appear as form outputs, not form inputs.
            var list = new List<DataDescription>();

            if (TriggersFromFlowData && !string.IsNullOrEmpty(TriggersDataName))
                list.Add(new DataDescription(typeof(PlatformEventTriggerInput), TriggersDataName, isList: true));
            if (EnabledTextFromDataName && !string.IsNullOrEmpty(EnabledTextDataName))
                list.Add(new DataDescription(typeof(string), EnabledTextDataName));
            if (DisabledTextFromDataName && !string.IsNullOrEmpty(DisabledTextDataName))
                list.Add(new DataDescription(typeof(string), DisabledTextDataName));

            return list.ToArray();
        }
    }

    // ── DataName ──────────────────────────────────────────────────────────────

    [ClientOption]
    [PropertyHidden]
    public override string DataName
    {
        get
        {
            var stored = base.DataName;
            return string.IsNullOrEmpty(stored)
                ? (string.IsNullOrEmpty(ComponentName) ? "PlatformEventTrigger" : ComponentName)
                : stored;
        }
        set => base.DataName = value;
    }

    // ── Hide irrelevant base properties ───────────────────────────────────────

    [PropertyHidden] public override bool IsDataNameRequired => false;
    [PropertyHidden] public override bool IsAttachedLabelProvider { get => false; set { } }
    [PropertyHidden] public override OutcomeScenario[] OutcomePathsWithOutcomeType
    {
        get => base.OutcomePathsWithOutcomeType;
        set => base.OutcomePathsWithOutcomeType = value;
    }
    [PropertyHidden] public override string DefaultValue   { get => base.DefaultValue;   set => base.DefaultValue = value; }
    [PropertyHidden] public override bool StaticInput      { get => base.StaticInput;     set => base.StaticInput = value; }
    [PropertyHidden] public override bool OverrideRequiredMessage { get => base.OverrideRequiredMessage; set => base.OverrideRequiredMessage = value; }
    [PropertyHidden] public override bool OutputOnly { get => true; set { } }

    public override OutcomeScenarioData[] OutcomeScenarios => new[]
    {
        new OutcomeScenarioData("Value Changed",
            new DataDescription(new DecisionsNativeType(typeof(string)), DataName + "_EventType", isList: false, canBeNull: true, isReadOnly: true)
                { DisplayName = "Event Type", ExcludeNameInTitle = true },
            new DataDescription(new DecisionsNativeType(typeof(string)), DataName + "_FolderId", isList: false, canBeNull: true, isReadOnly: true)
                { DisplayName = "Folder ID",  ExcludeNameInTitle = true },
            new DataDescription(new DecisionsNativeType(typeof(string)), DataName + "_Keys",      isList: false, canBeNull: true, isReadOnly: true)
                { DisplayName = "Keys",       ExcludeNameInTitle = true })
    };
}
