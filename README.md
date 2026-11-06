# gd-save-editor

**Grim Dawn Save Editor** is a Windows desktop application (WPF, .NET 10) for **offline** [Grim Dawn](https://www.grimdawn.com/) characters. Its interface follows the [Last Epoch Save Editor](https://github.com/IgorBabayan/le-save-editor).

> [!IMPORTANT]
> **Exit Grim Dawn before saving**, and keep a separate backup of your save folder.

## Features

- Custom title bar with native Windows behaviour: minimize, maximize / restore down (also by double-clicking the title bar or Aero Snap), close, and resizing at the window edges.
- Title bar tools:
  - **Save** (Ctrl+S): writes the selected character to its `player.gdc`, keeping the previous file as `player.gdc.<date>.bak`.
  - **Reload** (F5): reads the selected character's save file again.
  - **Generate / update database**: builds `database.db` from the game files (see below).
  - **Notification center**: history of what happened, kept between runs.
  - **Settings**: theme, saves folder (default `Documents\My Games\Grim Dawn\save\main`) and game folder.
- **Automatic updates** for copies installed with the installer: on start the editor checks the latest GitHub release, downloads and verifies the new installer (progress shown as a toast, can be cancelled), counts down and restarts into the new version. Turn it off, change the countdown or update manually in **Settings → Updates** (*Check for updates* / *Download and update*). Unpacked or development builds are never updated.
- Character list (a quarter of the window) split into **Softcore** and **Hardcore** tabs, each with a **Show only SSF characters** filter.
- Character details with **Character** (equipment, attributes and bags), **Stash**, **Waypoints** and **Factions** tabs.
- **Character panel** (beside the equipment): name, sex, unlocked difficulty (Normal / Elite / Ultimate), Crucible
  tributes, physique, cunning, spirit, and unspent attribute, skill and devotion points (at most 55). Changed values are
  highlighted and written on **Save**; base health and energy follow physique and spirit like in the game; Esc or the
  undo buttons put back the values of the save file.
- **Bags**: items can be dragged to another cell or bag, and added, changed or removed with the **item builder**:
  - right-click a free cell → *Build a new item* → pick a kind (helm, weapon, ring, relic, component, …): the builder lists only that kind and the item goes to that cell; or the **+** button in the bag header (any item, first free cell);
  - click an item to open it in the item inspector beside it (expand for the full builder), or right-click it → *Change…* / *Remove*;
  - the undo button puts every bag back as the save file has it.
- **Item builder** (needs the game database): category and rarity filters over every item of the game, a live preview
  with the game's tooltip, and the parts an item can take: prefix, suffix, crafting bonus, component and augment for
  gear, completion bonus (the ones the relic rolls are listed first) and crafting bonus for relics. The seed the game
  rolls the values from can be typed or rerolled; components, augments and consumables take a stack size.
- **Stash**: the **personal stash** (tabs 1–8; a tab the character has not bought in the game yet is not in the save
  file, so it is shown locked and nothing can be put there) and the **shared stash** of every softcore or every
  hardcore character (`transfer.gst` / `transfer.gsh` in the `save` folder above `save\main`). Items are dragged,
  built, changed and removed like in the bags; each stash has its own undo button. **Save** writes the personal stash
  into `player.gdc` and, when it was changed, the shared stash into its own file (kept as `transfer.gst.<date>.bak`).
  The shared stash is the same for every character of a mode, so its changes stay while another character is selected.
- **Waypoints** (needs the game database built with the game folder set): the rift gates of every campaign — base
  campaign (Acts 1–3, 30), Ashes of Malmouth (10), Forgotten Gods (13) and Fangs of Asterkarn (19) — for Normal, Elite
  and Ultimate. Tick single waypoints, **Unlock all** of a campaign, unlock everything in the selected difficulty or in
  every difficulty; changes are highlighted and written on **Save**, the undo button puts the save file's lists back.
- **Factions**: the reputation with every faction, split into allies and enemies (infamy is stored as a negative
  value). Type a value from -25,000 to 25,000 (Up/Down by one, Shift by 100), pick a rank from the badge (Nemesis …
  Revered), or set a whole group to its top rank or back to Tolerated; one button sets allies Revered and enemies
  Nemesis. A changed faction is also unlocked so the game shows it. Devil's Crossing, Aetherials, Chthonians and
  Cronley's Gang are always listed; the other names come from the game texts, so generate the database to see them.

## Game database

The database button asks for confirmation, then gets these files:

| File | Content |
| --- | --- |
| `database/database.arz` | Base-game records, including items |
| `gdx1/database/GDX1.arz` | Ashes of Malmouth |
| `gdx2/database/GDX2.arz` | Forgotten Gods |
| `gdx3/database/GDX3.arz` | Fangs of Asterkarn |
| `resources/Text_EN.arc` | English item names and descriptions |
| `resources/Items.arc` | Item icons and other item resources |
| `resources/Levels.arc`, `gdx1/…`, `gdx2/…`, `gdx3/resources/Levels.arc` | World maps: the waypoints (rift gates) of each campaign |

Each file is first requested from `https://www.grimtools.com/db/<path>`. When the response is not a valid game file, the file is read from the Grim Dawn installation folder (Settings, or found through Steam / GOG). Expansions you do not own are skipped with a warning.

The `Levels.arc` archives are only read from the Grim Dawn installation folder (they are far too large to download). The editor reads the `"- World Riftgates"` table of `world001.map` in each of them; a waypoint belongs to the first campaign whose archive has it.

The item records, the texts and the `Items.arc` file list are compared with `%APPDATA%\GD.SaveEditor\database.db`. Only what the database does not hold yet (new rows, changed rows, rows that are gone) is written as a migration to `%APPDATA%\GD.SaveEditor\Migrations\<timestamp>_game_data.sql` and applied in one transaction; applied migrations are listed in the `__migrations` table. When the game files did not change, no migration is created.

## Building from source

```powershell
dotnet build GD.SaveEditor/GD.SaveEditor.sln
```
