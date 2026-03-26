# Event Trigger Form Control

> ⚠️ **Important:** Use this module at your own risk. See the **Disclaimer** section below.

A custom form control module for the [Decisions](https://decisions.com) platform that bridges server-side platform events to Active Form Flows (AFF). Place the control on a form, configure which platform events to listen for, and wire up an AFF in the Logic tab — the flow fires automatically when a matching event occurs.

## Features

- **Platform event → AFF bridge** — connects server-side platform events (entity changes, report refreshes) directly to Active Form Flows without polling.
- **Toggle button UI** — renders as an ON/OFF button so users can pause and resume event-driven refresh without leaving the form.
- **Multiple trigger bindings** — one control can listen for multiple distinct events, each with independent folder/key filters and throttle intervals.
- **Dynamic button labels** — button text for each state can be a static string or pulled from a form data name at runtime.
- **Full appearance control** — per-state background, border and text color overrides, plus border width, corner radius, and a CSS class picker that integrates with the project stylesheet.

## Requirements

- Decisions 9.21 or later

## Installation

### Option 1: Install Pre-built Module
1. Download the compiled module (`.zip` file)
2. Log into the Decisions Portal
3. Navigate to **System > Administration > Features**
4. Click **Install Module**
5. Upload the `.zip` file
6. Restart the Decisions service if prompted

### Option 2: Build from Source
See the [Building from Source](#building-from-source) section below.

## Configuration

Once installed, the **Event Trigger** control appears in the form toolbox under *Trigger*.

### Common Properties

| Property | Description |
|---|---|
| Minimum Refresh Interval (ms) | Minimum time between successive firings, regardless of which binding triggered. Set to 0 (default) to fire every time. |
| Fire When Enabled | When ticked, the AFF fires immediately when the toggle transitions from OFF to ON. |
| Set Triggers From Flow Data | When ticked, hides the static *Platform Event Triggers* list and reads the trigger configuration from a form data name at runtime. Allows an AFF to change what the control listens for via *Set Control Value* without reloading the form. |
| Triggers Data Name | The form data name that carries the trigger configuration when *Set Triggers From Flow Data* is ticked. The value is typed as `PlatformEventTriggerBinding[]` — the AFF can map a flow variable of that type directly via *Set Control Value*. Only visible when *Set Triggers From Flow Data* is ticked. |
| Enabled Label | Button text shown when the trigger is active (ON state). Defaults to `⚡ Auto Refresh: ON`. |
| Enabled Text from Data Name | When ticked, the ON-state label is read from a form data name at runtime instead of the static text. |
| Enabled Text Data Name | The form data name to read the ON-state label from. Shown only when *Enabled Text from Data Name* is ticked. |
| Disabled Label | Button text shown when the trigger is paused (OFF state). Defaults to `⏸ Auto Refresh: OFF`. |
| Disabled Text from Data Name | When ticked, the OFF-state label is read from a form data name at runtime. |
| Disabled Text Data Name | The form data name to read the OFF-state label from. Shown only when *Disabled Text from Data Name* is ticked. |
| Platform Event Triggers | List of trigger bindings — one per event scenario. Each binding configures event type and folder/key filters. |

### Trigger Binding Properties

Each entry in *Platform Event Triggers* has the following settings:

| Property | Description |
|---|---|
| Event Type | The platform event to listen for (see Event Types below). |
| Folder Filter | Optional. Only fire when the event's folder matches this folder. Leave empty to match any folder. |
| Key Filters | Optional. Only fire when the event's keys contain at least one of these values. Leave empty to match any keys. |

#### Event Types

| Event Type | When it fires |
|---|---|
| `RefreshByFolder` | An entity in the configured folder was created, updated, or deleted — OR a flow explicitly sends a folder refresh signal for that folder. |
| `RefreshByKey` | A flow sends a named refresh signal matching one of the configured keys. |
| `RefreshByFolderAndKey` | A flow sends a named refresh signal matching both the configured folder and at least one of the configured keys. |

All three event types support user- and group-targeted variants from the flow toolbox (e.g. *Send Refresh Report By Folder Event For User*). Targeted events are filtered server-side — the control only fires if the current user matches the target.

### View Properties

| Property | Description |
|---|---|
| Css Class | CSS class from the project stylesheet to apply to the button wrapper. |
| Enabled Background Color | Background color override for the ON state. Leave empty to use the stylesheet default. |
| Enabled Border Color | Border color override for the ON state. |
| Enabled Text Color | Text color override for the ON state. |
| Disabled Background Color | Background color override for the OFF state. |
| Disabled Border Color | Border color override for the OFF state. |
| Disabled Text Color | Text color override for the OFF state. |
| Border Width (px) | Border thickness in pixels. Leave empty to use the stylesheet default. |
| Corner Radius (px) | Corner radius in pixels. Leave empty to use the stylesheet default. |

### Output Data

Each time a platform event fires the AFF, the following data values are available in the flow:

| Data Name | Type | Description |
|---|---|---|
| `{DataName}` | String | Counter incremented on each firing. Use as a signal that an event occurred. |
| `{DataName}_EventData` | `PlatformEventData` | Typed object containing details of the event that fired. |

`PlatformEventData` has the following properties:

| Property | Type | Description |
|---|---|---|
| `EventType` | `PlatformEventType` | The event type that fired (RefreshByFolder, RefreshByKey, RefreshByFolderAndKey). Null if fired from the toggle button. |
| `FolderId` | String | The folder ID from the event payload. Null for key-only events. |
| `Keys` | String[] | The keys from the event payload. Null for folder-only events. |

`{DataName}` is the value of the control's *Data Name* property (defaults to the component name or `PlatformEventTrigger`).

### Wiring up an Active Form Flow

1. Drag the **Event Trigger** control onto the form canvas.
2. In *Common Properties*, configure one or more *Platform Event Triggers*.
3. Open the **Logic** tab and create a new Active Form Flow.
4. Set the trigger source to **Event Trigger → Value Changed**.
5. The flow fires each time a matching event occurs. Use the output data fields above to react to the specific event that triggered the flow.

### Hiding the control

The control can be hidden on the form while keeping the trigger active. Set *Initial Visibility* to **Hidden** in the *Behaviour* section — the event listener runs regardless of visibility.

## Building from Source

### Prerequisites
- .NET 10.0 SDK or higher
- `CreateDecisionsModule` Global Tool (installed automatically during build)
- Decisions Platform SDK (NuGet package: `DecisionsSDK`)

### Build Steps

#### On Linux/macOS:
```bash
chmod +x build_module.sh
./build_module.sh
```

#### On Windows (PowerShell):
```powershell
.\build_module.ps1
```

#### Manual Build:
```bash
# 1. Publish the project
dotnet publish ./Decisions.EventTriggerFormControl/Decisions.EventTriggerFormControl.csproj --self-contained false --output ./Decisions.EventTriggerFormControl/bin -c Debug

# 2. Install/Update CreateDecisionsModule tool
dotnet tool update --global CreateDecisionsModule-GlobalTool

# 3. Create the module package
CreateDecisionsModule -buildmodule Decisions.EventTriggerFormControl -output "." -buildfile Module.Build.json
```

### Build Output
The build creates `Decisions.EventTriggerFormControl.zip` in the root directory. Upload it directly to Decisions via **System > Administration > Features**.

## Disclaimer

This module is provided "as is" without warranties of any kind. Use it at your own risk. The authors, maintainers, and contributors disclaim all liability for any direct, indirect, incidental, special, or consequential damages, including data loss or service interruption, arising from the use of this software.

**Important Notes:**
- Always test in a non-production environment first
- This module is not officially supported by Decisions

## License

[MIT](LICENSE)
