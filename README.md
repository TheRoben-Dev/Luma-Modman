# Luma Modman

A controller-first mod manager for SteamOS and Bazzite (Steam Deck), built with Godot 4 (.NET).
It runs in Game Mode and Desktop Mode, and works just as well with touch and a mouse.

## Install

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

For other launchers, use **Put the mods in the game now** and **Take the mods out** when done.

## What it does

Luma is a mod manager, not a launcher: you pick the game you're modding in the header (View /
Select), and every tab works on that game. Games still start from Steam or their launcher.

- **Games**: finds supported games in every Steam library (including the SD card) and in Heroic,
  Lutris and Flatpak installs, with their Proton prefix, launch options and cover art.
- **Mods**: the game's setup (loader, where mods go, Proton or native) and its mods: per-game
  profiles, enable/disable with Y, load order, conflict and missing-dependency warnings, update
  checks and rollback. Loaders (BepInEx, MelonLoader, UE4SS) install from their official releases.
- **Browse**: Thunderstore, mod.io, CurseForge and Nexus Mods, showing only the sites that carry
  the game, with guided setup for sites that need an account or API key.
- **Downloads**: progress, retry, and "Mod Manager Download" links from Nexus Mods. Finished
  downloads install themselves for their game.
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
keyboard (Steam + X) type straight in.

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
  Luma.Cli/    `luma`: Steam launch wrapper (`luma run`), deploy/undeploy, nxm links, steam-add
  Luma.App/    Godot project: screens, focus, input, themes, sounds, animation
tests/         xUnit tests, run in CI without Godot or a display
data/games/    per-game support profiles (JSON): add a game by adding a file
data/loaders.json  mod loaders and script extenders Luma can install
data/steam-art/    Steam library art (drawn by tools/art/make_steam_art.py)
tools/release/     packaging and install.sh; CI publishes a release when the version changes
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
