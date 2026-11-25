# Luma Modman

A controller-first mod manager for SteamOS and Bazzite (Steam Deck), built with Godot 4 (.NET).
It runs in Game Mode and Desktop Mode, and works just as well with touch and a mouse.

> Work in progress. Built in phases; see [docs/plan.md](docs/plan.md).

## What it does

- **Library**: finds supported games in every Steam library (including the SD card) and in
  Heroic, Lutris and Flatpak installs, and shows them as cartridges with their art.
- **Browse**: Thunderstore, mod.io, CurseForge and Nexus Mods in one controller-friendly browser,
  with guided setup for sites that need an account or API key.
- **Installed**: per-game profiles, one-tap enable/disable, load order on the d-pad, conflict and
  missing-dependency warnings, update checks and full rollback.
- **Downloads**: progress, retry, and "Mod Manager Download" links from Nexus Mods.
- **Clean games**: mods live in Luma's own folder and are linked into the game only while it runs,
  so game files stay untouched and everything can be undone.

## Controls

| Action | Controller | Keyboard |
|---|---|---|
| Confirm | A | Enter |
| Back | B | Esc |
| Switch tabs | LB / RB | Q / E, Tab / Shift+Tab |
| Context actions | X / Y | F / R |

There is no visible cursor; touch and mouse clicks work everywhere. Text fields use the Steam
on-screen keyboard.

## Rules

Luma never downloads games, DRM bypasses or pirated content. It follows each mod site's API terms,
rate limits and attribution rules. Games with anti-cheat or online-only modding bans are listed as
not moddable, with the reason.

## Building

Requires the .NET 8 SDK and Godot 4.7 (.NET build).

```sh
dotnet build Luma.sln -c Release
dotnet test Luma.sln -c Release
dotnet build src/Luma.App -c Debug
godot --path src/Luma.App                      # run the app
tools/smoke/run-ui-smoke.sh "$(which godot)" /tmp/shots   # scripted UI test under Xvfb
```

## Layout

```
src/
  Luma.Core/   Godot-free: paths, game profiles, detection, mod store and deployment
  Luma.Host/   composition root shared by the app and the CLI, settings, self-update, log
  Luma.Cli/    `luma` headless command (later: Steam launch wrapper)
  Luma.App/    Godot project: screens, focus, input, themes, sounds, animation
tests/         xUnit tests, run in CI without Godot or a display
data/games/    per-game support profiles (JSON): add a game by adding a file
```

The UI framework (themes, focus highlight, cartridges, sounds, touch scrolling) follows Scanline's
design by the same author.
