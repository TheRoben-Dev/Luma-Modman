# Changelog

Player-facing changes per release. The section for a version opens its GitHub release notes and
the in-app "What's new" page.

## 0.17.0

- Separators, like MO2's: add a named separator anywhere in the load order to group mods (Add a separator on the Mods page, "Add separator above" on a mod; Mods → Add a separator… or a row's right-click menu on the desktop). Rename, remove or move them like mods; they don't change what goes in.
- Categories: give any mod a category. The desktop has a Category column, the filter matches categories, and controller mode can sort by category.
- Notes on any mod (shown with ✎ in the desktop list and in the mod's details), and rename a mod to what you call it. Updates still match it by the site's own name, and the name, notes and category are kept across updates.
- Rename a profile (Mods page in controller mode, the Rename… button next to the profile on the desktop).
- Controller mode: a mod's page scrolls, and has Move to top/bottom and Open mod folder.

## 0.16.0

- Tools, like MO2's executables: run xEdit, LOOT, BodySlide, Outfit Studio, Nemesis, Pandora, FNIS, DynDOLOD, TexGen, Wrye Bash, Synthesis or any other program with your mods in. Luma puts the mods in, starts the tool (on SteamOS and Bazzite inside the game's own Proton prefix, with the Proton Steam uses for the game, so it sees the same Data folder and plugin list), and takes the mods out when you close it. What the tool writes (patches, meshes, behaviour files) lands in Overwrite.
- Tools that come as mods (BodySlide, Nemesis) or that you unpacked into Downloads or the game folder are found for you; add one with a single press, or pick any program. Known tools get the arguments they need (xEdit is pointed at the Data folder, LOOT at the game).
- LOOT: when it closes, the plugin order it saved becomes your plugin order for the profile.
- Where: Mods → Tools in controller mode; the Tools menu (with a Run entry per tool), the Tools… toolbar button or Ctrl+T on the desktop; `luma tool GAME NAME` from a terminal.

## 0.15.0

- Plugins, deeper: Luma now reads each plugin's header, so ESP files flagged as masters load with the masters and light (ESL-flagged) plugins are known as light. The Plugins list shows each plugin's type and the masters it needs, and warns when a master is missing or turned off (the game would crash at start), when a plugin loads before a master it needs, and when more full plugins are on than the game can load (254).
- Plugins can be moved on their own, apart from their mods: A on a plugin → Move up/down in controller mode, Move up/down buttons or Ctrl+Up/Down on the desktop. "Sort plugins" puts every plugin after the masters it needs, changing as little as possible; "Follow the mod order again" forgets the order you set. The order is kept per profile and carried in modpacks.
- Controller mode, same as the desktop: "Add a mod from a file" can browse any folder (home, Downloads, Desktop, SD cards and USB drives on the Deck, every drive on Windows), not just the newest files in Downloads. The load order has "Find a mod" (filter by name) and "Sort by" (name, site, version, conflicts, updates first, off first). Mods → "Open a folder" opens the game, mods or Overwrite folder, and Settings → About opens Luma's own folders and the log file.
- Desktop: the On column sorts too (off first), and File → Open Overwrite folder.

## 0.14.0

- Overwrite, like MO2's: files the game or a tool makes while your mods are in (configs, generated patches, BodySlide or Nemesis output, logs next to mod files) no longer stay behind in the game folder. When the mods come out, Luma moves them to the game's Overwrite and puts them back in last every time, so they win over every mod. Saves and the game's main folder are left alone. See and manage them under Mods → Overwrite in controller mode, or the Overwrite banner and Mods → Overwrite… on the desktop: make a mod of them, or delete them.
- Fixed: when the game saved over a mod's file (replacing it instead of writing through it), taking the mods out left that file in the game folder and the game's own copy stuck in Luma's backup folder. The changed file now goes to Overwrite and the game's copy is put back.
- On Windows, mod files copied in (no symbolic links) that the game changes are kept in Overwrite too, instead of being lost when the mods come out.

## 0.13.0

- Update all: one button downloads every mod update Check for updates found (Mods in controller mode, the toolbar and Mods menu on the desktop). Mods a site only lets you get from its website (free Nexus accounts, some CurseForge authors) are counted so you can update them one by one.
- Touchscreen: hold a mod in the load order for a moment to pick it up, drag it up or down, and let go to drop it there.
- Folder paths in Settings and Downloads are shorter: `~/.local/share/luma/mods` on Linux, `%LOCALAPPDATA%\Luma\mods` on Windows (the full path shows on hover in the desktop layout).
- Fixed: in controller mode the Mods page of a game with many options could grow taller than the screen after a change, so its bottom (the load order) couldn't be scrolled to.
- Windows builds are code-signed once the project's signing certificate is set up.

## 0.12.0

- Conflicts: see every file that more than one of your mods has, which mod's copy goes in, and pick another winner per file, MO2-style. The pick is kept per profile and beats the load order. Both layouts: Mods → Conflicts in controller mode, Mods → Conflicts… (Ctrl+K) or a mod's right-click menu on the desktop, where several rows can be picked at once. A mod's own page has "Make it win all of these".
- Modpacks carry your picked winners and the plugins you turned off.

## 0.11.0

- FOMOD installers. Mods that come with install options (body types, textures, patches for other mods) now show their steps: pick the options with pictures and descriptions, step by step, and only the files you chose are installed. Options that need a plugin you don't have are marked, and recommended ones are picked for you. Works from Downloads, "Add a mod from a file" and modpacks, in both layouts.
- "Change install options" on an installed mod runs its installer again with your earlier choices filled in. Modpacks keep each mod's choices, so the people you share them with get the same setup.

## 0.10.0

- Plugins for Bethesda games. Skyrim (both editions), Enderal, Fallout 3, New Vegas, Fallout 4, Starfield and Oblivion: while mods are in the game, Luma writes plugins.txt (and loadorder.txt for Skyrim 2011, or file dates for the older games) so the plugins of your enabled mods load, masters first, in your mod order. Plugins you didn't install through Luma keep their place and state, and the game's own file is put back when the mods come out.
- A Plugins list in both layouts (Mods → Plugins, or Ctrl+P on the desktop) shows the load order and turns single plugins on or off.
- Archive invalidation is now actually set while mods are in (Fallout 3, New Vegas, Fallout 4, Fallout 76, Starfield, Oblivion), so replaced textures and meshes show up.

## 0.9.0

- Modpacks. Export a game's profile as a .lumapack file and share it: as a download list (everyone gets each mod from its site), or full, with the mods that may be passed on inside. Importing makes a new profile in the pack's load order on Linux, SteamOS and Windows alike: mods inside install straight away, the others download from their sites, and anything that has to be fetched by hand (free Nexus accounts, for example) is listed with a link. Both layouts have it: Mods → Import/Export in controller mode, File → Import/Export modpack (Ctrl+I, Ctrl+E) on the desktop.
- Mods of a profile that haven't arrived yet are shown with a way to forget them.
- Command line: `luma pack export` and `luma pack import`.

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
