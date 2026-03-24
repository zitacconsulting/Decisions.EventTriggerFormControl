/**
 * $DP.Control.PlatformEventTriggerControl
 *
 * Renders as an "Auto Refresh: ON / OFF" toggle button.
 * Clicking the button toggles auto-refresh for this control instance:
 *   - ON  → platform events trigger the configured Active Form Flow
 *   - OFF → platform events are ignored until toggled back on
 *
 * Default appearance uses the standard Decisions event-button stylesheet
 * (classes: SilverButtonStyle full-size silver-eventbutton).
 * Per-state color/border overrides can be configured in the "View" property
 * panel and are applied as inline styles on top of the CSS defaults.
 *
 * State is exchanged with the server via the control's DataName ("1" = on, "0" = off).
 * The server-side PlatformEventFormTriggerService respects the IsEnabled flag before
 * firing any AFF.
 */

$DP = $DP || {};
$DP.Control = $DP.Control || {};

$DP.Control.PlatformEventTriggerControl = class PlatformEventTriggerControl extends $DP.Control.SilverPart {

    constructor($controlLayout, options) {
        super($controlLayout, options);
        this._enabled      = true;   // mirrors server-side _autoRefreshEnabled
        this._$btn         = null;
        this._$label       = null;
        this._enabledText  = null;   // resolved at runtime from data name, if configured
        this._disabledText = null;
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    renderhtml(host) {
        const $el = $('<div>')
            .addClass('platform-event-trigger-control')
            .css({ display: 'flex', alignItems: 'center',
                   width: '100%', height: '100%',
                   overflow: 'hidden', boxSizing: 'border-box' });

        // Outer wrapper — mirrors SilverEventButton's DOM structure so CSS rules apply.
        const $wrapper = $('<div>')
            .addClass('SilverButtonStyle full-size silver-eventbutton')
            .addClass(this.options?.cssClass || '')
            .css({ width: '100%', height: '100%' });

        // Inner button — receives .dp-form-button--outlined theme styles.
        this._$btn = $('<button>')
            .attr('type', 'button')
            .addClass('full-size dp-form-button--outlined buttonControl')
            .css({ width: '100%', height: '100%' });

        this._$label = $('<span>');
        this._$btn.append(this._$label);
        $wrapper.append(this._$btn);
        $el.append($wrapper);

        const toggle = () => {
            if ($DP?.FormDesigner?.IsActive || this.$controlLayout?.closest('.dp-form-designer')?.length) return;
            this._enabled = !this._enabled;
            this._updateUI();
            this.raiseEvent(new $DP.FormHost.DataChangedEvent());
        };
        this._$btn.on('click', toggle);

        this._updateUI();
        return $el;
    }

    getControl() {
        return this.$controlLayout.find('.platform-event-trigger-control');
    }

    _updateUI() {
        if (!this._$btn) return;
        const o = this.options || {};

        // Reset all inline overrides — let .dp-form-button--outlined CSS control defaults.
        this._$btn.css({
            background: '', color: '', borderColor: '', borderStyle: '',
            borderWidth: '', borderRadius: '', opacity: '',
        });

        const s = {};
        if (o.borderWidth  != null) s.borderWidth  = o.borderWidth  + 'px';
        if (o.cornerRadius != null) s.borderRadius = o.cornerRadius + 'px';

        if (this._enabled) {
            const label = this._enabledText
                || (o.enabledTextFromDataName
                    ? (o.enabledTextDataName ? '[' + o.enabledTextDataName + ']' : '[?]')
                    : (o.enabledLabel || '⚡ Auto Refresh: ON'));
            this._$label.text(label);
            if (o.enabledBackgroundColor) s.background  = o.enabledBackgroundColor;
            if (o.enabledBorderColor)     s.borderColor = o.enabledBorderColor;
            if (o.enabledTextColor)       s.color       = o.enabledTextColor;
        } else {
            const label = this._disabledText
                || (o.disabledTextFromDataName
                    ? (o.disabledTextDataName ? '[' + o.disabledTextDataName + ']' : '[?]')
                    : (o.disabledLabel || '⏸ Auto Refresh: OFF'));
            this._$label.text(label);
            s.opacity = '0.5';
            if (o.disabledBackgroundColor) s.background  = o.disabledBackgroundColor;
            if (o.disabledBorderColor)     s.borderColor = o.disabledBorderColor;
            if (o.disabledTextColor)       s.color       = o.disabledTextColor;
        }

        if (Object.keys(s).length) this._$btn.css(s);
    }

    // ── Data I/O ──────────────────────────────────────────────────────────────

    setValue(data, isFromStartUp) {
        const o = this.options || {};

        const items = Array.isArray(data) ? data
            : (typeof data?.toArray === 'function' ? data.toArray() : []);

        // Toggle state
        if (o.dataName) {
            const found = items.find(t => t.name === o.dataName);
            if (found?.value != null) {
                this._enabled = found.value !== '0';
            }
        }

        // Dynamic label texts
        if (o.enabledTextFromDataName && o.enabledTextDataName) {
            const found = items.find(t => t.name === o.enabledTextDataName);
            if (found?.value != null) this._enabledText = found.value;
        }
        if (o.disabledTextFromDataName && o.disabledTextDataName) {
            const found = items.find(t => t.name === o.disabledTextDataName);
            if (found?.value != null) this._disabledText = found.value;
        }

        this._updateUI();
    }

    getValue() {
        const dataName = this.options?.dataName;
        if (!dataName) return [];
        return [{ name: dataName, value: this._enabled ? '1' : '0' }];
    }

    resize(height, width) {
        if (this.$controlLayout) {
            this.$controlLayout.css({ width, height });
        }
    }
};
