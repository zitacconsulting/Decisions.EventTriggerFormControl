using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DecisionsFramework.Data.ORMapper;
using DecisionsFramework;
using DecisionsFramework.ComponentData;
using Decisions.Silverlight.UI.Forms;
using DecisionsFramework.Design.Form;
using DecisionsFramework.Design.Report;
using DecisionsFramework.ServiceLayer;
using DecisionsFramework.ServiceLayer.Services.ClientEvents;
using DecisionsFramework.ServiceLayer.Services.ContextData;
using DecisionsFramework.ServiceLayer.Services.Folder;
using DecisionsFramework.ServiceLayer.Utilities;

namespace Decisions.EventTriggerFormControl;

// ── Registry entry ────────────────────────────────────────────────────────────

/// <summary>Snapshot of one control's trigger bindings held at session-start time.</summary>
internal sealed record SessionControlRegistration(
    string SessionId,
    string ControlComponentName,
    PlatformEventTriggerBinding[] Bindings,
    bool IsEnabled = true);

// ── Event processor decorator ─────────────────────────────────────────────────

/// <summary>
/// Wraps an existing IEventsProcessor (if any) so we never displace a prior
/// registration. We observe every platform event and forward unconditionally.
/// </summary>
internal sealed class PlatformEventProcessorDecorator : IEventsProcessor
{
    private readonly IEventsProcessor? _previous;
    private readonly PlatformEventFormTriggerService _service;

    public PlatformEventProcessorDecorator(
        IEventsProcessor? previous,
        PlatformEventFormTriggerService service)
    {
        _previous = previous;
        _service  = service;
    }

    public bool SendEvent(EventData data)
    {
        // Observe and handle — never block.
        _service.HandleEvent(data);

        // Forward to whatever was registered before us.
        return _previous?.SendEvent(data) ?? true;
    }
}

// ── Service ───────────────────────────────────────────────────────────────────

/// <summary>
/// Bridge between the Decisions platform event bus and Active Form Flows.
///
/// Lifecycle:
///   • Initialize() — wraps ClientEventsService.EventProcessor at boot.
///   • Register()   — called from PlatformEventTriggerControl.FormSessionInfoID setter
///                    when a form session starts.
///   • HandleEvent() — called by the decorator for every platform event.
///                    Matches events against registered bindings, resolves AFF IDs,
///                    and invokes FormService.Instance.RunRulesOrFlows().
/// </summary>
public sealed class PlatformEventFormTriggerService : IBootInitializable, IInitializable, IInializableOrder
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    private static readonly PlatformEventFormTriggerService _instance = new();
    public static PlatformEventFormTriggerService Instance => _instance;

    private static readonly Log _log = new("PlatformEventTrigger");

    // ── IInializableOrder ─────────────────────────────────────────────────────

    public int Ordinal => 10001;   // after control registrar

    public InitializableHost[] Environments => new[]
    {
        InitializableHost.Unmanaged,
        InitializableHost.User,
        InitializableHost.Control,
    };

    public InitializablePhase Phase => InitializablePhase.ApplicationBoot;

    public string Name => "Platform Event Form Trigger Service";

    // ── Startup ───────────────────────────────────────────────────────────────

    public void Initialize()
    {
        var existing = ClientEventsService.EventProcessor;
        ClientEventsService.EventProcessor =
            new PlatformEventProcessorDecorator(existing, Instance);

        _log.Debug("PlatformEventFormTriggerService initialized — event processor registered.");
    }

    // ── Registry ──────────────────────────────────────────────────────────────

    // Key: "{sessionId}::{controlComponentName}" — one entry per control instance per session.
    private readonly ConcurrentDictionary<string, SessionControlRegistration> _registry = new();

    // Key: "{sessionId}::{controlComponentName}::{eventType}" — last fire time for throttling.
    private readonly ConcurrentDictionary<string, DateTime> _lastFired = new();

    /// <summary>
    /// Called from PlatformEventTriggerControl.FormSessionInfoID when the form session starts.
    /// </summary>
    public void Register(
        string sessionId,
        string controlComponentName,
        PlatformEventTriggerBinding[] bindings)
    {
        if (string.IsNullOrEmpty(sessionId) || bindings.Length == 0)
            return;

        var key = RegistryKey(sessionId, controlComponentName);
        _registry[key] = new SessionControlRegistration(sessionId, controlComponentName, bindings);

        // Calling GetFormSessionInfo here (during IFormSessionInfoConsumer callback) triggers
        // recursive JSON deserialization and a stack overflow. Instead, defer the design-time
        // check until after session construction completes.
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            // Retry until the session is registered (construction usually completes in <100 ms).
            for (int i = 0; i < 20; i++)
            {
                await System.Threading.Tasks.Task.Delay(50);
                var s = FormService.GetFormSessionInfo(sessionId, throwIfSessionNotFound: false);
                if (s == null) continue;

                if (s.IsInDesignMode)
                {
                    Deregister(sessionId, controlComponentName);
                    _log.Debug($"[PET] Deregistered design-time session {sessionId} / control {controlComponentName}.");
                }
                else
                {
                    _log.Debug($"[PET] Registered {bindings.Length} binding(s): session={sessionId} control={controlComponentName} types=[{string.Join(",", bindings.Select(b => $"{b.EventType}:{b.FolderIdFilter ?? "*"}"))}]");
                }
                return;
            }
        });
    }

    /// <summary>
    /// Called from PlatformEventTriggerControl.SetValue when the user toggles
    /// auto-refresh on or off in the running form.
    /// </summary>
    public void SetEnabled(string sessionId, string controlName, bool enabled)
    {
        var key = RegistryKey(sessionId, controlName);
        if (_registry.TryGetValue(key, out var reg))
        {
            _registry[key] = reg with { IsEnabled = enabled };
            _log.Debug($"[PET] Auto-refresh {(enabled ? "enabled" : "disabled")} for session {sessionId} / control {controlName}.");
        }
    }

    private void Deregister(string sessionId, string controlComponentName)
    {
        _registry.TryRemove(RegistryKey(sessionId, controlComponentName), out _);

        // Remove all throttle entries for this control.
        var prefix = RegistryKey(sessionId, controlComponentName) + "::";
        foreach (var key in _lastFired.Keys.Where(k => k.StartsWith(prefix)).ToArray())
            _lastFired.TryRemove(key, out _);
    }

    private static string RegistryKey(string sessionId, string controlName)
        => $"{sessionId}::{controlName}";

    // ── Event handling ────────────────────────────────────────────────────────

    /// <summary>
    /// Called for every platform event passing through the event processor.
    /// Dispatches to the appropriate typed handler.
    /// </summary>
    internal void HandleEvent(EventData data)
    {
        var payload = data.Data?.OutputValue;
        if (payload == null) return;

        try
        {
            switch (payload)
            {
                case FolderChangedMessage msg:
                    HandleFolderChanged(msg);
                    break;

                case RefreshReportByFolderMessage msg:
                    HandleRefreshByFolder(msg);
                    break;

                case RefreshReportByKeysMessage msg:
                    HandleRefreshByKeys(msg);
                    break;

                case RefreshReportByFolderAndKeysMessage msg:
                    HandleRefreshByFolderAndKeys(msg);
                    break;

                case FolderEntitiesChangedMessage msg:
                    HandleContainedEntityChanged(msg);
                    HandleContainedEntityChangedInTree(msg);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in PlatformEventFormTriggerService.HandleEvent");
        }
    }

    // ── Typed handlers ────────────────────────────────────────────────────────

    private void HandleFolderChanged(FolderChangedMessage msg)
    {
        foreach (var (reg, binding) in MatchingRegistrations(
            b => b.EventType == PlatformEventType.FolderChanged &&
                 MatchesFolder(b.FolderIdFilter, msg.FolderId)))
        {
            FireAffs(reg, binding, PlatformEventType.FolderChanged,
                folderId: msg.FolderId, keys: null);
        }
    }

    private void HandleRefreshByFolder(RefreshReportByFolderMessage msg)
    {
        foreach (var (reg, binding) in MatchingRegistrations(
            b => b.EventType == PlatformEventType.RefreshByFolder &&
                 MatchesFolder(b.FolderIdFilter, msg.FolderId)))
        {
            FireAffs(reg, binding, PlatformEventType.RefreshByFolder,
                folderId: msg.FolderId, keys: null);
        }
    }

    private void HandleRefreshByKeys(RefreshReportByKeysMessage msg)
    {
        foreach (var (reg, binding) in MatchingRegistrations(
            b => b.EventType == PlatformEventType.RefreshByKey &&
                 MatchesKeys(b.KeyFilters, msg.Keys)))
        {
            FireAffs(reg, binding, PlatformEventType.RefreshByKey,
                folderId: null, keys: msg.Keys);
        }
    }

    private void HandleRefreshByFolderAndKeys(RefreshReportByFolderAndKeysMessage msg)
    {
        foreach (var (reg, binding) in MatchingRegistrations(
            b => b.EventType == PlatformEventType.RefreshByFolderAndKey &&
                 MatchesFolder(b.FolderIdFilter, msg.FolderId) &&
                 MatchesKeys(b.KeyFilters, msg.Keys)))
        {
            FireAffs(reg, binding, PlatformEventType.RefreshByFolderAndKey,
                folderId: msg.FolderId, keys: msg.Keys);
        }
    }

    private void HandleContainedEntityChanged(FolderEntitiesChangedMessage msg)
    {
        foreach (var (reg, binding) in MatchingRegistrations(
            b => b.EventType == PlatformEventType.ContainedEntityChanged &&
                 MatchesFolder(b.FolderIdFilter, msg.FolderId)))
        {
            FireAffs(reg, binding, PlatformEventType.ContainedEntityChanged,
                folderId: msg.FolderId, keys: null);
        }
    }

    private void HandleContainedEntityChangedInTree(FolderEntitiesChangedMessage msg)
    {
        foreach (var (reg, binding) in MatchingRegistrations(
            b => b.EventType == PlatformEventType.ContainedEntityChangedInTree &&
                 IsUnderFolder(b.FolderIdFilter, msg.FolderId)))
        {
            FireAffs(reg, binding, PlatformEventType.ContainedEntityChangedInTree,
                folderId: msg.FolderId, keys: null);
        }
    }

    // ── AFF execution ─────────────────────────────────────────────────────────

    private void FireAffs(
        SessionControlRegistration reg,
        PlatformEventTriggerBinding binding,
        PlatformEventType eventType,
        string? folderId,
        string[]? keys)
    {
        // ── Throttle check ────────────────────────────────────────────────────
        if (binding.MinimumRefreshInterval > TimeSpan.Zero)
        {
            var throttleKey = $"{RegistryKey(reg.SessionId, reg.ControlComponentName)}::{eventType}";
            var now = DateTime.UtcNow;
            if (_lastFired.TryGetValue(throttleKey, out var last) &&
                now - last < binding.MinimumRefreshInterval)
            {
                _log.Debug($"Throttled event {eventType} for session {reg.SessionId} / control {reg.ControlComponentName}.");
                return;
            }
            _lastFired[throttleKey] = now;
        }

        var session = FormService.GetFormSessionInfo(reg.SessionId, throwIfSessionNotFound: false);
        if (session == null)
        {
            _log.Debug($"[PET] Session {reg.SessionId} not found — deregistering control {reg.ControlComponentName}.");
            Deregister(reg.SessionId, reg.ControlComponentName);
            return;
        }

        // Never fire AFFs for Forms Designer sessions.
        if (session.IsInDesignMode)
        {
            _log.Debug($"[PET] Session {reg.SessionId} is a designer session — deregistering control {reg.ControlComponentName}.");
            Deregister(reg.SessionId, reg.ControlComponentName);
            return;
        }

        // Find AFFs configured with this control as their ValueChanged trigger.
        var allAffs = session.FormSurface.FormFlowData ?? Array.Empty<FormFlowData>();
        var ruleIds = allAffs
            .Where(aff => aff.Enabled &&
                aff.SelectedFormTriggers?.Any(t =>
                    t.ComponentName == reg.ControlComponentName &&
                    t.Events?.Contains(FormTriggerType.ValueChanged) == true) == true)
            .Select(aff => aff.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToArray();

        if (ruleIds == null || ruleIds.Length == 0)
        {
            _log.Debug($"[PET] No matching AFFs found for control={reg.ControlComponentName} on event={eventType}.");
            return;
        }

        _log.Debug($"[PET] Firing {ruleIds.Length} AFF(s) for session {reg.SessionId} on {eventType}: [{string.Join(",", ruleIds)}]");

        // Inject event context as transient form data — available to AFF flow
        // inputs but never written to the form's output data.
        var contextData = BuildContextData(eventType, folderId, keys);

        var ruleSession = new RunRuleSessionInfo
        {
            RuleIds     = ruleIds,
            ComponentId = session.FormSurface.RootContainer.ComponentId,
            SurfaceId   = session.FormSurface.RegistrationId,
        };

        // RunRulesOrFlows is public and carries [ForwardCallToFormSessionInfoOwner]
        // so it routes correctly in a clustered deployment.
        FormService.Instance.RunRulesOrFlows(
            new SystemUserContext(),
            reg.SessionId,
            ruleSession,
            "PLATFORM_EVENT_TRIGGER",
            contextData);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IEnumerable<(SessionControlRegistration reg, PlatformEventTriggerBinding binding)> MatchingRegistrations(
        Func<PlatformEventTriggerBinding, bool> bindingPredicate)
    {
        foreach (var entry in _registry.Values)
        {
            if (!entry.IsEnabled) continue;
            foreach (var binding in entry.Bindings.Where(bindingPredicate))
                yield return (entry, binding);
        }
    }

    private static bool MatchesFolder(string? filter, string? eventFolderId)
        => string.IsNullOrEmpty(filter) || filter == eventFolderId;

    /// <summary>
    /// Returns true when <paramref name="eventFolderId"/> is the configured folder
    /// itself OR any descendant of it, determined by comparing FullPath prefixes.
    /// </summary>
    private static bool IsUnderFolder(string? rootFolderIdFilter, string? eventFolderId)
    {
        if (string.IsNullOrEmpty(rootFolderIdFilter)) return true;
        if (string.IsNullOrEmpty(eventFolderId))      return false;
        if (rootFolderIdFilter == eventFolderId)       return true;

        try
        {
            var rootFolder  = new ORM<Folder>().Fetch(rootFolderIdFilter);
            var eventFolder = new ORM<Folder>().Fetch(eventFolderId);
            if (rootFolder?.FullPath == null || eventFolder?.FullPath == null) return false;

            // A descendant's FullPath starts with the root's FullPath followed by a separator.
            return eventFolder.FullPath.StartsWith(rootFolder.FullPath + "/",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _log.Warn(ex, $"IsUnderFolder: error checking {eventFolderId} under {rootFolderIdFilter}");
            return false;
        }
    }

    private static bool MatchesKeys(string[]? filters, string[]? eventKeys)
    {
        if (filters == null || filters.Length == 0) return true;
        if (eventKeys == null || eventKeys.Length == 0) return false;
        return filters.Any(f => eventKeys.Contains(f));
    }

    private static DataPair[] BuildContextData(
        PlatformEventType eventType,
        string? folderId,
        string[]? keys)
    {
        var list = new List<DataPair>
        {
            new("PlatformEvent_Type", eventType.ToString()),
        };
        if (!string.IsNullOrEmpty(folderId))
            list.Add(new DataPair("PlatformEvent_FolderId", folderId));
        if (keys?.Length > 0)
            list.Add(new DataPair("PlatformEvent_Keys", keys));
        return list.ToArray();
    }
}
