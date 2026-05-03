# Path To Tarkov Alpha — Configuration Guide

## What is a Config?

PTT uses a config folder to define your entire open world. Each folder inside `configs/` is a separate world layout — different maps, different connections, different traders at different locations. You pick which one to use by editing `UserConfig.json` in the mod root.

```json
{
  "selectedConfig": "Default"
}
```

Change `"Default"` to `"DevilFlippy"`, `"LinearPath"`, etc. to switch worlds. Restart the server after changing this.

---

## The Three Core Concepts

### 1. Offraid Positions
Where your character physically is between raids. Think of it like a base camp or safe house. Every location on the map is an offraid position (e.g. `Crossroads`, `SkierHideout`, `PraporHideout`). You start at `initial_offraid_position` and move by extracting.

### 2. Infiltrations
Which spawn point you use when entering a map from a given offraid position.

```json5
infiltrations: {
  SkierHideout: {
    bigmap: ['Warehouse 17'],   // From Skier's hideout, you spawn at Warehouse 17 on Customs
  }
}
```

### 3. Exfiltrations
Which offraid position you move to when using a specific extract.

```json5
exfiltrations: {
  bigmap: {
    'Warehouse 17': ['SkierHideout'],  // Extracting at Warehouse 17 takes you to Skier's hideout
  }
}
```

The connection is always: **Extract → Offraid Position → Spawn**. One map's extract is another map's spawn point.

---

## Key Config Sections

- **`initial_offraid_position`** — where a fresh profile starts. Must be an offraid position that has infiltrations defined.
- **`respawn_at`** — where you respawn on death. Can be a list for random selection.
- **`traders_config`** — controls which trader is accessible from which offraid position via `access_via`. Fence uses `'*'` meaning always available everywhere.
- **`hideout_secondary_stashes`** — small stashes accessible when you're at specific offraid locations. Each has a `size` (stash grid rows) and `access_via` list.
- **`hideout_main_stash_access_via`** — which offraid positions give you access to your main hideout stash.
- **`offraid_regen_config`** — controls where hydration, energy, and health regenerate between raids.

---

## Auto-Generated Files

Two files are auto-generated per profile on first server start and should not be manually edited unless you know what you're doing:

- **`exfils_config.json5`** — lets you enable/disable individual extracts
- **`spawns_config.json5`** — lets you enable/disable individual spawn points

These regenerate automatically when SPT updates add new exits or spawn points.

---

## Included Configs

| Config | Description |
|---|---|
| `Default` | Full open world — all maps connected, all traders placed at locations |
| `DevilFlippy` | Community-made layout with custom world map |
| `LinearPath` | Simplified linear progression from map to map |
| `LegacyPathToTarkovV4/V5` | Classic PTT world layouts from previous versions |
| `OriginalNarcoticsConfig` | The original PTT world used in early releases |
| `PathToTarkovReloaded` | Alternative community layout |
| `TrapTransits` | Experimental transit-focused config |
| `Examples/` | Minimal and tutorial configs — good starting point for custom configs |

---

## Making Your Own Config

Copy the `Default` folder, rename it, and edit `UserConfig.json` to point to your new folder. The `Examples/MinimalConfig` is the best starting point — it has the smallest possible working config with comments explaining each section.

The config uses **JSON5** format which allows comments (`//`) and trailing commas, making it much easier to read and edit than standard JSON.
