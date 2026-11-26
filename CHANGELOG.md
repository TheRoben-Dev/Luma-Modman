# Changelog

Player-facing changes per release. The section for a version opens its GitHub release notes and
the in-app "What's new" page.

## 0.8.0

- A real desktop layout. Outside Game Mode, Luma is now a traditional mod manager window: menu bar and toolbar, your games on the left, the load order as a table with on/off checkboxes, sortable columns, drag-and-drop reordering and right-click menus, a details pane for the selected mod, and the app log at the bottom. Browse, Downloads and Settings are desktop views too, and questions and setup open as ordinary dialog windows. No controller button prompts anywhere.
- The controller layout is unchanged, and both layouts have the same features. Switching between them in Settings → Layout restarts Luma.
- Profiles can be deleted (in both layouts).
- Settings → Mod sites: set up your Nexus Mods, CurseForge and mod.io keys in one place.

## 0.7.0

- A desktop layout: a resizable window with more on screen, right-click for a row's options, and keyboard shortcuts (Ctrl+1–4 tabs, Ctrl+F search, Ctrl+Up/Down to reorder mods). It's on automatically outside Game Mode; change it in Settings → Layout.
- Script extenders install as mods: download SKSE, F4SE, SFSE, OBSE, xNVSE or FOSE, then choose "Add a mod from a file". Starting the game from Steam with Luma's launch option then goes through the extender.
- Add a mod from a file: install any mod archive from Downloads or the Desktop.
- 28 more games, 165 in all. New moddable ones include ARK: Survival Evolved, Lies of P, Silent Hill 2, Grounded, Manor Lords and Divinity: Original Sin 2. Games with anti-cheat, like Honkai: Star Rail, Fortnite and Helldivers 2, are listed with the reason they can't be modded.

## 0.6.0

- Luma for Windows: download `Luma-windows-x86_64.zip` and double-click "Install Luma.cmd". Same app, same controls, on desktops and Windows handhelds (ROG Ally, Legion Go, MSI Claw).
- On Windows, games are found in Steam (every library), the Epic Games Launcher, GOG Galaxy and Heroic. Mods that go in Documents or AppData use your own folders.
- On Windows, the Steam launch option, Nexus "Mod Manager Download" links, Add Luma to Steam, and in-app updates with rollback all work as on Linux.
- Games started through a launcher or script extender keep their mods until the game itself closes.

## 0.5.0

- First release: download `Luma-linux-x86_64.tar.gz` and run `./install.sh`. Luma installs to ~/Applications/Luma with a menu entry.
- Add Luma to Steam, with its own library art, from the installer or Settings → Updates, so it opens from Game Mode.
- Updates install from inside the app, and the previous version is kept for rollback.
- A new icon.

## 0.4.0

- Downloads install themselves for their game. Mods are kept in Luma's folder, never mixed into the game's.
- The Mods tab shows the load order: turn mods on and off (Y), move them up and down, and see which files later mods replace.
- Profiles: separate sets of mods for the same game, switched in one step.
- Start with mods from Steam: one launch option links the mods in at every start (Game Mode too) and takes them out when the game closes. Or put them in now and take them out later.
- Loaders (BepInEx, MelonLoader, UE4SS) install from their official releases with the first mod.
- Missing required mods are flagged and fetched from Thunderstore in one go.
- Check for updates, update in place, and roll back to the previous version you downloaded.
- Dead as Disco: add maps downloaded from DiscoMaps straight into the game's song folder.
- Command line: `luma run`, `luma deploy`, `luma undeploy` and `luma mods`.

## 0.3.0

- Browse and search Thunderstore, Nexus Mods, mod.io and CurseForge for the game you're modding: sort by popular, updated or new, filter by category, and load more as you scroll.
- Mod pages with the description, pictures, what the mod needs, every file and version, and a link to the mod's website.
- Downloads with progress, cancel and retry. Finished files are kept for reinstalling and rolling back.
- Guided setup for sites that need your own key (Nexus Mods, mod.io, CurseForge): open the key page, paste the key, and Luma checks it.
- Nexus Mods "Mod Manager Download" buttons send files straight to Luma. Premium members download right in the app.
- An on-screen keyboard for searching with a controller, with Paste for keys. A real keyboard and Steam's keyboard work too.
- Dead as Disco support, with a link to the DiscoMaps community map site.

## 0.2.0

- Finds your games: every Steam library (SD card too), Heroic (Epic, GOG, Amazon), Lutris, and their Flatpak versions, with Proton prefixes and cover art.
- Supports 136 of the most-modded PC games out of the box; games with anti-cheat or online bans are shown as not moddable, with the reason.
- Pick the game you're modding with View / Select: Mods, Browse and Downloads all work on that game.
- The Mods tab shows the game's setup: its loader, where mods go and how it runs.
- Browse lists only the mod sites that carry your game.
- Settings → Games: scan again, the full supported list, and your own game profiles.

## 0.1.0

- First build: the controller-first shell with Library, Browse, Installed, Downloads and Settings tabs.
- Eight themes with an accent picker, menu sounds, rumble and smooth animations.
- Works with a controller, touch and mouse.
