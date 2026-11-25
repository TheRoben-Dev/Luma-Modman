# Where the supported-games list comes from

The built-in profiles in `data/games/` cover the games with the most mods on the four sites Luma
works with, checked on 2026-11-25 from each site's public game listings:

- **Nexus Mods**: the public game list sorted by mod count. Top of the list: Skyrim Special
  Edition (~144,500 mods), Fallout 4 (~78,000), Skyrim (~73,200), Fallout: New Vegas, Stardew
  Valley, Oblivion, Cyberpunk 2077, Baldur's Gate 3.
- **Thunderstore**: communities by package count. Top: Lethal Company (~55,100 packages),
  Valheim, Risk of Rain 2, R.E.P.O., H3VR, PEAK, Northstar (Titanfall 2), BONELAB.
- **CurseForge**: games by popularity. Top PC games: Minecraft, World of Warcraft, The Sims 4,
  ARK: Survival Ascended.
- **mod.io**: popular games. Top: Transport Fever 3, Cyberpunk 2077, Abiotic Factor, The Witcher
  3, BMX Streets.

Games were merged across sites, then:

- Games that need their own launcher-level tooling rather than file mods (Minecraft, World of
  Warcraft addons, ARK's in-game mod browser) are left out for now.
- Games whose anti-cheat or terms ban modified files (Marvel Rivals, Final Fantasy XIV, Destiny 2,
  Warframe, Apex Legends, Dead by Daylight, PUBG, Rainbow Six Siege, Ghost Recon Breakpoint) are
  kept with `notModdable` and the reason, so players see why instead of wondering why a game is
  missing.
- Profiles name each site's own id for the game (Nexus domain, Thunderstore community, mod.io
  name, CurseForge slug), so the Browse tab only shows sites that carry it.

To add a game, add `data/games/<id>.json`; CI checks every profile (`CatalogTests`).
