# ErenshorBuddy

ErenshorBuddy is a starter implementation for a Unity/BepInEx combat-grinding bot for Erenshor. It includes:

- a `BepInEx` plugin for game-side state capture, decision-making, and action execution
- a Windows desktop companion app for selecting profiles and controlling the bot over a local named pipe
- shared contracts and a testable agent core

## Repository layout

- `src/ErenshorBuddy.Contracts`: shared DTOs for profiles, snapshots, commands, and events
- `src/ErenshorBuddy.Core`: goal-driven decision engine and runtime interfaces
- `src/ErenshorBuddy.Plugin`: BepInEx plugin, Unity snapshot adapter, named-pipe host, and Windows input actuator
- `src/ErenshorBuddy.Companion`: WinForms operator UI
- `tests/ErenshorBuddy.Tests`: unit tests for the decision engine
- `profiles/example-farm-profile.json`: sample farming profile

## Current implementation notes

This v1 scaffold deliberately keeps the game-specific pieces thin:

- world state comes from Unity scene data, tags, and reflection heuristics
- abilities are mapped to action-bar slots `1` through `6`
- target acquisition uses `TAB`, loot uses `F`, and minimal repositioning pulses `W`
- zone-to-zone travel is not implemented
- unsupported states pause the bot and surface an alert to the companion app

To make the plugin fully reliable for Erenshor, expect to replace the reflection heuristics in `ReflectionGameWorldAdapter` with concrete bindings once the game's assemblies are inspected.

## Build prerequisites

- Windows
- Visual Studio 2022 or newer, or a .NET SDK that can build `net48`, `netstandard2.0`, and `net8.0-windows`
- BepInEx package feed enabled via `NuGet.config`
- `ERENSHOR_MANAGED_DIR` set to the game's managed assembly folder, or `GameManagedDir` passed at build time, so the plugin can reference `UnityEngine*.dll`

## Suggested next steps

1. Install the .NET SDK if the machine only has runtimes.
2. Inspect Erenshor's managed assemblies and replace the generic reflection lookups with real type/property bindings.
3. Copy a JSON profile into `%AppData%\ErenshorBuddy\Profiles`.
4. Build the solution with `GameManagedDir` pointed at the game's managed folder, deploy the plugin DLL into the game's `BepInEx/plugins` folder, and run the companion app locally.
