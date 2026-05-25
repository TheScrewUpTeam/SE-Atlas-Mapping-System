using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Contracts;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace TSUT.MappingSystem
{
    public sealed class RemoteSurveyContract : MappingContractHandler
    {
        private const string StorageKey = "AMS_PendingRemoteMeta";

        // Contracts that have been spawned but not yet accepted by a player.
        // Keyed by contractId; cleared when the player accepts or the contract is removed.
        private readonly Dictionary<long, MappingContractMeta> _pending =
            new Dictionary<long, MappingContractMeta>();

        public override MyDefinitionId DefinitionId { get; } = new MyDefinitionId(
            MyObjectBuilderType.ParseBackwardsCompatible("ContractTypeDefinition"), "RemoteSurveyContract");

        public override void Init(IMyContractSystem system, HashSet<long> spawnedIds)
        {
            LoadPending(spawnedIds);
        }

        public override void Spawn(
            IMyContractSystem system,
            long stationId,
            long contractBlockId,
            Vector3D stationPos,
            MyPlanet planet,
            HashSet<long> spawnedIds)
        {
            var random = MyRandom.Instance;
            var cfg    = Config.Instance;

            // Build a tangent plane from the planet's local up - same pattern as ProjectionHelper.GetLocalUp
            Vector3D up    = Vector3D.Normalize(stationPos - planet.PositionComp.GetPosition());
            Vector3D right = Vector3D.Cross(up, Vector3D.Up);
            if (right.LengthSquared() < 0.001)
                right = Vector3D.Cross(up, Vector3D.Forward);
            right = Vector3D.Normalize(right);
            Vector3D fwd = Vector3D.Normalize(Vector3D.Cross(right, up));

            double distanceM  = cfg.RemoteMinDistanceMeters + random.NextDouble() * (cfg.RemoteMaxDistanceMeters - cfg.RemoteMinDistanceMeters);
            double angle      = random.NextDouble() * Math.PI * 2.0;
            var candidate     = stationPos + (right * Math.Cos(angle) + fwd * Math.Sin(angle)) * distanceM;
            Vector3D zoneCenter = planet.GetClosestSurfacePointGlobal(candidate);

            var radii        = cfg.ContractRadiiMeters;
            float radius     = radii[random.Next(radii.Length)];
            double radiusKm  = radius / 1000.0;
            double distanceKm = distanceM / 1000.0;

            int reward          = (int)(distanceKm * cfg.RemoteRewardPerDistanceKm + radiusKm * radiusKm * cfg.ContractRewardPerRadiusKm2);
            int reputation      = (int)(radiusKm * cfg.ContractRepPerRadiusKm);
            int failReputation  = (int)(radiusKm * cfg.RemoteFailRepPerRadiusKm);
            int durationMinutes = (int)Math.Ceiling(distanceKm * cfg.RemoteDurationPerDistanceKm + radiusKm * radiusKm * cfg.ContractDurationPerRadiusKm2);

            string name = $"Distant survey ({radiusKm:F0}km)";
            string desc = $"Survey a {radiusKm:F0}km area located {distanceKm:F0}km from this station. Return the data here for reward.";

            var contract = new MyContractCustom(
                DefinitionId,
                startBlockId: 0,
                moneyReward: reward,
                collateral: 0,
                duration: durationMinutes,
                name: name,
                description: desc,
                reputationReward: reputation,
                failReputationPrice: failReputation,
                endBlockId: contractBlockId != 0 ? (long?)contractBlockId : null);

            var result = system.AddContract(contract, stationId);
            if (!result.Success)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Failed to add '{name}' to station {stationId}");
                return;
            }

            spawnedIds.Add(result.ContractId);
            _pending[result.ContractId] = new MappingContractMeta
            {
                Center        = zoneCenter,
                StationCenter = stationPos,
                Radius        = radius
            };
            SavePending();
            MyLog.Default.WriteLine($"{Config.LogPrefix} Added '{name}' to station {stationId}, id={result.ContractId}");
        }

        public override MappingContractMeta OnActivate(long contractId, long identityId, IMyContract contract)
        {
            MappingContractMeta meta;
            if (!_pending.TryGetValue(contractId, out meta))
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} OnActivate: no pending meta for remote survey {contractId}");
                return null;
            }

            meta.ContractorIdentityId = identityId;
            meta.MoneyReward          = contract?.MoneyReward ?? 0;
            meta.ReputationReward     = contract?.RewardReputation ?? 0;
            meta.FactionTag           = MappingContractSystem.GetFactionTag(contract);

            _pending.Remove(contractId);
            SavePending();

            AddZoneGps(meta);
            return meta;
        }

        public override void OnSpawnedRemoved(long contractId)
        {
            if (_pending.Remove(contractId))
                SavePending();
        }

        // ── Persistence ──────────────────────────────────────────────────────────

        private void SavePending()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kvp in _pending)
                {
                    var m = kvp.Value;
                    sb.Append($"{kvp.Key}:{m.Center.X:R},{m.Center.Y:R},{m.Center.Z:R}," +
                              $"{m.StationCenter.X:R},{m.StationCenter.Y:R},{m.StationCenter.Z:R},{m.Radius:R};");
                }
                MyAPIGateway.Utilities.SetVariable(StorageKey, sb.ToString());
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error saving pending remote meta: {ex.Message}");
            }
        }

        private void LoadPending(HashSet<long> spawnedIds)
        {
            try
            {
                string data;
                if (!MyAPIGateway.Utilities.GetVariable(StorageKey, out data) || string.IsNullOrEmpty(data))
                    return;

                _pending.Clear();
                foreach (var entry in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = entry.IndexOf(':');
                    if (colon < 0) continue;

                    long contractId;
                    if (!long.TryParse(entry.Substring(0, colon), out contractId)) continue;
                    if (!spawnedIds.Contains(contractId)) continue; // contract no longer active

                    var parts = entry.Substring(colon + 1).Split(',');
                    if (parts.Length != 7) continue;

                    double cx, cy, cz, scx, scy, scz;
                    float radius;
                    if (!double.TryParse(parts[0], out cx) || !double.TryParse(parts[1], out cy) ||
                        !double.TryParse(parts[2], out cz) || !double.TryParse(parts[3], out scx) ||
                        !double.TryParse(parts[4], out scy) || !double.TryParse(parts[5], out scz) ||
                        !float.TryParse(parts[6], out radius)) continue;

                    _pending[contractId] = new MappingContractMeta
                    {
                        Center        = new Vector3D(cx, cy, cz),
                        StationCenter = new Vector3D(scx, scy, scz),
                        Radius        = radius
                    };
                }
                MyLog.Default.WriteLine($"{Config.LogPrefix} Restored {_pending.Count} pending remote survey metas");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error loading pending remote meta: {ex.Message}");
            }
        }
    }
}
