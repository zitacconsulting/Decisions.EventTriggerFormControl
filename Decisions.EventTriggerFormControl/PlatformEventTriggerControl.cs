using System;
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
using Silverdark.Components;

namespace Decisions.EventTriggerFormControl;

// ── Initializer ──────────────────────────────────────────────────────────────

/// <summary>
/// Registers PlatformEventTriggerControl in the Forms Designer toolbox.
/// Runs once at application boot — no restart needed after first module install.
/// </summary>
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

/// <summary>
/// Signals ValueChanged to the form engine when a platform event fires.
/// Implemented here because DataChangedServerEvent is internal to the framework.
/// </summary>
internal sealed class PlatformEventValueChangedEvent : IDecisionsControlServerEvent
{
    public string EventName => "Value Changed";
    public FormTriggerType? Event => FormTriggerType.ValueChanged;
}

// ── Control ───────────────────────────────────────────────────────────────────

/// <summary>
/// Invisible form control that bridges server-side platform events (folder changes,
/// report refresh events) to Active Form Flows configured in the Logic tab.
///
/// Usage in the Form Designer:
///   1. Drag this control onto the form canvas (it renders as an empty placeholder).
///   2. In the property panel configure one or more Platform Event Triggers,
///      specifying the event type, optional folder/key filters.
///   3. In the Logic tab, create an AFF and set this control as the trigger
///      (it appears as "Platform Event Trigger → Value Changed").
///   4. When the matching platform event fires at runtime the AFF executes.
///      The flow receives PlatformEvent_Type, PlatformEvent_FolderId, and
///      PlatformEvent_Keys as injected input data.
///
/// The control produces no form output data — it is purely a trigger carrier.
/// </summary>
[Writable]
public class PlatformEventTriggerControl
    : DataContentBase<PlatformEventTriggerControl, string>,
      IFormSessionInfoConsumer
{
    public PlatformEventTriggerControl()
    {
        // Give the control a visible footprint in the Forms Designer canvas so it
        // can be selected and its context menu (Delete, Copy, etc.) functions normally.
        RequestedWidth  = 200.0;
        RequestedHeight = 32.0;
    }

    // Whether auto-refresh is currently enabled for this control instance.
    // Toggled by the user at runtime via the toggle button in the form.
    private bool _autoRefreshEnabled = true;

    // Backing field for IFormSessionInfoConsumer.
    private string? _formSessionInfoId;

    // ── IFormSessionInfoConsumer ──────────────────────────────────────────────

    /// <summary>
    /// Set by FormSessionInfo during form session construction.
    /// Registers this control + its trigger bindings with the bridge service.
    /// </summary>
    // Used by the service to skip registration/firing in designer sessions.
    // Not exposed as [ClientOption] — Surface access from the serializer causes stack overflows.
    internal bool IsDesignTime => Surface?.IsDesignTime ?? false;

    [PropertyHidden]
    public string FormSessionInfoID
    {
        get => _formSessionInfoId ?? string.Empty;
        set
        {
            _formSessionInfoId = value;
            if (!string.IsNullOrEmpty(value))
                PlatformEventFormTriggerService.Instance.Register(
                    value,
                    ComponentName,
                    _triggers ?? Array.Empty<PlatformEventTriggerBinding>());
        }
    }

    // ── Informational hint ────────────────────────────────────────────────────

    [InfoOrWarningEditor(false, null, true)]
    [PropertyClassification(100, "Visibility", "Common Properties")]
    public string VisibilityTip
    {
        get => "To hide this control while keeping triggers active, set \"Initial Visibility\" to Hidden in the Behaviour settings.";
        set { }
    }

    // ── Button labels ─────────────────────────────────────────────────────────

    private string _enabledLabel = "⚡ Auto Refresh: ON";
    [ClientOption]
    [WritableValue]
    [PropertyClassification(105, "Enabled Label", "Common Properties")]
    [PropertyHiddenByValue("EnabledTextFromDataName", true, true)]
    public string EnabledLabel
    {
        get => _enabledLabel;
        set { _enabledLabel = value; OnPropertyChanged(nameof(EnabledLabel)); }
    }

    private bool _enabledTextFromDataName;
    [ClientOption]
    [WritableValue]
    [PropertyClassification(106, "Enabled Text from Data Name", "Common Properties")]
    public bool EnabledTextFromDataName
    {
        get => _enabledTextFromDataName;
        set
        {
            _enabledTextFromDataName = value;
            OnPropertyChanged(nameof(EnabledTextFromDataName));
        }
    }

    private string? _enabledTextDataName;
    [ClientOption]
    [WritableValue]
    [PropertyClassification(107, "Enabled Text Data Name", "Common Properties")]
    [PropertyHiddenByValue("EnabledTextFromDataName", true, false)]
    [FormDataPickerEditor(false)]
    public string? EnabledTextDataName
    {
        get => _enabledTextDataName;
        set { _enabledTextDataName = value; OnPropertyChanged(nameof(EnabledTextDataName)); }
    }

    private string _disabledLabel = "⏸ Auto Refresh: OFF";
    [ClientOption]
    [WritableValue]
    [PropertyClassification(108, "Disabled Label", "Common Properties")]
    [PropertyHiddenByValue("DisabledTextFromDataName", true, true)]
    public string DisabledLabel
    {
        get => _disabledLabel;
        set { _disabledLabel = value; OnPropertyChanged(nameof(DisabledLabel)); }
    }

    private bool _disabledTextFromDataName;
    [ClientOption]
    [WritableValue]
    [PropertyClassification(109, "Disabled Text from Data Name", "Common Properties")]
    public bool DisabledTextFromDataName
    {
        get => _disabledTextFromDataName;
        set
        {
            _disabledTextFromDataName = value;
            OnPropertyChanged(nameof(DisabledTextFromDataName));
        }
    }

    private string? _disabledTextDataName;
    [ClientOption]
    [WritableValue]
    [PropertyClassification(110, "Disabled Text Data Name", "Common Properties")]
    [PropertyHiddenByValue("DisabledTextFromDataName", true, false)]
    [FormDataPickerEditor(false)]
    public string? DisabledTextDataName
    {
        get => _disabledTextDataName;
        set { _disabledTextDataName = value; OnPropertyChanged(nameof(DisabledTextDataName)); }
    }

    // ── Trigger configuration ─────────────────────────────────────────────────

    /// <summary>
    /// One entry per distinct platform event scenario you want to react to.
    /// Configured in the control's property panel in the Form Designer.
    /// </summary>
    private PlatformEventTriggerBinding[]? _triggers;

    [WritableValue]
    [PropertyClassification(115, "Platform Event Triggers", "Common Properties")]
    public PlatformEventTriggerBinding[]? Triggers
    {
        get
        {
            // Propagate Surface into every binding before the sub-editor opens,
            // so the folder picker can resolve the current project's folders.
            if (_triggers != null && Surface != null)
                foreach (var b in _triggers) b.Surface = Surface;
            return _triggers;
        }
        set
        {
            _triggers = value;
            if (_triggers != null && Surface != null)
                foreach (var b in _triggers) b.Surface = Surface;
            OnPropertyChanged(nameof(Triggers));
        }
    }

    // ── DataName for toggle state exchange ────────────────────────────────────

    [PropertyHidden] public override bool IsDataNameRequired => false;

    // Disable the attached label — this control is self-labelling via the button text.
    [PropertyHidden]
    public override bool IsAttachedLabelProvider { get => false; set { } }

    // Hide the Outcome Scenarios panel — this control has no output paths.
    [PropertyHidden]
    public override OutcomeScenario[] OutcomePathsWithOutcomeType
    {
        get => base.OutcomePathsWithOutcomeType;
        set => base.OutcomePathsWithOutcomeType = value;
    }

    /// <summary>
    /// The form data key used to exchange the auto-refresh toggle state with the client.
    /// Defaults to ComponentName + "_enabled". Change this only if it conflicts with
    /// another control's data name on the same form.
    /// </summary>
    [WritableValue]
    [PropertyHidden]
    public override string DataName
    {
        get
        {
            var stored = base.DataName;
            return string.IsNullOrEmpty(stored)
                ? (string.IsNullOrEmpty(ComponentName) ? "PlatformEventTrigger_enabled" : ComponentName + "_enabled")
                : stored;
        }
        set => base.DataName = value;
    }

    // ── View / appearance overrides ───────────────────────────────────────────

    private string? _cssClass;
    [ClientOption] [WritableValue]
    [PropertyClassification(190, "Css Class", "View")]
    [CSSClassPickerEditor("CssClassList")]
    public string? CssClass
    {
        get => _cssClass;
        set { _cssClass = value; OnPropertyChanged(nameof(CssClass)); }
    }

    private string? _enabledBackgroundColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(200, "Enabled Background Color", "View")]
    [ColorPickerEditor]
    public string? EnabledBackgroundColor
    {
        get => _enabledBackgroundColor;
        set { _enabledBackgroundColor = value; OnPropertyChanged(nameof(EnabledBackgroundColor)); }
    }

    private string? _enabledBorderColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(201, "Enabled Border Color", "View")]
    [ColorPickerEditor]
    public string? EnabledBorderColor
    {
        get => _enabledBorderColor;
        set { _enabledBorderColor = value; OnPropertyChanged(nameof(EnabledBorderColor)); }
    }

    private string? _enabledTextColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(202, "Enabled Text Color", "View")]
    [ColorPickerEditor]
    public string? EnabledTextColor
    {
        get => _enabledTextColor;
        set { _enabledTextColor = value; OnPropertyChanged(nameof(EnabledTextColor)); }
    }

    private string? _disabledBackgroundColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(210, "Disabled Background Color", "View")]
    [ColorPickerEditor]
    public string? DisabledBackgroundColor
    {
        get => _disabledBackgroundColor;
        set { _disabledBackgroundColor = value; OnPropertyChanged(nameof(DisabledBackgroundColor)); }
    }

    private string? _disabledBorderColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(211, "Disabled Border Color", "View")]
    [ColorPickerEditor]
    public string? DisabledBorderColor
    {
        get => _disabledBorderColor;
        set { _disabledBorderColor = value; OnPropertyChanged(nameof(DisabledBorderColor)); }
    }

    private string? _disabledTextColor;
    [ClientOption] [WritableValue]
    [PropertyClassification(212, "Disabled Text Color", "View")]
    [ColorPickerEditor]
    public string? DisabledTextColor
    {
        get => _disabledTextColor;
        set { _disabledTextColor = value; OnPropertyChanged(nameof(DisabledTextColor)); }
    }

    private int? _borderWidth;
    /// <summary>Border thickness in pixels. Leave empty to use the CSS class default.</summary>
    [ClientOption] [WritableValue]
    [PropertyClassification(220, "Border Width (px)", "View")]
    public int? BorderWidth
    {
        get => _borderWidth;
        set { _borderWidth = value; OnPropertyChanged(nameof(BorderWidth)); }
    }

    private int? _cornerRadius;
    /// <summary>Corner radius in pixels. Leave empty to use the CSS class default.</summary>
    [ClientOption] [WritableValue]
    [PropertyClassification(221, "Corner Radius (px)", "View")]
    public int? CornerRadius
    {
        get => _cornerRadius;
        set { _cornerRadius = value; OnPropertyChanged(nameof(CornerRadius)); }
    }

    // ── Hide remaining irrelevant DataContentBase properties ──────────────────

    [PropertyHidden] public override string DefaultValue { get => base.DefaultValue; set => base.DefaultValue = value; }
    [PropertyHidden] public override bool StaticInput { get => base.StaticInput; set => base.StaticInput = value; }
    [PropertyHidden] public override bool OverrideRequiredMessage { get => base.OverrideRequiredMessage; set => base.OverrideRequiredMessage = value; }
    [PropertyHidden] public override bool OutputOnly { get => base.OutputOnly; set => base.OutputOnly = value; }

    // ── ISilverFormEventsProvider (via DataContentBase) ───────────────────────

    /// <summary>
    /// Declares ValueChanged so this control appears in the AFF trigger picker
    /// in the Logic tab — identical to any field-based trigger.
    /// </summary>
    public override FormTriggerType[] ProvidedEvents => new[] { FormTriggerType.ValueChanged };

    // ── Suppress all data I/O ─────────────────────────────────────────────────
    // The control intentionally produces no form input or output data.

    public override DataDescription[] InputData
    {
        get
        {
            var list = new System.Collections.Generic.List<DataDescription>();
            if (EnabledTextFromDataName && !string.IsNullOrEmpty(EnabledTextDataName))
                list.Add(new DataDescription(typeof(string), EnabledTextDataName));
            if (DisabledTextFromDataName && !string.IsNullOrEmpty(DisabledTextDataName))
                list.Add(new DataDescription(typeof(string), DisabledTextDataName));
            return list.ToArray();
        }
    }
    public override OutcomeScenarioData[] OutcomeScenarios => Array.Empty<OutcomeScenarioData>();

    // Expose the toggle state as control data so the JS and server stay in sync.
    public override DataPair[] ControlValues =>
        new[] { new DataPair(DataName, _autoRefreshEnabled ? "1" : "0") };

    protected override string GetValue() => _autoRefreshEnabled ? "1" : "0";

    protected override bool SetValue(string value)
    {
        var newEnabled = value != "0";
        if (newEnabled == _autoRefreshEnabled) return false;
        _autoRefreshEnabled = newEnabled;

        // Propagate to the service so the next event checks the updated flag.
        if (!string.IsNullOrEmpty(_formSessionInfoId))
            PlatformEventFormTriggerService.Instance.SetEnabled(
                _formSessionInfoId, ComponentName, _autoRefreshEnabled);

        return true;
    }

    protected override string GetConvertedValue(object value) => value?.ToString() ?? string.Empty;

    // ── ConsumeData ───────────────────────────────────────────────────────────

    public override void ConsumeData(System.Collections.Generic.IDictionary<string, object> data)
    {
        base.ConsumeData(data);   // reads DataName key → calls SetValue if found
    }
}
