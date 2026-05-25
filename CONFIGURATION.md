# Atlas Mapping System - Configuration Guide

Config file: `MappingSystem_Config.xml` in your world's **Storage** folder (created automatically on first load).

Edit the file, save, then reload the world to apply changes.

> **Auto-update:** if `AutoUpdateConfig` is `true` and the mod version changes, the config resets to defaults. Set it to `false` to preserve your settings across mod updates (you may need to add new fields manually).

---

## General

| Field | Default | Description |
|-------|---------|-------------|
| `AutoUpdateConfig` | `true` | Reset config to defaults when the mod version changes. |
| `CellSize` | `100` | Map grid cell size in meters. Smaller = higher resolution map, more memory and scan time. |
| `AlignToGravity` | `true` | Orient the map plane to the planet's gravity direction rather than world axes. |
| `MarkerUpdateIntervalSeconds` | `10` | How often (seconds) GPS markers are refreshed on the HUD. |

---

## Scanning

Scan radius is derived from the antenna's broadcast range:

```
Scan radius (large grid) = AntennaRange × ScanRadiusFactorLarge
Scan radius (small grid) = AntennaRange × ScanRadiusFactorSmall
```

Power draw during a scan scales with `ScanPowerMultiplierLarge` / `ScanPowerMultiplierSmall`.

| Field | Default | Description |
|-------|---------|-------------|
| `ScanRadiusFactorSmall` | `0.02` | Scan radius = antenna range × this factor (small grid). |
| `ScanRadiusFactorLarge` | `0.05` | Scan radius = antenna range × this factor (large grid). |
| `ScanPowerMultiplierSmall` | `1000.0` | Additional power draw multiplier during a scan (small grid). |
| `ScanPowerMultiplierLarge` | `5000.0` | Additional power draw multiplier during a scan (large grid). |
| `MaxRaycastsPerTick` | `50` | Raycasts performed per game tick. Lower = smoother but slower scans. Raise on powerful servers. |
| `MaxScanPunchThroughs` | `5` | How many overlapping voxel layers (e.g., cave ceilings) a single ray can punch through before stopping. |
| `PunchThroughOffset` | `0.5` | Distance (meters) to advance the ray origin after a punch-through hit, to avoid self-intersection. |

---

## Map Storage Limits

Each antenna stores map data in chunks. These limits cap how many chunks can accumulate before old ones are discarded.

| Field | Default | Description |
|-------|---------|-------------|
| `MaxChunksSmallGrid` | `20` | Max stored map chunks for antennas on a small grid. |
| `MaxChunksLargeGrid` | `200` | Max stored map chunks for antennas on a large grid. |
| `MaxChunksStaticGrid` | `500000` | Max stored map chunks for antennas on a static (station) grid. Effectively unlimited. |

---

## Contract System

Contracts spawn automatically at NPC ground stations (within 1 km of a planet surface) that have a Contract Block. The system tops up to 2 contracts per type per station on each economy tick.

### Shared Reward Formula

```
reward   = radius_km²  × ContractRewardPerRadiusKm2
rep      = radius_km   × ContractRepPerRadiusKm
duration = radius_km²  × ContractDurationPerRadiusKm2  (minutes)
```

| Field | Default | Description |
|-------|---------|-------------|
| `ContractRadiiMeters` | `[1000, 2000, 3000]` | Pool of survey zone radii (meters) randomly picked when spawning a contract. |
| `ContractRewardPerRadiusKm2` | `120000.0` | Credits per km² of survey radius. |
| `ContractRepPerRadiusKm` | `100.0` | Reputation per km of survey radius. |
| `ContractDurationPerRadiusKm2` | `3.0` | Contract duration (minutes) per km² of survey radius. |

### Remote Survey Contracts

Remote contracts require the player to travel 10–50 km from the station to scan a distant zone.

```
reward   += distance_km × RemoteRewardPerDistanceKm
duration += distance_km × RemoteDurationPerDistanceKm
```

| Field | Default | Description |
|-------|---------|-------------|
| `RemoteMinDistanceMeters` | `10000.0` | Minimum distance (meters) from the station for the remote zone. |
| `RemoteMaxDistanceMeters` | `50000.0` | Maximum distance (meters) from the station for the remote zone. |
| `RemoteRewardPerDistanceKm` | `15000.0` | Bonus credits per km of zone distance. |
| `RemoteFailRepPerRadiusKm` | `50.0` | Reputation penalty per km of radius on contract failure/abandon. |
| `RemoteDurationPerDistanceKm` | `0.5` | Additional duration (minutes) per km of zone distance. |

### Dangerous Survey Contracts

Dangerous contracts target hostile NPC bases on the same planet. They require collateral and pay more.

Applied **on top of** the shared reward formula:

| Field | Default | Description |
|-------|---------|-------------|
| `ContractDangerousRewardMultiplier` | `1.5` | Reward multiplier vs. a basic survey of the same radius. |
| `ContractDangerousCollateralFraction` | `0.2` | Collateral = reward × this fraction. |
| `ContractDangerousDurationMultiplier` | `2.0` | Duration multiplier vs. a basic survey of the same radius. |
| `ContractDangerousMinDistanceMeters` | `10000.0` | Hostile base must be at least this far (meters) from the issuing station. |

---

## Example Config

```xml
<?xml version="1.0"?>
<Config>
  <AutoUpdateConfig>true</AutoUpdateConfig>
  <CellSize>100</CellSize>
  <AlignToGravity>true</AlignToGravity>
  <ScanRadiusFactorSmall>0.02</ScanRadiusFactorSmall>
  <ScanPowerMultiplierSmall>1000</ScanPowerMultiplierSmall>
  <ScanRadiusFactorLarge>0.05</ScanRadiusFactorLarge>
  <ScanPowerMultiplierLarge>5000</ScanPowerMultiplierLarge>
  <MaxRaycastsPerTick>50</MaxRaycastsPerTick>
  <MaxScanPunchThroughs>5</MaxScanPunchThroughs>
  <PunchThroughOffset>0.5</PunchThroughOffset>
  <MarkerUpdateIntervalSeconds>10</MarkerUpdateIntervalSeconds>
  <MaxChunksSmallGrid>20</MaxChunksSmallGrid>
  <MaxChunksLargeGrid>200</MaxChunksLargeGrid>
  <MaxChunksStaticGrid>500000</MaxChunksStaticGrid>
  <ContractRadiiMeters>
    <float>1000</float>
    <float>2000</float>
    <float>3000</float>
  </ContractRadiiMeters>
  <ContractRewardPerRadiusKm2>120000</ContractRewardPerRadiusKm2>
  <ContractRepPerRadiusKm>100</ContractRepPerRadiusKm>
  <ContractDurationPerRadiusKm2>3</ContractDurationPerRadiusKm2>
  <RemoteRewardPerDistanceKm>15000</RemoteRewardPerDistanceKm>
  <RemoteFailRepPerRadiusKm>50</RemoteFailRepPerRadiusKm>
  <RemoteDurationPerDistanceKm>0.5</RemoteDurationPerDistanceKm>
  <RemoteMinDistanceMeters>10000</RemoteMinDistanceMeters>
  <RemoteMaxDistanceMeters>50000</RemoteMaxDistanceMeters>
  <ContractDangerousRewardMultiplier>1.5</ContractDangerousRewardMultiplier>
  <ContractDangerousCollateralFraction>0.2</ContractDangerousCollateralFraction>
  <ContractDangerousDurationMultiplier>2</ContractDangerousDurationMultiplier>
  <ContractDangerousMinDistanceMeters>10000</ContractDangerousMinDistanceMeters>
</Config>
```
