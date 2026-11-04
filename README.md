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
- Character list (a quarter of the window) split into **Softcore** and **Hardcore** tabs, each with a **Show only SSF characters** filter.
- Character details with **Inventory** and **Waypoints** tabs (not implemented yet).

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

Each file is first requested from `https://www.grimtools.com/db/<path>`. When the response is not a valid game file, the file is read from the Grim Dawn installation folder (Settings, or found through Steam / GOG). Expansions you do not own are skipped with a warning.

The item records, the texts and the `Items.arc` file list are compared with `%APPDATA%\GD.SaveEditor\database.db`. Only what the database does not hold yet (new rows, changed rows, rows that are gone) is written as a migration to `%APPDATA%\GD.SaveEditor\Migrations\<timestamp>_game_data.sql` and applied in one transaction; applied migrations are listed in the `__migrations` table. When the game files did not change, no migration is created.

## Building from source

```powershell
dotnet build GD.SaveEditor/GD.SaveEditor.sln
```
