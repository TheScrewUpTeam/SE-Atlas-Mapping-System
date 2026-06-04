using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace TSUT.MappingSystem
{
    public class ClientContractInfo
    {
        public long ContractId;
        public Vector3D Center;
        public float Radius;
        public long AcceptedTicks;
        public Vector3D StationCenter;
        public bool CoverageMet;
    }

    public class MappingContractMeta
    {
        public Vector3D Center;        // survey area center
        public Vector3D StationCenter; // where player returns for reward
        public float Radius;
        public long ContractorIdentityId;
        public long AcceptedTicks;     // DateTime.UtcNow.Ticks when contract was accepted
        public bool CoverageMet;
        public int MoneyReward;
        public int ReputationReward;
        public string FactionTag;
        public int SurveyGpsHash;            // persisted, 0 = none
        public IMyGps SurveyGps;             // runtime only, not persisted
        public MappingContractHandler Handler; // runtime only, not persisted
    }

    public class MappingContractSystem
    {
        public const float CoverageThreshold = 0.75f;
        public const float StationAntennaRadius = 500f;
        private const string StorageKey       = "AMS_ContractMeta";
        private const string SpawnedKey       = "AMS_SpawnedIds";
        private int _periodicTopUpInterval;

        private readonly Dictionary<long, MappingContractMeta> _contracts = new Dictionary<long, MappingContractMeta>();
        private readonly HashSet<long> _spawnedIds = new HashSet<long>();

        private readonly List<MappingContractHandler> _handlers = new List<MappingContractHandler>
        {
            new BasicSurveyContract(),
            new RemoteSurveyContract(),
            new HostileSurveyContract()
        };
        private readonly Dictionary<MyDefinitionId, MappingContractHandler> _handlerMap;

        private IMyContractSystem _system;

        private int _periodicTopUpTimer;

        public MappingContractSystem()
        {
            _handlerMap = new Dictionary<MyDefinitionId, MappingContractHandler>();
            foreach (var h in _handlers)
                _handlerMap[h.DefinitionId] = h;
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public void Init()
        {
            if (!MyAPIGateway.Session.IsServer) return;

            _system = MyAPIGateway.ContractSystem;
            if (_system == null) return;

            _system.CustomActivateContract += OnActivate;
            _system.CustomCleanUp         += OnCleanUp;
            _system.CustomFinishFor       += OnFinishFor;
            _system.CustomFailFor         += OnFailFor;

            MapSession.Instance.Scheduler.ScanCompleted += OnScanCompleted;

            _periodicTopUpInterval = Math.Max(60, MyAPIGateway.Session.SessionSettings.EconomyTickInSeconds);

            LoadSpawnedIds();
            foreach (var handler in _handlers)
                handler.Init(_system, _spawnedIds);
            MyLog.Default.WriteLine($"{Config.LogPrefix} Contract system ready. Economy interval: {_periodicTopUpInterval}s, {_handlers.Count} handlers, {_spawnedIds.Count} pending IDs restored");
        }

        public void Unload()
        {
            if (_system == null) return;

            _system.CustomActivateContract -= OnActivate;
            _system.CustomCleanUp         -= OnCleanUp;
            _system.CustomFinishFor       -= OnFinishFor;
            _system.CustomFailFor         -= OnFailFor;

            if (MapSession.Instance?.Scheduler != null)
                MapSession.Instance.Scheduler.ScanCompleted -= OnScanCompleted;

            foreach (var handler in _handlers)
                handler.Unload();
        }

        // ── Spawning ─────────────────────────────────────────────────────────────

        private struct StationInfo
        {
            public long StationId, BlockId;
            public Vector3D Position;
            public MyPlanet Planet;
        }

        public void SpawnContractsAtNPCStations()
        {
            if (_system == null) return;

            var stations = new List<StationInfo>();
            foreach (var faction in MyAPIGateway.Session.Factions.Factions.Values)
            {
                if (!faction.IsEveryoneNpc()) continue;

                foreach (var station in faction.Stations)
                {
                    long blockId = FindContractBlockId(station.StationEntityId);
                    if (blockId == 0) continue;

                    var stationEntity = MyAPIGateway.Entities.GetEntityById(station.StationEntityId);
                    if (stationEntity == null) continue;

                    Vector3D stationPos = stationEntity.GetPosition();
                    MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(stationPos);
                    if (planet == null) continue;
                    if (Vector3D.Distance(stationPos, planet.GetClosestSurfacePointGlobal(stationPos)) > 1000.0) continue;

                    stations.Add(new StationInfo { StationId = station.Id, BlockId = blockId, Position = stationPos, Planet = planet });
                }
            }

            if (stations.Count == 0)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} No ground NPC station with contract block found");
                return;
            }

            MyLog.Default.WriteLine($"{Config.LogPrefix} Checking contracts at {stations.Count} ground NPC station(s)");
            PruneStaleSpawnedIds();
            foreach (var s in stations)
                TopUpMissingContracts(s.StationId, s.BlockId, s.Position, s.Planet);
        }

        private void PruneStaleSpawnedIds()
        {
            var toRemove = new List<long>();
            foreach (var id in _spawnedIds)
            {
                if (_system.GetContractById(id) == null)
                    toRemove.Add(id);
            }
            if (toRemove.Count == 0) return;

            foreach (var id in toRemove)
            {
                _spawnedIds.Remove(id);
                foreach (var h in _handlers)
                    h.OnSpawnedRemoved(id);
            }
            SaveSpawnedIds();
            MyLog.Default.WriteLine($"{Config.LogPrefix} Pruned {toRemove.Count} stale pending contract IDs");
        }

        private void TopUpMissingContracts(long stationId, long blockId, Vector3D stationPos, MyPlanet planet)
        {
            var pendingByDef = new Dictionary<MyDefinitionId, int>();
            foreach (var id in _spawnedIds)
            {
                // Count only contracts that belong to THIS station's contract block
                var custom = _system.GetContractById(id) as IMyContractCustom;
                if (custom?.EndBlockId != blockId) continue;

                var defId = _system.GetContractDefinitionId(id);
                if (!defId.HasValue) continue;
                int count;
                pendingByDef.TryGetValue(defId.Value, out count);
                pendingByDef[defId.Value] = count + 1;
            }

            bool anySpawned = false;
            foreach (var handler in _handlers)
            {
                int count;
                pendingByDef.TryGetValue(handler.DefinitionId, out count);
                if (count < handler.MaxPerStation)
                {
                    handler.Spawn(_system, stationId, blockId, stationPos, planet, _spawnedIds);
                    anySpawned = true;
                }
            }

            if (anySpawned)
            {
                SaveSpawnedIds();
                MyLog.Default.WriteLine($"{Config.LogPrefix} Topped up contracts at station {stationId}, now {_spawnedIds.Count} pending");
            }
            else
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Contracts OK: {_spawnedIds.Count} pending at station {stationId}");
            }
        }

        // ── Update loop ───────────────────────────────────────────────────────────

        private int _updateTick;

        public void Update()
        {
            if (!MyAPIGateway.Session.IsServer) return;
            if (++_updateTick % 60 != 0) return;

            if (++_periodicTopUpTimer >= _periodicTopUpInterval)
            {
                _periodicTopUpTimer = 0;
                MyLog.Default.WriteLine($"{Config.LogPrefix} Periodic top-up check, {_spawnedIds.Count} pending");
                SpawnContractsAtNPCStations();
            }

            if (_contracts.Count == 0) return;

            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            var toComplete = new List<KeyValuePair<long, MappingContractMeta>>();
            foreach (var kvp in _contracts)
            {
                var meta = kvp.Value;
                IMyPlayer contractor = null;
                foreach (var p in players)
                {
                    if (p.IdentityId == meta.ContractorIdentityId) { contractor = p; break; }
                }
                if (contractor?.Character == null) continue;
                if (Vector3D.Distance(contractor.Character.GetPosition(), meta.StationCenter) > 130.0) continue;

                float freshCoverage = ComputeFreshCoverage(meta, contractor);
                if (freshCoverage >= CoverageThreshold)
                    toComplete.Add(kvp);
            }

            foreach (var kvp in toComplete)
                CompleteContract(kvp.Key, kvp.Value, GetSteamIdByIdentity(kvp.Value.ContractorIdentityId));
        }

        // ── Contract system event handlers ────────────────────────────────────────

        private void OnActivate(long contractId, long identityId)
        {
            var rawDefId = _system.GetContractDefinitionId(contractId);
            if (rawDefId == null) return;

            MappingContractHandler handler;
            if (!_handlerMap.TryGetValue(rawDefId.Value, out handler)) return;

            var contract = _system.GetContractById(contractId) as IMyContract;
            if (contract == null)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} OnActivate: GetContractById null for {contractId}");
                return;
            }

            var meta = handler.OnActivate(contractId, identityId, contract);
            if (meta == null) return;

            meta.Handler = handler;
            meta.AcceptedTicks = DateTime.UtcNow.Ticks;
            _contracts[contractId] = meta;
            _spawnedIds.Remove(contractId);
            SaveSpawnedIds();
            SaveToStorage();
            MyLog.Default.WriteLine($"{Config.LogPrefix} Contract {contractId} accepted by {identityId}");

            ulong steamId = GetSteamIdByIdentity(identityId);
            if (steamId != 0)
                MapSession.Instance.Networking.SendToPlayer(
                    new PacketContractSync(contractId, meta.Center, meta.Radius, meta.AcceptedTicks, meta.StationCenter, meta.CoverageMet),
                    steamId);
        }

        private void OnScanCompleted(IMyRadioAntenna antenna)
        {
            if (_system == null || _contracts.Count == 0) return;

            bool anyChanged = false;
            long ownerId = antenna.OwnerId;

            foreach (var kvp in _contracts)
            {
                var meta = kvp.Value;
                if (meta.CoverageMet) continue;

                // Resolve unresolved center
                if (meta.Center == Vector3D.Zero)
                {
                    var contract = _system.GetContractById(kvp.Key) as IMyContract;
                    if (contract != null) { meta.Center = GetStationPosition(contract); meta.StationCenter = meta.Center; }
                    if (meta.Center == Vector3D.Zero) continue;
                    anyChanged = true;
                }

                // Check if this antenna's owner is the contractor or in their faction
                if (!IsOwnerOrFaction(ownerId, meta.ContractorIdentityId)) continue;

                // Fresh coverage check from just this antenna (notify when enough fresh data gathered)
                var storage = antenna.Components.Get<MapStorageComponent>();
                if (storage?.Grid == null) continue;

                float fresh = ComputeFreshCoverageFromGrid(meta, storage.Grid);
                if (fresh < CoverageThreshold) continue;

                meta.CoverageMet = true;
                anyChanged = true;
                ulong steamId = GetSteamIdByIdentity(meta.ContractorIdentityId);
                meta.Handler?.OnCoverageMet(kvp.Key, meta, steamId);

                // Sync updated CoverageMet to client
                if (steamId != 0)
                    MapSession.Instance.Networking.SendToPlayer(
                        new PacketContractSync(kvp.Key, meta.Center, meta.Radius, meta.AcceptedTicks, meta.StationCenter, true),
                        steamId);
            }

            if (anyChanged) SaveToStorage();
        }

        public void SendContractsToPlayer(ulong steamId)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, p => p.SteamUserId == steamId);
            if (players.Count == 0) return;
            SendContractsToPlayer(steamId, players[0].IdentityId);
        }

        public void SendContractsToPlayer(ulong steamId, long identityId)
        {
            foreach (var kvp in _contracts)
            {
                var meta = kvp.Value;
                if (meta.ContractorIdentityId != identityId) continue;
                MapSession.Instance.Networking.SendToPlayer(
                    new PacketContractSync(kvp.Key, meta.Center, meta.Radius, meta.AcceptedTicks, meta.StationCenter, meta.CoverageMet),
                    steamId);
            }
        }

        private float ComputeFreshCoverage(MappingContractMeta meta, IMyPlayer contractor)
        {
            // Find all faction/player antennas within station range, pool fresh cells
            var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(meta.ContractorIdentityId);

            var entities = new HashSet<VRage.ModAPI.IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyRadioAntenna);

            int cellSize = Config.Instance.CellSize;
            int sampled = 0, found = 0;

            // Collect grids to check (deduplicated)
            var grids = new List<MapGrid>();
            foreach (var entity in entities)
            {
                var antenna = entity as IMyRadioAntenna;
                if (antenna == null) continue;
                if (Vector3D.Distance(antenna.WorldMatrix.Translation, meta.StationCenter) > StationAntennaRadius) continue;
                if (!IsOwnerOrFaction(antenna.OwnerId, meta.ContractorIdentityId)) continue;
                var storage = antenna.Components.Get<MapStorageComponent>();
                if (storage?.Grid != null) grids.Add(storage.Grid);
            }

            if (grids.Count == 0) return 0f;

            for (float dx = -meta.Radius; dx <= meta.Radius; dx += cellSize)
            {
                for (float dz = -meta.Radius; dz <= meta.Radius; dz += cellSize)
                {
                    if (dx * dx + dz * dz > meta.Radius * meta.Radius) continue;
                    sampled++;

                    var worldPos = meta.Center + new Vector3D(dx, 0, dz);
                    var cellPos = ProjectionHelper.WorldToGrid(worldPos, cellSize);

                    foreach (var grid in grids)
                    {
                        var cell = grid.GetCell(cellPos);
                        if (cell == null) continue;
                        if (grid.GetChunkWrittenTicks(cellPos) < meta.AcceptedTicks) continue;
                        found++;
                        break;
                    }
                }
            }

            return sampled > 0 ? (float)found / sampled : 0f;
        }

        private float ComputeFreshCoverageFromGrid(MappingContractMeta meta, MapGrid grid)
        {
            int cellSize = Config.Instance.CellSize;
            int sampled = 0, found = 0;

            for (float dx = -meta.Radius; dx <= meta.Radius; dx += cellSize)
            {
                for (float dz = -meta.Radius; dz <= meta.Radius; dz += cellSize)
                {
                    if (dx * dx + dz * dz > meta.Radius * meta.Radius) continue;
                    sampled++;
                    var cellPos = ProjectionHelper.WorldToGrid(meta.Center + new Vector3D(dx, 0, dz), cellSize);
                    if (grid.GetCell(cellPos) == null) continue;
                    if (grid.GetChunkWrittenTicks(cellPos) < meta.AcceptedTicks) continue;
                    found++;
                }
            }

            return sampled > 0 ? (float)found / sampled : 0f;
        }

        private bool IsOwnerOrFaction(long ownerId, long contractorIdentityId)
        {
            if (ownerId == contractorIdentityId) return true;
            var contractorFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(contractorIdentityId);
            if (contractorFaction == null) return false;
            var ownerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
            return ownerFaction?.FactionId == contractorFaction.FactionId;
        }

        private void OnFinishFor(long contractId, long identityId, int rewardeeCount)
        {
            MyLog.Default.WriteLine($"{Config.LogPrefix} Mapping contract {contractId} completed by {identityId}");
            ulong steamId = GetSteamIdByIdentity(identityId);
            if (steamId != 0)
                MapSession.Instance.Networking.SendToPlayer(new PacketContractEnded(contractId), steamId);
        }

        private void OnFailFor(long contractId, long identityId, bool isAbandon)
        {
            MyLog.Default.WriteLine($"{Config.LogPrefix} Mapping contract {contractId} {(isAbandon ? "abandoned" : "failed")} by {identityId}");
            ulong steamId = GetSteamIdByIdentity(identityId);
            if (steamId != 0)
                MapSession.Instance.Networking.SendToPlayer(new PacketContractEnded(contractId), steamId);
        }

        private void OnCleanUp(long contractId)
        {
            MappingContractMeta meta;
            if (_contracts.TryGetValue(contractId, out meta))
            {
                meta.Handler?.CleanUp(meta);
                MyLog.Default.WriteLine($"{Config.LogPrefix} Active contract {contractId} cleaned up");
                ulong steamId = GetSteamIdByIdentity(meta.ContractorIdentityId);
                if (steamId != 0)
                    MapSession.Instance.Networking.SendToPlayer(new PacketContractEnded(contractId), steamId);
            }
            _contracts.Remove(contractId);
            SaveToStorage();
        }

        // ── Contract completion ───────────────────────────────────────────────────

        private void CompleteContract(long contractId, MappingContractMeta meta, ulong contractorSteamId)
        {
            var liveContract = _system.GetContractById(contractId) as IMyContract;
            liveContract?.ContractCondition?.FinalizeCondition();

            bool finished = _system.TryFinishCustomContract(contractId);
            if (!finished)
                MyLog.Default.WriteLine($"{Config.LogPrefix} TryFinishCustomContract({contractId}) failed");

            meta.Handler?.CleanUp(meta);

            if (finished && contractorSteamId != 0)
            {
                string msg = $"Survey synced! +{meta.MoneyReward} cr, +{meta.ReputationReward} rep to [{meta.FactionTag}]";
                MapSession.Instance.Networking.SendToPlayer(new PacketNotification(msg, 8000), contractorSteamId);
            }
        }

        // ── Helpers (internal so contract handlers can call them) ─────────────────

        internal static Vector3D GetStationPosition(IMyContract contract)
        {
            var custom = contract as IMyContractCustom;
            if (custom?.EndBlockId == null) return Vector3D.Zero;
            var block = MyAPIGateway.Entities.GetEntityById(custom.EndBlockId.Value);
            return block?.GetPosition() ?? Vector3D.Zero;
        }

        internal static string GetFactionTag(IMyContract contract)
        {
            var custom = contract as IMyContractCustom;
            if (custom?.EndBlockId == null) return "?";
            var block = MyAPIGateway.Entities.GetEntityById(custom.EndBlockId.Value) as IMyCubeBlock;
            if (block?.CubeGrid == null || block.CubeGrid.BigOwners.Count == 0) return "?";
            var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(block.CubeGrid.BigOwners[0]);
            return faction?.Tag ?? "?";
        }

        private static ulong GetSteamIdByIdentity(long identityId)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (var p in players)
                if (p.IdentityId == identityId) return p.SteamUserId;
            return 0;
        }

        private static float CalculateCoverage(Vector3D center, float radius, MapGrid grid)
        {
            int cellSize = Config.Instance.CellSize;
            int sampled = 0, found = 0;

            for (float dx = -radius; dx <= radius; dx += cellSize)
            {
                for (float dz = -radius; dz <= radius; dz += cellSize)
                {
                    if (dx * dx + dz * dz > radius * radius) continue;

                    sampled++;
                    var worldPos = center + new Vector3D(dx, 0, dz);
                    var cellPos  = ProjectionHelper.WorldToGrid(worldPos, cellSize);

                    if (grid.GetCell(cellPos) != null)
                        found++;
                }
            }

            return sampled > 0 ? (float)found / sampled : 0f;
        }

        private long FindContractBlockId(long stationGridEntityId)
        {
            var grid = MyAPIGateway.Entities.GetEntityById(stationGridEntityId) as IMyCubeGrid;
            if (grid == null) return 0;

            var termSys = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            if (termSys == null) return 0;

            var blocks = new List<IMyTerminalBlock>();
            termSys.GetBlocksOfType<IMyTerminalBlock>(blocks,
                b => b.BlockDefinition.TypeId.ToString().Contains("ContractBlock"));

            return blocks.Count > 0 ? blocks[0].EntityId : 0;
        }


        // ── Storage ───────────────────────────────────────────────────────────────

        private void SaveToStorage()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kvp in _contracts)
                {
                    var m = kvp.Value;
                    sb.Append($"{kvp.Key}:{m.Center.X:R},{m.Center.Y:R},{m.Center.Z:R}," +
                              $"{m.StationCenter.X:R},{m.StationCenter.Y:R},{m.StationCenter.Z:R}," +
                              $"{m.Radius:R},{m.ContractorIdentityId},{(m.CoverageMet ? 1 : 0)},{m.MoneyReward},{m.ReputationReward},{m.AcceptedTicks},{m.SurveyGpsHash};");
                }
                MyAPIGateway.Utilities.SetVariable(StorageKey, sb.ToString());
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error saving contract meta: {ex.Message}");
            }
        }

        private void SaveSpawnedIds()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var id in _spawnedIds)
                    sb.Append(id).Append(';');
                MyAPIGateway.Utilities.SetVariable(SpawnedKey, sb.ToString());
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error saving spawned ids: {ex.Message}");
            }
        }

        private void LoadSpawnedIds()
        {
            try
            {
                string data;
                if (!MyAPIGateway.Utilities.GetVariable(SpawnedKey, out data) || string.IsNullOrEmpty(data))
                    return;

                _spawnedIds.Clear();
                foreach (var part in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    long id;
                    if (long.TryParse(part, out id))
                        _spawnedIds.Add(id);
                }
                MyLog.Default.WriteLine($"{Config.LogPrefix} Restored {_spawnedIds.Count} spawned contract ids");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error loading spawned ids: {ex.Message}");
            }
        }

        public void LoadContractMetas()
        {
            try
            {
                string data;
                if (!MyAPIGateway.Utilities.GetVariable(StorageKey, out data) || string.IsNullOrEmpty(data))
                    return;

                _contracts.Clear();
                foreach (var entry in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = entry.IndexOf(':');
                    if (colon < 0) continue;

                    long contractId;
                    if (!long.TryParse(entry.Substring(0, colon), out contractId)) continue;

                    var parts = entry.Substring(colon + 1).Split(',');
                    if (parts.Length != 5 && parts.Length != 8 && parts.Length != 11 && parts.Length != 12 && parts.Length != 13) continue;

                    double cx, cy, cz;
                    if (!double.TryParse(parts[0], out cx) ||
                        !double.TryParse(parts[1], out cy) ||
                        !double.TryParse(parts[2], out cz)) continue;

                    var center = new Vector3D(cx, cy, cz);
                    Vector3D stationCenter;
                    float radius;
                    long contractorId;
                    bool coverageMet = false;
                    int moneyReward = 0, repReward = 0;
                    long acceptedTicks = 0;
                    int gpsHash = 0;

                    if (parts.Length == 5)
                    {
                        if (!float.TryParse(parts[3], out radius) ||
                            !long.TryParse(parts[4], out contractorId)) continue;
                        stationCenter = center;
                    }
                    else if (parts.Length == 8)
                    {
                        int cm, mr, rr;
                        if (!float.TryParse(parts[3], out radius) ||
                            !long.TryParse(parts[4], out contractorId) ||
                            !int.TryParse(parts[5], out cm) ||
                            !int.TryParse(parts[6], out mr) ||
                            !int.TryParse(parts[7], out rr)) continue;
                        stationCenter = center;
                        coverageMet   = cm != 0;
                        moneyReward   = mr;
                        repReward     = rr;
                    }
                    else // 11, 12, or 13
                    {
                        double scx, scy, scz;
                        int cm, mr, rr;
                        if (!double.TryParse(parts[3], out scx) ||
                            !double.TryParse(parts[4], out scy) ||
                            !double.TryParse(parts[5], out scz) ||
                            !float.TryParse(parts[6], out radius) ||
                            !long.TryParse(parts[7], out contractorId) ||
                            !int.TryParse(parts[8], out cm) ||
                            !int.TryParse(parts[9], out mr) ||
                            !int.TryParse(parts[10], out rr)) continue;
                        stationCenter = new Vector3D(scx, scy, scz);
                        coverageMet   = cm != 0;
                        moneyReward   = mr;
                        repReward     = rr;
                        if (parts.Length >= 12)
                        {
                            long at;
                            if (long.TryParse(parts[11], out at)) acceptedTicks = at;
                        }
                        if (parts.Length >= 13)
                        {
                            int gh;
                            if (int.TryParse(parts[12], out gh)) gpsHash = gh;
                        }
                    }

                    if (!_system.IsContractActive(contractId)) continue;

                    var liveContract = _system.GetContractById(contractId) as IMyContract;

                    var rawDefId = _system.GetContractDefinitionId(contractId);
                    MappingContractHandler handler = null;
                    if (rawDefId.HasValue)
                        _handlerMap.TryGetValue(rawDefId.Value, out handler);

                    _contracts[contractId] = new MappingContractMeta
                    {
                        Center               = center,
                        StationCenter        = stationCenter,
                        Radius               = radius,
                        ContractorIdentityId = contractorId,
                        AcceptedTicks        = acceptedTicks,
                        CoverageMet          = coverageMet,
                        SurveyGpsHash        = gpsHash,
                        MoneyReward          = moneyReward > 0 ? moneyReward : (liveContract?.MoneyReward ?? 0),
                        ReputationReward     = repReward > 0 ? repReward : (liveContract?.RewardReputation ?? 0),
                        FactionTag           = liveContract != null ? GetFactionTag(liveContract) : "?",
                        Handler              = handler
                    };
                }

                MyLog.Default.WriteLine($"{Config.LogPrefix} Restored {_contracts.Count} mapping contracts");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error loading contract meta: {ex.Message}");
            }
        }
    }
}
