/**
 * $DP.Control.PlatformEventTriggerControl
 *
 * Renders an ON/OFF toggle button in both design time and runtime.
 *   - Clicking toggles auto-refresh without firing any AFF.
 *   - Platform events only fire the configured AFF when the toggle is ON.
 *   - Toggle state is in-memory only; it resets to ON when the form reloads.
 *
 * To hide the button entirely set Initial Visibility → Hidden in Behaviour.
 *
 * options (from C# [ClientOption] properties):
 *   dataName                 {string}
 *   triggersJson             {string}  — JSON-serialized PlatformEventTriggerBinding[] (static)
 *   triggersFromFlowData     {boolean} — when true, triggers come from triggersDataName instead
 *   triggersDataName         {string}  — form data name carrying JSON trigger config at runtime
 *   minimumRefreshIntervalMs {number}
 *   fireOnReEnable           {boolean}
 *   enabledLabel             {string}
 *   enabledTextFromDataName  {boolean}
 *   enabledTextDataName      {string}
 *   disabledLabel            {string}
 *   disabledTextFromDataName {boolean}
 *   disabledTextDataName     {string}
 *   cssClass                 {string}
 *   enabledBackgroundColor   {string}
 *   enabledBorderColor       {string}
 *   enabledTextColor         {string}
 *   disabledBackgroundColor  {string}
 *   disabledBorderColor      {string}
 *   disabledTextColor        {string}
 *   borderWidth              {number}
 *   cornerRadius             {number}
 *
 * PlatformEventType enum values:
 *   0 = RefreshByFolder     → 'FolderEntitiesChangedMessage' (entity CRUD)
 *                             + 'ReportRefreshEvent' / RefreshReportByFolderMessage subtype
 *   1 = RefreshByKey        → 'ReportRefreshEvent' / RefreshReportByKeysMessage subtype
 *   2 = RefreshByFolderAndKey → 'ReportRefreshEvent' / RefreshReportByFolderAndKeysMessage subtype
 *
 * NOTE on ReportRefreshEvent: all three RefreshReportMessage subtypes are sent on
 * the same 'ReportRefreshEvent' channel. The __type field in the event detail
 * identifies the subtype. _matches() uses __type to route correctly.
 *
 * NOTE on FolderEntitiesChangedMessage: extends FolderMessage. EventIsForUser on
 * the server checks whether the event's FolderId is in the session's visible folder
 * list. showAdditionalFolder() registers our target folder so the check passes.
 * The form's own UpdateUserFolders calls can overwrite this registration, so we
 * re-register on a short interval. The interval is only started when there are
 * RefreshByFolder bindings with an explicit folderIdFilter — bindings without a
 * filter match any folder, so EventIsForUser returns true unconditionally.
 *
 * NOTE on SignalR reconnection: when SignalR reconnects, the server loses all
 * RegisterEvent subscriptions. We hook onreconnected to re-register immediately
 * using the stored handler references (no duplicate window listeners).
 *
 * Output data (accessible in the AFF after each firing):
 *   {DataName}             — incrementing counter (signals a new event)
 *   {DataName}_EventType   — PlatformEventType enum value (e.g. "RefreshByFolder")
 *   {DataName}_FolderId    — FolderId from the event payload (empty string if not applicable)
 *   {DataName}_Keys        — comma-separated keys from the event payload (empty string if none)
 */

$DP = $DP || {};
$DP.Control = $DP.Control || {};

$DP.Control.PlatformEventTriggerControl = class PlatformEventTriggerControl extends $DP.Control.SilverPart {

    constructor($controlLayout, options) {
        super($controlLayout, options);
        this._enabled      = true;   // toggle state (in-memory only)
        this._counter      = 0;      // incremented on each AFF fire
        this._subId             = null;
        this._lastFired         = 0;  // timestamp of last AFF firing (control-level throttle)
        this._$label            = null;
        this._enabledText       = null;   // resolved from form data if configured
        this._disabledText      = null;
        this._reregisterTimer   = null;
        this._eventHandlers     = new Map(); // event name → handler fn (for reconnect re-use)
        this._subscribedEvents  = null;
        this._registeredFolders = null;
        this._reconnectDisposer = null;     // SignalR onreconnected disposer
        this._currentTriggersJson = null;   // active trigger JSON when triggersFromFlowData is on
        // Last event scalars — returned by getValue() as plain strings.
        // _eventType is the PlatformEventType enum name (e.g. "RefreshByFolder") so the
        // framework's ChangeType can parse it back to the enum via Enum.Parse.
        this._lastEventType   = '';
        this._lastFolderId    = '';
        this._lastKeys        = '';
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    renderhtml(host) {
        // The framework sets options.isInDesignMode = true when rendering inside
        // the form designer. Use this flag — not DOM heuristics — to detect design time.
        const isDesign = !!this.options?.isInDesignMode;

        const $el = $('<div>').addClass('platform-event-trigger-control')
            .css({ width: '100%', height: '100%', boxSizing: 'border-box', overflow: 'hidden' });

        // Render the toggle button in both design and runtime modes so the designer
        // sees exactly how the button will look with the configured styles applied.
        const o = this.options || {};
        const $wrapper = $('<div>')
            .addClass('SilverButtonStyle full-size silver-eventbutton')
            .addClass(o.cssClass || '')
            .css({ width: '100%', height: '100%' });

        const $btn = $('<button>')
            .attr('type', 'button')
            .addClass('full-size dp-form-button--outlined buttonControl')
            .css({ width: '100%', height: '100%' });

        this._$label = $('<span>');
        $btn.append(this._$label);
        $wrapper.append($btn);
        $el.append($wrapper);

        if (!isDesign) {
            $btn.on('click', () => {
                this._enabled = !this._enabled;
                this._updateUI();
                // If configured, fire the AFF immediately when the user turns the
                // toggle ON. This is useful to trigger an immediate refresh after
                // re-enabling the control, rather than waiting for the next event.
                if (this._enabled && this.options?.fireOnReEnable) {
                    this._fireImmediate(null, null, true);
                }
            });

            // Subscribe to platform events if the hub is available.
            if (dpComponents?.EventsHub?.subscribeAndRegisterEvent) {
                this._subscribe();
                this.$controlLayout?.one('remove', () => this._unsubscribe());
            }
        }

        this._updateUI();

        return $el;
    }

    getControl() {
        return this.$controlLayout?.find('.platform-event-trigger-control');
    }

    resize(height, width) {
        this.$controlLayout?.css({ width, height });
    }

    // ── Data I/O ──────────────────────────────────────────────────────────────

    setValue(data, isFromStartUp) {
        const o = this.options || {};

        // ── DIAGNOSTIC ────────────────────────────────────────────────────────
        console.log('[PET] setValue type=' + typeof data + ' isArray=' + Array.isArray(data)
            + ' triggersFromFlowData=' + o.triggersFromFlowData
            + ' triggersDataName=' + o.triggersDataName
            + ' dataName=' + o.dataName);
        if (Array.isArray(data)) {
            data.forEach(function(item, i) {
                try { console.log('[PET] data[' + i + '] = ' + JSON.stringify(item)); }
                catch(e) { console.log('[PET] data[' + i + '] keys=' + Object.keys(item || {}).join(',')); }
            });
        } else {
            try { console.log('[PET] data = ' + JSON.stringify(data)); }
            catch(e) { console.log('[PET] data keys=' + Object.keys(data || {}).join(',')); }
        }
        // ─────────────────────────────────────────────────────────────────────

        // Resolve the primary DataName value from whatever format the framework
        // delivers.  SilverPart controls receive data in at least three formats:
        //   • raw string — the value for DataName directly
        //   • array of {name/Name, value/Value/outputValue/OutputValue} pairs
        //   • plain object keyed by data name
        const dn = o.dataName || '';
        let primary = '';
        if (typeof data === 'string') {
            primary = data;
        } else if (Array.isArray(data)) {
            const item = data.find(t =>
                (t.name ?? t.Name) === dn
            );
            primary = item?.value ?? item?.Value ?? item?.outputValue ?? item?.OutputValue ?? '';
        } else if (data != null && typeof data === 'object') {
            primary = data[dn] ?? '';
        }

        console.log('[PET] primary value =', JSON.stringify(primary));

        // Dynamic trigger configuration path A: when triggersFromFlowData is on,
        // the server encodes a trigger-JSON update as "TRIGGERS:{json}" in the
        // primary DataName DataPair (used by Set Control Value AFF step).
        if (o.triggersFromFlowData && typeof primary === 'string' && primary.startsWith('TRIGGERS:')) {
            const json = primary.slice('TRIGGERS:'.length);
            console.log('[PET] trigger update received (TRIGGERS: prefix), json =', json);
            if (json !== this._currentTriggersJson) {
                this._currentTriggersJson = json;
                this._reapplyTriggers();
            }
            return; // trigger update only — no UI change needed
        }

        // Dynamic trigger configuration path B: on form startup the framework
        // sends raw form input data directly to setValue() without going through
        // SetControlValue() → ControlValues. Scan the data array for triggersDataName
        // and treat it as a trigger update when found.
        if (o.triggersFromFlowData && o.triggersDataName && Array.isArray(data)) {
            const trigItem = data.find(t =>
                (t.name ?? t.Name) === o.triggersDataName
            );
            if (trigItem != null) {
                const rawVal = trigItem.value ?? trigItem.Value
                            ?? trigItem.outputValue ?? trigItem.OutputValue;
                if (rawVal != null) {
                    let json;
                    try { json = typeof rawVal === 'string' ? rawVal : JSON.stringify(rawVal); }
                    catch(e) { json = '[]'; }
                    console.log('[PET] trigger update received (form input), json =', json);
                    if (json !== this._currentTriggersJson) {
                        this._currentTriggersJson = json;
                        this._reapplyTriggers();
                    }
                    return; // trigger update only — no UI change needed
                }
            }
        }

        this._updateUI();
    }

    getValue() {
        const dataName = this.options?.dataName;
        if (!dataName) return [];
        // Encode all event scalars into the primary value separated by ASCII unit
        // separator (\x1F). C# SetValue() parses this and stores the parts as
        // instance variables; ControlValues() then returns them so the framework
        // writes them into formDataDictionary before the AFF reads them.
        const encoded = [
            String(this._counter),
            this._lastEventType,
            this._lastFolderId,
            this._lastKeys,
        ].join('\x1F');
        return [{ name: dataName, value: encoded }];
    }

    // ── Toggle button appearance ───────────────────────────────────────────────

    _updateUI() {
        if (!this._$label) return;
        const o = this.options || {};
        const $btn = this._$label.closest('button');

        // Reset inline overrides — let CSS control defaults.
        $btn.css({ background: '', color: '', borderColor: '', borderStyle: '',
                   borderWidth: '', borderRadius: '', opacity: '' });

        const s = {};
        if (o.borderWidth  != null) s.borderWidth  = o.borderWidth  + 'px';
        if (o.cornerRadius != null) s.borderRadius = o.cornerRadius + 'px';

        if (this._enabled) {
            const label = this._enabledText
                || (o.enabledTextFromDataName ? (o.enabledTextDataName ? '[' + o.enabledTextDataName + ']' : '[?]')
                    : (o.enabledLabel || '⚡ Auto Refresh: ON'));
            this._$label.text(label);
            if (o.enabledBackgroundColor) s.background  = o.enabledBackgroundColor;
            if (o.enabledBorderColor)     s.borderColor = o.enabledBorderColor;
            if (o.enabledTextColor)       s.color       = o.enabledTextColor;
        } else {
            const label = this._disabledText
                || (o.disabledTextFromDataName ? (o.disabledTextDataName ? '[' + o.disabledTextDataName + ']' : '[?]')
                    : (o.disabledLabel || '⏸ Auto Refresh: OFF'));
            this._$label.text(label);
            s.opacity = '0.5';
            if (o.disabledBackgroundColor) s.background  = o.disabledBackgroundColor;
            if (o.disabledBorderColor)     s.borderColor = o.disabledBorderColor;
            if (o.disabledTextColor)       s.color       = o.disabledTextColor;
        }

        if (Object.keys(s).length) $btn.css(s);
    }

    // ── Event subscriptions ───────────────────────────────────────────────────

    _parseTriggers() {
        // When triggersFromFlowData is on, use only the runtime JSON from setValue().
        // If no dynamic config has arrived yet, return empty — never fall back to static.
        const o = this.options || {};
        const json = o.triggersFromFlowData
            ? (this._currentTriggersJson ?? '[]')
            : (o.triggersJson || '[]');
        try {
            const raw = JSON.parse(json);
            // Normalize to camelCase so the rest of the code is consistent.
            // Static triggersJson uses camelCase (explicit anonymous type in C#).
            // Dynamic data from Set Control Value uses PascalCase (Decisions serializer).
            return raw.map(t => ({
                eventType:      t.eventType      ?? t.EventType      ?? 0,
                folderIdFilter: t.folderIdFilter ?? t.FolderIdFilter ?? null,
                keyFilters:     t.keyFilters     ?? t.KeyFilters     ?? null,
            }));
        } catch { return []; }
    }

    _subscribe() {
        if (!dpComponents?.EventsHub?.subscribeAndRegisterEvent) return;

        this._subId = dpComponents.Utils?.ClientEventUtils?.getUniqueComponentId?.('PET') ||
                      ('PET_' + Math.random().toString(36).slice(2, 10));

        this._applySubscriptions();

        // SignalR reconnection: when the connection drops and reconnects the server
        // loses all RegisterEvent subscriptions. Re-register immediately on reconnect
        // using the stored handler references so no duplicate window listeners are created.
        const conn = dpComponents.EventsHub?.connection;
        if (conn?.onreconnected) {
            this._reconnectDisposer = conn.onreconnected(() => {
                if (!this._subId) return; // already unsubscribed
                for (const [name, handler] of this._eventHandlers) {
                    dpComponents.EventsHub.subscribeAndRegisterEvent(name, this._subId, handler);
                }
                this._reregisterFolders();
            });
        }
    }

    _applySubscriptions() {
        const triggers = this._parseTriggers();
        console.log('[PET] _applySubscriptions, triggers =', JSON.stringify(triggers));
        if (!triggers.length) return;

        this._subscribedEvents = new Set();
        this._eventHandlers    = new Map();

        // Group bindings by window event name. A single binding may listen to
        // multiple event names (e.g. RefreshByFolder listens to both
        // FolderEntitiesChangedMessage and ReportRefreshEvent).
        const eventMap = new Map();
        for (const t of triggers) {
            for (const name of this._eventNamesForBinding(t)) {
                if (!eventMap.has(name)) eventMap.set(name, []);
                eventMap.get(name).push(t);
            }
        }

        for (const [name, bindings] of eventMap) {
            this._subscribedEvents.add(name);
            // Store the handler so reconnect logic can re-register it without
            // creating a new closure — this prevents duplicate window listeners.
            const handler = ({ detail }) => {
                if (!this._enabled) return;
                for (const t of bindings) {
                    if (this._matches(t, detail)) this._fire(t, detail);
                }
            };
            this._eventHandlers.set(name, handler);
            dpComponents.EventsHub.subscribeAndRegisterEvent(name, this._subId, handler);
        }

        // FolderEntitiesChangedMessage extends FolderMessage. Before delivering it
        // to a client session, the server checks whether the event's FolderId is in
        // that session's registered folder list. showAdditionalFolder() adds our
        // folder to that list via a SignalR UpdateUserFolders call.
        //
        // The problem: every time an AFF response is processed, the form sends its
        // own UpdateUserFolders call to sync the navigation panel's folder list with
        // the server — and that call does not include our folder, so our registration
        // gets overwritten. The periodic re-registration below re-adds our folder
        // within a few seconds after any such overwrite.
        //
        // showAdditionalFolder() reads the current folder list from the local store
        // and appends our folder, so the form's own folders are preserved. There is
        // a narrow race window where both calls read the same stale state simultaneously,
        // but either side will self-correct on its next update.
        //
        // The timer is only started when there are RefreshByFolder bindings with an
        // explicit folderIdFilter — bindings without a filter do not need it because
        // they match any folder, so EventIsForUser returns true unconditionally.
        this._registeredFolders = new Set();
        for (const t of triggers) {
            if (t.eventType === 0 && t.folderIdFilter) {
                this._registeredFolders.add(t.folderIdFilter);
            }
        }
        if (this._registeredFolders.size > 0) {
            this._reregisterFolders();
            this._reregisterTimer = setInterval(() => this._reregisterFolders(), 3000);
        }
    }

    // Re-subscribe when trigger configuration changes at runtime (triggersFromFlowData).
    _reapplyTriggers() {
        console.log('[PET] _reapplyTriggers called, subId =', this._subId, 'json =', this._currentTriggersJson);
        if (!this._subId) return; // not subscribed yet (hub not ready); _subscribe() will pick up the stored JSON

        // Unregister old event subscriptions from the hub and clear folder registrations.
        if (this._subscribedEvents) {
            for (const name of this._subscribedEvents) {
                dpComponents.EventsHub.unsubscribeFromEvent?.(name, this._subId);
            }
            this._subscribedEvents = null;
            this._eventHandlers    = new Map();
        }
        if (this._registeredFolders) {
            for (const folderId of this._registeredFolders) {
                dpComponents.closeAdditionalFolder?.(folderId);
            }
            this._registeredFolders = null;
        }
        if (this._reregisterTimer) {
            clearInterval(this._reregisterTimer);
            this._reregisterTimer = null;
        }

        this._applySubscriptions();
    }

    _reregisterFolders() {
        for (const folderId of (this._registeredFolders || [])) {
            dpComponents.showAdditionalFolder?.(folderId);
        }
    }

    _unsubscribe() {
        if (this._reregisterTimer) {
            clearInterval(this._reregisterTimer);
            this._reregisterTimer = null;
        }
        if (typeof this._reconnectDisposer === 'function') {
            this._reconnectDisposer();
            this._reconnectDisposer = null;
        }
        if (!this._subId || !this._subscribedEvents) return;
        for (const name of this._subscribedEvents) {
            dpComponents.EventsHub.unsubscribeFromEvent?.(name, this._subId);
        }
        for (const folderId of (this._registeredFolders || [])) {
            dpComponents.closeAdditionalFolder?.(folderId);
        }
        this._subId            = null;
        this._subscribedEvents = null;
        this._eventHandlers    = new Map();
        this._registeredFolders = null;
    }

    // ── Event matching ────────────────────────────────────────────────────────

    _eventNamesForBinding(t) {
        switch (t.eventType) {
            case 0: // RefreshByFolder — entity CRUD events AND explicit folder refresh signals
                return ['FolderEntitiesChangedMessage', 'ReportRefreshEvent'];
            case 1: // RefreshByKey
            case 2: // RefreshByFolderAndKey
                return ['ReportRefreshEvent'];
            default:
                return [];
        }
    }

    _matches(t, detail) {
        // __type identifies the concrete message class. All three RefreshReport
        // subtypes share the ReportRefreshEvent channel, so we use __type to
        // distinguish them. FolderEntitiesChangedMessage has its own channel.
        const type = detail?.__type || '';

        switch (t.eventType) {
            case 0: // RefreshByFolder
                // Accept entity CRUD events (FolderEntitiesChangedMessage) OR
                // explicit folder refresh signals (RefreshReportByFolderMessage).
                if (!type.includes('FolderEntitiesChangedMessage') &&
                    !type.includes('RefreshReportByFolderMessage')) return false;
                return !t.folderIdFilter || detail?.FolderId === t.folderIdFilter;

            case 1: // RefreshByKey
                if (!type.includes('RefreshReportByKeysMessage')) return false;
                return !t.keyFilters?.length ||
                       t.keyFilters.some(k => detail?.Keys?.includes(k));

            case 2: // RefreshByFolderAndKey
                if (!type.includes('RefreshReportByFolderAndKeysMessage')) return false;
                const hasKeyMatch = !t.keyFilters?.length ||
                                    t.keyFilters.some(k => detail?.Keys?.includes(k));
                return (!t.folderIdFilter || detail?.FolderId === t.folderIdFilter) &&
                       hasKeyMatch;

            default:
                return false;
        }
    }

    // ── AFF trigger ───────────────────────────────────────────────────────────

    _fire(trigger, detail) {
        // Throttle: check how long ago this control last fired anything.
        // The interval is control-level — any event from any binding counts,
        // so rapid events from multiple bindings don't cause repeated AFF runs.
        // If the interval hasn't elapsed yet, silently drop this firing.
        const minInterval = this.options?.minimumRefreshIntervalMs ?? 0;
        if (minInterval > 0) {
            const now = Date.now();
            if (this._lastFired && now - this._lastFired < minInterval) return;
            this._lastFired = now;
        }

        this._fireImmediate(trigger, detail);
    }

    _fireImmediate(trigger, detail, reEnable = false) {
        // Store event scalars as strings.
        // reEnable=true  → EventType = 'ReEnable' (toggle turned ON, no event context)
        // reEnable=false → EventType = PlatformEventType enum name ('RefreshByFolder' etc.)
        const eventTypeNames = { 0: 'RefreshByFolder', 1: 'RefreshByKey', 2: 'RefreshByFolderAndKey' };
        this._lastEventType = reEnable ? 'ReEnable'
            : (trigger != null ? (eventTypeNames[trigger.eventType] ?? '') : '');
        this._lastFolderId  = detail?.FolderId ?? '';
        this._lastKeys      = Array.isArray(detail?.Keys) ? detail.Keys.join(',') : '';

        // Increment the counter and raise a DataChanged event. The form framework
        // sees the value change and fires any AFF wired to this control's
        // "Value Changed" event. The counter itself is not meaningful — it just
        // ensures the value is always "new" so the framework never suppresses the event.
        this._counter++;
        this.raiseEvent(new $DP.FormHost.DataChangedEvent());
    }
};
