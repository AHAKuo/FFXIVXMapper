# XMapper

A Dalamud plugin for FFXIV that fills your **Cross Hotbars** for the current class in one click, following a layout **bias** you choose. Map a job once, the same way, every time, instead of dragging every action by hand.

## Install

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Paste this URL into **Custom Plugin Repositories**, press **+**, then **Save**:

   ```
   https://raw.githubusercontent.com/AHAKuo/ahadevtools/main/pluginmaster.json
   ```

3. Open `/xlplugins`, search for **XMapper**, and install it.

## How it works

Switch to the class you want to map, open `/xmapper`, check the preview, hold **Ctrl** and click **Apply to cross hotbars**. The plugin writes the slots with the same call the game uses when you drag an action, so the result is saved like any manual hotbar edit.

### The default bias ("AHA")

| Region | Set | Half | Cluster | Gets |
|---|---|---|---|---|
| Single-target attacks | 1 | R2 | face buttons | single-target weaponskills and spells, in combo order |
| AoE attacks | 1 | L2 | face buttons | AoE weaponskills and spells |
| Abilities | 1 | L2 + R2 | d-pads | off-GCD abilities with normal cooldowns |
| Role actions | 2 | R2 | buttons, then d-pad | role actions |
| Rare actions | 2 | L2 | buttons, then d-pad | anything with a recast of 90 s or more |

When a region is full, the same region on set 3 continues it (set 3 mirrors set 1, set 4 mirrors set 2). Sets above the bias's **max set** (default 4) are never touched. Anything that does not fit is listed under the preview.

**AHA Mirrored** swaps L2 and R2. **Crafting** puts progress actions on R2 buttons, quality on L2, buffs on the d-pads. **Gathering** puts gather/yield actions on R2, buffs on L2 buttons, utility on the d-pads.

### Biases

A bias is just a list of regions per bucket, in fill order, plus a rare threshold and a max set. Built-in biases are read-only. Duplicate one in the **Biases** section, then edit the copy. The chosen bias is remembered per job.

By default only the regions a bias owns are emptied before writing. Turn on **Empty whole sets** if you want the used sets wiped completely.

## Commands

| Command | What it does |
|---|---|
| `/xmapper` | Open the window (preview, biases, options). |
| `/xmapper map` | Apply the current job's bias right away. |
| `/xmapper map <bias>` | Apply a specific bias by name. |

## Classification

- Eligible: player actions for the current class, not PvP, at or below your level, with their unlock quest done.
- Weaponskills and spells are GCDs; abilities are off-GCD.
- Single-target means cast type 1 with no effect range; everything else is AoE.
- Role actions come from the game's role flag. Rare is decided by recast time, before anything else.
- Only the highest eligible action in an upgrade chain is placed. The game swaps upgrades on the bar anyway.
- Crafting actions come from the `CraftAction` sheet and are classified by name. Specialist actions are skipped unless enabled in Options.

## Building

Requires the .NET 10 SDK and a Dalamud dev install (XIVLauncher).

```
dotnet build XMapper/XMapper.csproj -c Release
```

The plugin lands in `XMapper/bin/Release/XMapper.dll`. For local testing, add that path under Dalamud Settings > Experimental > Dev Plugin Locations.

## License

AGPL-3.0. See [LICENSE](LICENSE).
