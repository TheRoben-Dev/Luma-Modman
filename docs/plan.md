# Build plan

Each phase lands as one pull request, merged only with CI green.

1. **Shell** — solution layout, CI (build, tests, Godot import, UI smoke test under Xvfb, Linux
   export), themes with accent picker, sounds, controller/touch/mouse navigation.
2. **Games** — Steam library scan (libraryfolders.vdf, appmanifests, SD card), Heroic, Lutris and
   Flatpak installs, Proton compatdata prefixes, cover art, and data-driven support profiles for
   the 100+ most-modded PC games (mod paths, loader, sites, quirks, "not moddable" reasons).
   Game-first layout: the game being modded is picked in the header and every tab works on it
   (Mods, Browse, Downloads, Settings). Luma is a mod manager, not a launcher.
3. **Mod sites** — Thunderstore, mod.io, CurseForge and Nexus Mods clients with search,
   categories, sorting, details, screenshots, versions and dependencies; guided setup screens for
   API keys and sign-in; `nxm://` handler for Nexus "Mod Manager Download" links; rate limiting
   and attribution.
4. **Mods** — store outside the game folder, link-based deploy at launch through the `luma` Steam
   launch wrapper, per-game profiles, load order, conflicts, dependencies, update checks, rollback,
   automatic loader install (BepInEx, MelonLoader, SKSE, F4SE, REFramework, …), Proton DLL
   overrides, log viewer.
5. **Release** — packaging, GitHub Releases, in-app self-update, "Add Luma to Steam" with its own
   art, release workflow.
