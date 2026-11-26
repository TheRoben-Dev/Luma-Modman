# Luma Modman

A controller-first mod manager for SteamOS and Bazzite (Steam Deck), and Windows (desktops and
handhelds like the ROG Ally and Legion Go), built with Godot 4 (.NET). It runs in Game Mode,
Desktop Mode and Big Picture, and works just as well with touch and a mouse.

Luma has two layouts with the same features:

- **Controller layout**: full screen with big rows, for a pad or touch. It's used in Game Mode
  and Big Picture.
- **Desktop layout**: a traditional mod manager window with a menu bar, toolbar, games list, a
  load-order table with checkboxes, sortable columns, drag-and-drop and right-click menus, a mod
  details pane and a log pane. It's used everywhere else. Change it in Settings → Layout.

## Install

### Linux (SteamOS, Bazzite)

1. Download `Luma-linux-x86_64.tar.gz` from the [latest release](https://github.com/TheRoben-Dev/Luma-Modman/releases/latest)
   (in Desktop Mode on a Steam Deck).
2. Unpack it and run `./install.sh` from the `Luma` folder. Luma goes to `~/Applications/Luma`
   with a menu entry. If Steam is closed it also adds a Steam shortcut with Luma's own art;
   `./install.sh --restart-steam` closes and restarts Steam to do it, and Settings → Updates →
   "Add Luma to Steam" does the same from inside the app.
3. Open Luma from Steam (Game Mode too). Updates install from inside the app, with one-step
   rollback.

### Playing with mods

Mods are kept in Luma's folder. To have Steam link them in at every start (and take them out when
the game closes), set the game's launch options to the line Luma shows under **Mods → Start with
mods from Steam**:

```
"/home/deck/Applications/Luma/luma" run -- %command%
```

On Windows the line is the same with Luma's Windows path:

```
"C:\Users\you\AppData\Local\Programs\Luma\luma-cli.exe" run -- %command%
```

For other launchers, use **Put the mods in the game now** and **Take the mods out** when done.

### Windows (10 and 11, x64)

1. Download `Luma-windows-x86_64.zip` from the [latest release](https://github.com/TheRoben-Dev/Luma-Modman/releases/latest)
   and unpack it.
2. Double-click **Install Luma.cmd** in the `Luma` folder. Luma goes to
   `%LOCALAPPDATA%\Programs\Luma` (no admin rights) with a Start menu entry and an entry in
   Settings → Apps to uninstall it. If Steam is closed it also adds a Steam shortcut with Luma's
   art (or run `"Install Luma.cmd" -RestartSteam`, or use Settings → Updates → "Add Luma to Steam").
   Windows may warn about an unrecognised app the first time: the builds aren't code-signed yet.
3. Open Luma from the Start menu or Steam (Big Picture too). Updates install from inside the app,
   with one-step rollback, exactly as on Linux.

Differences from Linux: games run directly (no Proton prefixes), so mods that go in Documents or
AppData use your own folders. Games are found in Steam (every library), the Epic Games Launcher,
GOG Galaxy and Heroic. Mod files are linked in as symbolic links when Windows allows it
(Developer Mode on), else as hard links, else copied; either way they come out again when the game
closes. 7z and rar archives need 7-Zip, or Windows 11's built-in `tar`.

## What it does

Luma is a mod manager, not a launcher: you pick the game you're modding in the header (View /
Select), and every tab works on that game. Games still start from Steam or their launcher.

- **Games**: finds supported games in every Steam library (including the SD card) and in Heroic,
  Lutris and Flatpak installs, with their Proton prefix, launch options and cover art. On Windows:
  Steam, the Epic Games Launcher, GOG Galaxy and Heroic.
- **Mods**: the game's setup (loader, where mods go, Proton or native) and its mods: per-game
  profiles, enable/disable with Y, load order, conflict and missing-dependency warnings, update
  checks and rollback. Loaders (BepInEx, MelonLoader, UE4SS) install from their official releases.
  Bethesda games get their plugin list (plugins.txt, masters first, in mod order, single plugins
  on or off under Plugins) and archive invalidation written while mods are in, and put back after.
  Mods with FOMOD installers show their install options step by step, with pictures. Conflicts
  lists files that several mods have and lets you pick which copy goes in, per file.
- **Browse**: Thunderstore, mod.io, CurseForge and Nexus Mods, showing only the sites that carry
  the game, with guided setup for sites that need an account or API key.
- **Downloads**: progress, retry, and "Mod Manager Download" links from Nexus Mods. Finished
  downloads install themselves for their game.
- **Modpacks**: export a profile as a `.lumapack` (Mods → "Export this profile as a modpack", or
  File → Export modpack… / Ctrl+E on the desktop). A *download list* is a small file: whoever
  imports it downloads each mod from its own site. A *full* pack also carries the mod files that
  may be passed on (Thunderstore packages, the open-source loaders, files you added yourself);
  Nexus Mods, CurseForge and mod.io files and script extenders always stay download entries, as
  those sites and authors require. Importing (Ctrl+I on the desktop, or `luma pack import FILE`)
  makes a new profile in the pack's order on Linux, SteamOS or Windows alike, and lists anything
  that has to be fetched by hand.
- **Clean games**: mods live in Luma's own folder and are linked into the game only while it runs,
  so game files stay untouched and everything can be undone.

## Controls

| Action | Controller | Keyboard |
|---|---|---|
| Confirm | A | Enter |
| Back | B | Esc |
| Switch tabs | LB / RB | Q / E, Tab / Shift+Tab |
| Switch game | View / Select | G |
| Context actions | X / Y | F / R |

There is no visible cursor; touch and mouse clicks work everywhere. Text entry has its own
on-screen keyboard (Y deletes, X adds a space, Paste for API keys); a real keyboard and Steam's
keyboard (Steam + X in Game Mode or Big Picture) type straight in.

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

The Windows build exports from Linux too (`--export-release Windows`, with the Windows export
templates installed); CI also builds and tests on a Windows runner and checks the installer.
Platform differences live behind `Luma.Core.Platform` and the `Windows*` classes; Linux code paths
are unchanged.

Releases sign `Luma.exe` and `luma-cli.exe` when the repository has code-signing secrets (Azure
Trusted Signing, or a `.pfx` certificate): see `tools/release/sign-windows.ps1` for the secret
names. Without them the Windows build ships unsigned and SmartScreen asks before the first start.

## Layout

```
src/
  Luma.Core/   Godot-free: paths, game profiles, detection, mod store and deployment
  Luma.Host/   composition root shared by the app and the CLI, settings, self-update, log
  Luma.Cli/    `luma` (luma-cli.exe on Windows): Steam launch wrapper (`luma run`), deploy/undeploy, nxm links, steam-add
  Luma.App/    Godot project: screens, focus, input, themes, sounds, animation
tests/         xUnit tests, run in CI without Godot or a display
data/games/    per-game support profiles (JSON): add a game by adding a file
data/loaders.json  mod loaders and script extenders Luma can install
data/steam-art/    Steam library art (drawn by tools/art/make_steam_art.py)
tools/release/     packaging, install.sh (Linux) and windows/ (Install Luma.cmd, install.ps1, uninstall.ps1);
                   CI publishes a release, both platforms, when the version changes
```

## Supported games

`data/games/` holds one profile per game (136 today, 9 of them marked not moddable). The list was
picked from public popularity data on the mod sites; see [docs/popularity-sources.md](docs/popularity-sources.md).
A profile says how to find the game (Steam app ids, Epic/GOG ids, files in its folder), where mods
go, which loader it needs, which sites carry it and what's worth knowing. Players can add or fix a
game without an update by putting a profile in `~/.config/luma/games/`.

`luma games` lists what Luma finds on this machine; `luma games --all` lists every profile.

The visual style (themes, colours, sounds, animation, focus) follows Scanline's design by the same
author; the layout is Luma's own.
