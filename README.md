# 🐜 An Ant Colony City Builder

> **Note for the team:** This is a starter README template. Swap out the game name, screenshots, team names, license, and any placeholder text (marked with `[brackets]`) with your real details before publishing.

**"Build a thriving ant metropolis, one tunnel at a time."**

AntHaven is a 2D/isometric city-building simulation where you don't manage humans — you manage an ant colony. Dig tunnels, grow chambers, assign ant roles, gather food, defend against predators, and watch your colony grow from a handful of ants into a sprawling underground empire.

![Godot](https://img.shields.io/badge/Godot-4.x-478CBF?logo=godotengine&logoColor=white)
![C#](https://img.shields.io/badge/C%23-.NET-239120?logo=csharp&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-green)
![Status](https://img.shields.io/badge/status-in%20development-yellow)

---

## Table of Contents

- [About the Game](#about-the-game)
- [Features](#features)
- [Screenshots](#screenshots)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Installation](#installation)
  - [Running the Project](#running-the-project)
  - [Building a Release](#building-a-release)
- [How to Play](#how-to-play)
- [Project Structure](#project-structure)
- [Built With](#built-with)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [Team](#team)
- [License](#license)
- [Acknowledgments](#acknowledgments)
- [Contact](#contact)

---

## About the Game

Every great colony starts with a single queen and a handful of workers. In AntHaven, you play as the guiding "hivemind" behind an ant colony, responsible for:

- **Excavating** an ever-expanding underground network of tunnels and chambers
- **Assigning roles** to your ants — workers, foragers, soldiers, nurses, and builders
- **Managing resources** like food, larvae, and building material
- **Defending** your colony from predators, rival ant colonies, floods, and other environmental threats
- **Researching** new chamber types and colony upgrades as your population grows

The goal: grow from a tiny starter nest into a thriving underground metropolis capable of surviving whatever the surface world throws at you.

This project is built in **Godot 4** using **C#**, and is being developed by a small team as a passion project.

---

## Features

- 🐜 **Colony Simulation** — individual ants with roles, needs, and simple behavioral AI
- ⛏️ **Tunnel Digging** — carve out chambers and tunnels on a grid-based underground map
- 🌾 **Resource Economy** — balance food, building materials, and larvae production
- 🌗 **Day/Night Cycle** — surface conditions affect foraging risk and colony activity
- ⚔️ **Threats & Defense** — fend off anteaters, rival colonies, rain floods, and other hazards
- 🧬 **Tech Tree / Progression** — unlock new chamber types, ant castes, and colony upgrades
- 💾 **Save/Load System** — pick up your colony right where you left off
- 🎨 **Custom Art & Audio** — original pixel art and sound design *(placeholder — update as assets land)*

---

## Screenshots

> _Add gifs/screenshots here once you have in-engine footage. A short gameplay clip at the top of the README goes a long way on GitHub._

| Colony Overview | Tunnel Digging | Ant Role Assignment |
|---|---|---|
| `[screenshot placeholder]` | `[screenshot placeholder]` | `[screenshot placeholder]` |

---

## Getting Started

### Prerequisites

- [Godot 4.x (.NET/Mono build)](https://godotengine.org/download) — **not** the standard GDScript-only build
- [.NET SDK 8.0+](https://dotnet.microsoft.com/download) (or whichever version the project targets)
- Git (and optionally [Git LFS](https://git-lfs.com/) if the repo tracks large binary assets like sprites/audio)
- A code editor with C# support (Visual Studio, VS Code + C# extension, or Rider all work well with Godot)

### Installation

```bash
# Clone the repository
git clone https://github.com/[your-org]/AntHaven.git
cd AntHaven

# Restore .NET dependencies
dotnet restore
```

### Running the Project

1. Open **Godot 4 (.NET version)**
2. Click **Import**, then select the `project.godot` file in the cloned repo
3. Once imported, press **F5** (or the Play button) to run the game
4. On first run, Godot may prompt you to build the C# solution — allow it to do so

### Building a Release

From the Godot editor:
1. Go to **Project → Export**
2. Choose your target platform preset (Windows/Linux/macOS)
3. Click **Export Project** and select an output folder

*(Update this section once you've configured actual export presets and CI, if any.)*

---

## How to Play

| Action | Control |
|---|---|
| Pan camera | `WASD` / Right-click drag |
| Zoom | Scroll wheel |
| Dig tunnel | Left-click + drag on underground tiles |
| Select ant(s) | Left-click / box select |
| Assign role | Select ant → open role menu → choose role |
| Place structure | Open build menu → select structure → click tile |
| Pause / Resume | `Space` |

**Basic loop:** dig chambers → assign foragers to gather food → feed larvae to grow your population → assign new ants to roles → expand and defend.

*(Replace with your actual control scheme once input mapping is finalized.)*

---

## Project Structure

```
AntHaven/
├── Assets/               # Sprites, audio, fonts, and other raw assets
├── Scenes/               # .tscn scene files
│   ├── Ants/
│   ├── Colony/
│   ├── UI/
│   └── World/
├── Scripts/              # C# source
│   ├── Ants/             # Ant behavior, roles, pathfinding
│   ├── Colony/           # Resource management, colony state
│   ├── Systems/          # Core game systems (day/night, threats, tech tree)
│   └── UI/               # UI logic and view models
├── Resources/            # Godot .tres resources (configs, data tables)
├── project.godot
└── README.md
```

*(Adjust this to match your team's actual folder layout.)*

---

## Built With

- [Godot Engine 4.x](https://godotengine.org/) — game engine
- [C# / .NET](https://dotnet.microsoft.com/) — gameplay programming
- `[Aseprite / other art tool]` — pixel art
- `[FMOD / Godot AudioStreamPlayer / other]` — audio
- `[list any notable plugins, asset packs, or libraries here]`

---

## Roadmap

- [ ] Core colony simulation loop
- [ ] Tunnel digging and chamber system
- [ ] Ant AI, pathfinding, and role behaviors
- [ ] Resource economy (food, materials, larvae)
- [ ] Day/night cycle
- [ ] Threats & basic combat (predators, rival colonies)
- [ ] Tech tree / colony progression
- [ ] Save/load system
- [ ] Sound design & music pass
- [ ] UI/UX polish
- [ ] Steam page / demo release

See the [open issues](https://github.com/[your-org]/AntHaven/issues) for a full list of proposed features and known bugs.

---

## Contributing

This is currently a closed team project, but if that changes:

1. Fork the repo and create your branch from `main` (`git checkout -b feature/your-feature-name`)
2. Follow the project's C# style conventions (see `[link to style guide if you have one]`)
3. Commit your changes with clear, descriptive messages
4. Push to your branch and open a Pull Request
5. Link any related issues in your PR description

**Branch naming convention:** `feature/...`, `bugfix/...`, `chore/...`

---

## Team

| Name | Role |
|---|---|
| `[Name]` | Programming / Godot |
| `[Name]` | Programming / Gameplay Systems |
| `[Name]` | Art / Animation |
| `[Name]` | Sound / Music |
| `[Name]` | Game Design |

*(Update with real names, roles, and optionally links to socials/portfolios.)*

---

## License

This project is licensed under the `[MIT / GPL-3.0 / All Rights Reserved — choose one]` License. See the [LICENSE](LICENSE) file for details.

---

## Acknowledgments

- The Godot Engine community and documentation
- Games that inspired this project: *SimAnt*, *Empires of the Undergrowth*, *Rimworld*, `[add your own inspirations]`
- Any asset packs, fonts, or sound libraries used under license
- `[Anyone else you want to credit]`

---

## Contact

- Discord: `[invite link]`
- Itch.io: `[project page link]`
- Project lead: `[email or contact method]`





















ㅤㅤㅤ     I just want to get fucked like this, pleasseeeㅤㅤㅤㅤㅤㅤㅤ 
⠀⠀⠀⠀⠀⠀⠀⣠⣶⣶⣦⡀
⠀⠀⠀⠀⠀⠀⢰⣿⣿⣿⣿⣿⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀
⠀⠀⠀⠀⠀⠀⠀⠻⣿⣿⡿⠋⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀
⠀⠀⠀⠀⠀⠀⣴⣶⣶⣄⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀
⠀⠀⠀⠀⠀⣸⣿⣿⣿⣿⡄⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀
⠀⠀⠀⠀⢀⣿⣿⣿⣿⣿⣧⠀⠀⠀
⠀⠀⠀⠀⣼⣿⣿⣿⡿⣿⣿⣆⠀⠀⠀⠀⠀⠀⣠⣴⣶⣤⡀⠀
⠀⠀⠀⢰⣿⣿⣿⣿⠃⠈⢻⣿⣦⠀⠀⠀⠀⣸⣿⣿⣿⣿⣷⠀
⠀⠀⠀⠘⣿⣿⣿⡏⣴⣿⣷⣝⢿⣷⢀⠀⢀⣿⣿⣿⣿⡿⠋⠀
⠀⠀⠀⠀⢿⣿⣿⡇⢻⣿⣿⣿⣷⣶⣿⣿⣿⣿⣿⣷⠀⠀⠀⠀
⠀⠀⠀⠀⢸⣿⣿⣇⢸⣿⣿⡟⠙⠛⠻⣿⣿⣿⣿⡇⠀⠀⠀⠀
⣴⣿⣿⣿⣿⣿⣿⣿⣠⣿⣿⡇⠀⠀⠀⠉⠛⣽⣿⣇⣀⣀⣀⠀
⠙⠻⠿⠿⠿⠿⠿⠟⠿⠿⠿⠇⠀⠀⠀⠀⠀⠻⠿⠿⠛⠛⠛⠃


⣿⣿⣿⣿⠛⠛⠉⠄⠁⠄⠄⠉⠛⢿⣿⣿⣿⣿⣿⣿⣿
⣿⣿⡟⠁⠄⠄⠄⠄⠄⠄⠄⠄⠄⠄⣿⣿⣿⣿⣿⣿⣿
⣿⣿⡇⠄⠄⠄⠐⠄⠄⠄⠄⠄⠄⠄⠠⣿⣿⣿⣿⣿⣿
⣿⣿⡇⠄⢀⡀⠠⠃⡐⡀⠠⣶⠄⠄⢀⣿⣿⣿⣿⣿⣿
⣿⣿⣶⠄⠰⣤⣕⣿⣾⡇⠄⢛⠃⠄⢈⣿⣿⣿⣿⣿⣿
⣿⣿⣿⡇⢀⣻⠟⣻⣿⡇⠄⠧⠄⢀⣾⣿⣿⣿⣿⣿⣿
⣿⣿⣿⣟⢸⣻⣭⡙⢄⢀⠄⠄⠄⠈⢹⣯⣿⣿⣿⣿⣿
⣿⣿⣿⣭⣿⣿⣿⣧⢸⠄⠄⠄⠄⠄⠈⢸⣿⣿⣿⣿⣿
⣿⣿⣿⣼⣿⣿⣿⣽⠘⡄⠄⠄⠄⠄⢀⠸⣿⣿⣿⣿⣿
⡿⣿⣳⣿⣿⣿⣿⣿⠄⠓⠦⠤⠤⠤⠼⢸⣿⣿⣿⣿⣿
⡹⣧⣿⣿⣿⠿⣿⣿⣿⣿⣿⣿⣿⢇⣓⣾⣿⣿⣿⣿⣿
⡞⣸⣿⣿⢏⣼⣶⣶⣶⣶⣤⣶⡤⠐⣿⣿⣿⣿⣿⣿⣿
⣯⣽⣛⠅⣾⣿⣿⣿⣿⣿⡽⣿⣧⡸⢿⣿⣿⣿⣿⣿⣿
⣿⣿⣿⡷⠹⠛⠉⠁⠄⠄⠄⠄⠄⠄⠐⠛⠻⣿⣿⣿⣿
⣿⣿⣿⠃⠄⠄⠄⠄⠄⣠⣤⣤⣤⡄⢤⣤⣤⣤⡘⠻⣿
⣿⣿⡟⠄⠄⣀⣤⣶⣿⣿⣿⣿⣿⣿⣆⢻⣿⣿⣿⡎⠝
⣿⡏⠄⢀⣼⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⡎⣿⣿⣿⣿⠐
⣿⡏⣲⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⢇⣿⣿⣿⡟⣼
⣿⡠⠜⣿⣿⣿⣿⣟⡛⠿⠿⠿⠿⠟⠃⠾⠿⢟⡋⢶⣿
⣿⣧⣄⠙⢿⣿⣿⣿⣿⣿⣷⣦⡀⢰⣾⣿⣿⡿⢣⣿⣿
⣿⣿⣿⠂⣷⣶⣬⣭⣭⣭⣭⣵⢰⣴⣤⣤⣶⡾⢐⣿⣿
⣿⣿⣿⣷⡘⣿⣿⣿⣿⣿⣿⣿⢸⣿⣿⣿⣿⢃⣼⣿⣿
---

*Made with 🐜 and Godot.*
