# ErenshorBuddy

ErenshorBuddy is a starter repo for building an agentic helper for the Unity game Erenshor. It currently provides a working project structure with:

- a `BepInEx` plugin that runs inside the game
- a goal-driven combat loop scaffold
- a local IPC layer for bot control and live status
- a Windows companion app for starting, pausing, and monitoring the bot
- shared contracts and unit tests for the decision engine

This repo is usable as a foundation, but it is not yet a fully game-aware Erenshor bot. The current Unity adapter uses generic reflection and scene heuristics that must be replaced with real Erenshor-specific bindings.

## What this repo contains

- `src/ErenshorBuddy.Contracts`
  Shared data contracts for profiles, snapshots, commands, alerts, and status payloads.
- `src/ErenshorBuddy.Core`
  The bot decision engine and interfaces for world-state capture, actuation, and runtime events.
- `src/ErenshorBuddy.Plugin`
  The in-game `BepInEx` plugin. This is where Unity state is read and bot actions are executed.
- `src/ErenshorBuddy.Companion`
  A WinForms desktop app that connects to the plugin over a named pipe.
- `tests/ErenshorBuddy.Tests`
  Unit tests for the goal-driven agent behavior.
- `profiles/example-farm-profile.json`
  A sample farming profile you can copy and adapt.

## Current behavior

The current v1 scaffold is designed for one-zone combat grinding.

Implemented:

- plugin bootstrap through `BepInEx`
- profile-driven combat decisions
- named-pipe communication between plugin and companion app
- companion app controls for `Start`, `Pause`, `Resume`, `Stop`, `Request Snapshot`, and `Acknowledge Alert`
- sample farming profile
- build/testable shared contracts and decision logic

Not implemented yet:

- real Erenshor type bindings
- robust target selection based on actual Erenshor data structures
- zone-to-zone travel
- dynamic pathfinding
- strong stuck recovery
- production-safe combat rotation based on inspected cooldown/resource state

## Requirements

- Windows
- Erenshor installed locally
- `BepInEx` installed into the Erenshor game directory
- .NET SDK 8 or newer
- access to Erenshor's Unity managed assemblies

The managed assembly folder on this machine is:

```text
C:\Program Files (x86)\Steam\steamapps\common\Erenshor\Erenshor_Data\Managed
```

## Build instructions

Open a PowerShell shell in the repo root and set the Unity managed assembly path:

```powershell
$env:GameManagedDir='C:\Program Files (x86)\Steam\steamapps\common\Erenshor\Erenshor_Data\Managed'
```

Restore and build:

```powershell
dotnet restore ErenshorBuddy.sln
dotnet build ErenshorBuddy.sln -c Debug
```

Run tests:

```powershell
dotnet test tests\ErenshorBuddy.Tests\ErenshorBuddy.Tests.csproj -c Debug
```

If you prefer, you can also set `ERENSHOR_MANAGED_DIR` instead of `GameManagedDir`. The plugin project uses that path to resolve `UnityEngine*.dll`.

## Build outputs

After a successful debug build, the main outputs are:

- plugin: `src\ErenshorBuddy.Plugin\bin\Debug\net48\ErenshorBuddy.Plugin.dll`
- companion app: `src\ErenshorBuddy.Companion\bin\Debug\net8.0-windows\ErenshorBuddy.Companion.dll`

## Deploying the plugin

1. Install `BepInEx` into the Erenshor game folder if you have not already.
2. Build the solution.
3. Copy `ErenshorBuddy.Plugin.dll` into Erenshor's `BepInEx\plugins` folder.
4. Launch the game once and confirm `BepInEx` loads the plugin.

Typical target folder:

```text
C:\Program Files (x86)\Steam\steamapps\common\Erenshor\BepInEx\plugins
```

## Running the companion app

Build the solution, then launch the companion app from:

```text
src\ErenshorBuddy.Companion\bin\Debug\net8.0-windows\ErenshorBuddy.Companion.exe
```

The companion app expects the plugin named pipe to be:

```text
ErenshorBuddyPipe
```

That default can be changed in the plugin config if needed.

## Using profiles

Profiles define the farm area, ability rotation, thresholds, and stop conditions. A sample profile is included at:

- `profiles/example-farm-profile.json`

The companion app looks for profiles in:

```text
%AppData%\ErenshorBuddy\Profiles
```

To use the sample:

1. Create `%AppData%\ErenshorBuddy\Profiles` if it does not exist.
2. Copy `profiles/example-farm-profile.json` into that folder.
3. Launch the companion app.
4. Click `Refresh Profiles`.
5. Select the profile and click `Start`.

### Profile fields

- `Name`
  Friendly display name in the companion app.
- `FarmAreaId`
  Zone or scene identifier expected by the bot.
- `MobPriorityNames`
  Preferred hostile names to engage first.
- `AbilityRotation`
  Ordered list of action-bar abilities to attempt.
- `ResourceThresholds`
  Minimum health/resource values for safe operation.
- `StopConditions`
  Runtime limits such as max kills, low durability, or full inventory.
- `PullRadius`
  Desired range for acquiring mobs.
- `LeashRadius`
  Maximum operating radius before the bot should stop chasing.
- `LootCorpses`
  Whether the bot should attempt looting after kills.

## How the repo is structured technically

### Plugin side

The plugin owns:

- reading game state from Unity objects
- running the goal-driven decision engine
- turning decisions into game actions
- publishing snapshots and status updates to the companion app

The current adapter is in:

- [ReflectionGameWorldAdapter.cs](C:\Users\Hunter\Documents\ErenshorBuddy\src\ErenshorBuddy.Plugin\ReflectionGameWorldAdapter.cs)

This class is intentionally generic and should be treated as a placeholder until Erenshor's managed assemblies are inspected.

### Companion side

The companion app is intentionally thin. It does not implement bot logic. It only:

- connects to the plugin
- loads profiles from disk
- sends control commands
- displays state, target, zone, alerts, and logs

### Agent core

The decision engine lives in:

- [GoalDrivenAgent.cs](C:\Users\Hunter\Documents\ErenshorBuddy\src\ErenshorBuddy.Core\GoalDrivenAgent.cs)

This is the cleanest place to extend bot behavior once the game-state adapter becomes reliable.

## Recommended workflow for making this actually work in Erenshor

1. Inspect Erenshor assemblies with dnSpy, ILSpy, or Rider decompiler.
2. Identify the real player, target, combat, inventory, ability-bar, and scene state objects.
3. Replace the reflection heuristics in the plugin with explicit reads from those real classes.
4. Confirm the correct action path for target select, skill usage, and loot interaction.
5. Add stronger validation around cooldowns, resource costs, casting state, and out-of-range handling.
6. Test in one safe farming zone before attempting broader autonomy.

## Safety and limitations

This project is for a single-player Unity game, but it still directly automates player actions. You should assume the current implementation is experimental.

Important limitations:

- the current bot may not identify the correct player or target object in Erenshor
- ability slots are currently mapped to keys `1` through `6`
- targeting currently defaults to `TAB`
- looting currently defaults to `F`
- short repositioning currently pulses `W`
- the plugin does not yet verify true combat legality or game-specific UI state
- failure handling currently favors pause-and-alert over aggressive recovery

## Verified commands

These commands were run successfully in this repo:

```powershell
$env:GameManagedDir='C:\Program Files (x86)\Steam\steamapps\common\Erenshor\Erenshor_Data\Managed'
dotnet build ErenshorBuddy.sln -c Debug
dotnet test tests\ErenshorBuddy.Tests\ErenshorBuddy.Tests.csproj -c Debug
```

## Next development targets

- replace `ReflectionGameWorldAdapter` with Erenshor-specific bindings
- inspect and formalize ability metadata instead of using placeholder slot readiness
- add real target acquisition rules based on Erenshor entities
- add runtime logging around decision transitions and failed actions
- add deployment scripts for copying the plugin into the game folder
- add integration tests around IPC and profile loading
