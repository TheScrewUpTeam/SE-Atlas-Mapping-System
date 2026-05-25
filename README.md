# Atlas Mapping System for Space Engineers

Are you flying blind across alien terrain? Do your maps stop where the radar does?
The **Atlas Mapping System** lets you actively scan, record, and visualize planetary terrain - on LCD screens, cockpit panels, or a holographic 3D projection table.

---

## What Does This Mod Do?

- **Radio antennas scan terrain** using spherical raycasts, recording surface elevation.
- **Map data lives on the antenna** - each antenna accumulates its own survey data across multiple scans.
- **Display anywhere:** render your map on LCDs, cockpits, programmable blocks, or any text surface via the `MapApp` built-in app.
- **Holotable support:** use a Projector block to display a live 3D terrain relief projection floating above a table.
- **Share map data** between antennas on the same grid via wireless download.
- **Economy integration:** NPC stations at ground bases offer survey contracts - Basic, Remote, and Dangerous - for credits and reputation.
- **Fully configurable:** all scan radii, power costs, contract rewards, and chunk limits are tunable per save.
- **Multiplayer ready:** scan state, map data, and display settings sync across all players.

---

## Features

- **Spherical terrain scanning** - fires raycasts in a uniform sphere pattern, records voxel surface hits with height.
- **Incremental scanning** - large scans are spread across multiple ticks so they don't freeze the game.
- **Per-antenna map storage** - data persists through saves; accumulated scans build a growing picture of explored terrain.
- **Antenna data exchange** - download map data from another antenna on the same grid. Note that grids should be connected through connectors or mechanical blocks.
- **Flat map display** - assign the `MapApp` LCD script to any text surface. Zoom (1–500 m/px), pan (X/Y ±50 km), and optional heading-up rotation.
- **3D holotable display** - projector block renders a color-coded elevation relief map. Adjustable resolution (32–128 px), zoom (10–2000 m), pan, vertical scale (0.1–10×), and auto-compensate mode that tracks ship movement between updates.
- **Three survey contract types** spawning at NPC ground stations:
  - **Basic Survey** - map the area near the station.
  - **Remote Survey** - map a zone 10–50 km away.
  - **Dangerous Survey** - map hostile NPC territory; includes collateral.
- **Auto-updating config** - version mismatch resets to defaults automatically.
- **Visual scan feedback** - scan progress visible in terminal custom info and via network state packets.

---

## How to Use

### Scanning

1. Place a **Radio Antenna** on your ship or base.
2. Power it on. The scan radius scales with the antenna's broadcast range:
   - **Large grid:** `antenna range × 0.05` (e.g., 50 km antenna → 2.5 km scan radius)
   - **Small grid:** `antenna range × 0.02` (e.g., 50 km antenna → 1 km scan radius)
3. Open the antenna's terminal. Click **Start Map Scan**.
4. Wait for the scan to complete - progress shows in the terminal info bar.
5. Scan again anytime to add new data or update changed terrain.

### Displaying on Screens

1. On any LCD, cockpit screen, programmable block, or similar surface, set the **Content** to **Application** and choose `MapApp`.
2. Open the block terminal. Under **Source Antenna**, select the antenna whose data you want to display.
3. Adjust **Map Zoom**, **Map Pan X/Y**, and **Rotate Map** to taste.
4. Blocks with multiple surfaces: use **Next Map Screen** to cycle which surface is being configured.

### Holotable (3D Projection)

1. Place a **Projector** block. It will be recognized as a holotable.
2. Open its terminal. Check **Enable Map Projection**.
3. Select the **Source Antenna** from the list.
4. Adjust **Map Resolution** (32–128 px), **Map Zoom** (10–2000 m), **Vertical Scale**, and **Pan X/Y**.
5. Enable **Auto Compensate Movement** to have the projection visually track ship movement between update ticks.

### Antenna Data Exchange

1. Open the terminal of the antenna you want to receive data.
2. Under **Antenna to exchange**, select another working antenna on the same grid.
3. Click **Download Map Data** - the selected antenna's map is merged into this one.

### Survey Contracts

1. Land near an **NPC ground station** with a Contract Block.
2. Contracts appear automatically: Basic Survey, Remote Survey, and Dangerous Survey (when hostile NPC bases are present on the planet).
3. Accept a contract - a GPS marker is added to your HUD showing the survey zone.
4. Scan the zone with your antenna (75% coverage required).
5. Return within 130 m of the station to collect your reward.

---

## Configuration

See [CONFIGURATION.md](CONFIGURATION.md) for all available parameters.

The config file is saved as `MappingSystem_Config.xml` in your world's **Storage** folder. Edit it, then save and reload the world to apply changes.

---

## Multiplayer

- Scanning runs **client-side** (requester performs raycasts) and streams results to the server.
- Map data syncs from server to all clients on change.
- Display settings (zoom, pan, rotation) sync across all players in real time.
- Contract state and GPS markers are server-authoritative.

---

## Credits

Mod by **TSUT** (The Screw-Up Team)

---

## License

MIT License. Use freely, modify, include in modpacks - no attribution required but appreciated.
