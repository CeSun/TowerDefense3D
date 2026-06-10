# Tower Defense 3D

[中文](README_zh.md)

A 3D tower defense game built with [Avalonia UI](https://avaloniaui.net/) and [Aura3D](https://github.com/CeSun/Aura3D).

Place towers along the enemy path to stop waves of enemies from reaching the exit. Manage gold, choose the right tower mix, and survive all waves.

![Screenshot](screenshot.png)

## Features

- 7 tower types with unique mechanics (single-target, splash, slow, multi-shot, crit, DOT, AOE)
- 3 enemy types (Basic, Fast, Tank) across 5+ waves per map
- **Built-in map editor** — design custom maps with a live 3D preview, configure paths and waves, then play-test instantly
- 10 pre-built maps with progressive difficulty and player progress saving
- Isometric 3D view with real-time combat, projectiles, and HP bars
- Cross-platform: Windows, macOS, Linux & Android

## Tech Stack

| Layer | Technology |
|---|---|
| UI Framework | Avalonia 12 + Fluent Theme |
| 3D Engine | Aura3D (Avalonia integration) |
| Runtime | .NET 10, C# 13 |
| Platforms | Windows, macOS, Linux, Android |

## Project Structure

```
TowerDefense3D/
├── TowerDefense/              # Core game library
│   ├── GameData.cs            # Tower/enemy definitions, runtime data models
│   ├── GameManager.cs         # Core logic: waves, combat, pathfinding, projectiles
│   ├── MapData.cs             # Map serialization, save data, JSON source generation
│   ├── GameView.axaml/.cs     # 3D scene, HUD, level select, game over overlay
│   ├── MenuView.axaml/.cs     # Main menu (Play Game / Map Editor)
│   ├── MapEditorView.axaml/.cs # Map editor with 3D preview and wave config
│   ├── MapListControl.axaml/.cs # Map browser with 3D preview, edit, delete
│   ├── MainView.axaml/.cs     # Navigation container
│   ├── MainWindow.axaml/.cs   # Window shell
│   └── Maps/                  # 10 pre-built map JSON files (1–10.json)
├── TowerDefense.Desktop/      # Windows Desktop launcher
├── TowerDefense.Android/      # Android launcher
└── TowerDefense3D.slnx
```

## Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

### Run (Desktop)

```bash
cd TowerDefense.Desktop
dotnet run
```

### Build

```bash
dotnet build
```

## How to Play

1. Select a level from the level select screen
2. Click a **tower button** on the right panel to select a tower type
3. Click a **green cell** on the map to place the tower (brown path cells are blocked)
4. Towers automatically attack enemies in range
5. Survive all waves to win; lose all 20 lives and it's game over

### HUD

| Element | Description |
|---|---|
| Gold | Earned from kills; spent on towers |
| Lives | Lost when enemies reach the exit |
| Wave | Current wave / total waves |
| Status | Context hints and game info |

## Towers

### Basic

| Tower | Cost | DMG | Range | Speed | Special |
|---|---|---|---|---|---|
| Arrow | 50g | 15 | 3.5 | 1.8/s | Fast, cheap single-target |
| Cannon | 100g | 40 | 3.0 | 0.6/s | Splash radius 1.5 |
| Ice | 75g | 8 | 2.8 | 1.0/s | Slows enemies 50% for 2s |

### Advanced

| Tower | Cost | DMG | Range | Speed | Special |
|---|---|---|---|---|---|
| Multi-Shot | 150g | 12x3 | 3.0 | 1.0/s | Fires 3 arrows in a fan, each tracking a different enemy |
| Sniper | 130g | 50 | 5.0 | 0.5/s | 30% crit chance for 3x damage; longest range |
| Poison | 100g | 8 | 3.5 | 1.5/s | DOT: 20 dmg/s for 3s on hit |
| Sun | 175g | 18/s | 2.5 | — | Continuous AOE burn around the tower (no projectile) |

## Enemies

| Type | HP | Speed | Reward | Appearance |
|---|---|---|---|---|
| Basic | 100 | 1.8 | 10g | Crimson sphere |
| Fast | 60 | 3.5 | 15g | Gold sphere |
| Tank | 350 | 1.0 | 30g | Dark violet sphere (large) |

## Map Editor

The built-in map editor lets you create and modify maps with a live 3D preview:

- **Grid size** — adjustable columns and rows
- **Waypoints** — set start/end points and path waypoints by clicking cells
- **Wave config** — add waves, configure enemy types, counts, and spawn intervals
- **Test** — play-test your map directly from the editor
- **Save** — maps are stored as JSON files in the `Maps/` directory

## Maps

- 10 pre-built levels with increasing difficulty
- Player progress is saved automatically (highest unlocked level)
- Maps are JSON files and can be shared or edited manually

## License

MIT
