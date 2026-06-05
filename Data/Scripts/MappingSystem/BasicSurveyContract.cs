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
    public sealed class BasicSurveyContract : MappingContractHandler
    {
        public override MyDefinitionId DefinitionId { get; } = new MyDefinitionId(
            MyObjectBuilderType.ParseBackwardsCompatible("ContractTypeDefinition"), "MappingContract");

        public override int MaxPerStation => 1;

        public override void Spawn(
            IMyContractSystem system,
            long stationId,
            long contractBlockId,
            Vector3D stationPos,
            MyPlanet planet,
            HashSet<long> spawnedIds)
        {
            var cfg = Config.Instance;
            var radii = cfg.ContractRadiiMeters;
            float radius    = radii[MyRandom.Instance.Next(radii.Length)];
            double radiusKm = radius / 1000.0;

            // Offset survey center so repeat players can't win with their existing map data
            Vector3D up    = Vector3D.Normalize(stationPos - planet.PositionComp.GetPosition());
            Vector3D right = Vector3D.Cross(up, Vector3D.Up);
            if (right.LengthSquared() < 0.001)
                right = Vector3D.Cross(up, Vector3D.Forward);
            right = Vector3D.Normalize(right);
            Vector3D fwd = Vector3D.Normalize(Vector3D.Cross(right, up));

            double angle      = MyRandom.Instance.NextDouble() * Math.PI * 2;
            double offsetDist = radius * (0.5 + MyRandom.Instance.NextDouble() * 0.5);
            Vector3D candidate    = stationPos + (right * Math.Cos(angle) + fwd * Math.Sin(angle)) * offsetDist;
            Vector3D surveyCenter = planet.GetClosestSurfacePointGlobal(candidate);

            int reward          = (int)(radiusKm * radiusKm * cfg.ContractRewardPerRadiusKm2);
            int reputation      = (int)(radiusKm * cfg.ContractRepPerRadiusKm);
            int durationMinutes = (int)Math.Ceiling(radiusKm * radiusKm * cfg.ContractDurationPerRadiusKm2);

            string name = $"Know your surroundings ({radiusKm:F0}km)";
            string desc = $"Survey {radiusKm:F0}km of terrain in the area near this station using radio antennas. " +
                          $"[C:{surveyCenter.X:R},{surveyCenter.Y:R},{surveyCenter.Z:R}]";

            var contract = new MyContractCustom(
                DefinitionId,
                startBlockId: 0,
                moneyReward: reward,
                collateral: 0,
                duration: durationMinutes,
                name: name,
                description: desc,
                reputationReward: reputation,
                failReputationPrice: 0,
                endBlockId: contractBlockId != 0 ? (long?)contractBlockId : null);

            var result = system.AddContract(contract, stationId);
            if (result.Success)
            {
                spawnedIds.Add(result.ContractId);
                MyLog.Default.WriteLine($"{Config.LogPrefix} Added '{name}' to station {stationId}, id={result.ContractId}");
            }
            else
                MyLog.Default.WriteLine($"{Config.LogPrefix} Failed to add '{name}' to station {stationId}");
        }

        public override MappingContractMeta OnActivate(long contractId, long identityId, IMyContract contract)
        {
            Vector3D stationCenter = MappingContractSystem.GetStationPosition(contract);
            var custom = contract as IMyContractCustom;
            Vector3D surveyCenter  = ParseCenterFromDescription(custom?.Description, stationCenter);

            if (surveyCenter == Vector3D.Zero)
                MyLog.Default.WriteLine($"{Config.LogPrefix} Warning: cannot resolve survey center for contract {contractId}, will retry on scan");

            var meta = new MappingContractMeta
            {
                Center               = surveyCenter,
                StationCenter        = stationCenter,
                Radius               = RewardToRadius(contract.MoneyReward),
                ContractorIdentityId = identityId,
                MoneyReward          = contract.MoneyReward,
                ReputationReward     = contract.RewardReputation,
                FactionTag           = MappingContractSystem.GetFactionTag(contract)
            };

            if (surveyCenter != Vector3D.Zero)
                AddZoneGps(meta);

            return meta;
        }

        private static Vector3D ParseCenterFromDescription(string desc, Vector3D fallback)
        {
            if (string.IsNullOrEmpty(desc)) return fallback;

            int tagStart = desc.LastIndexOf("[C:");
            if (tagStart < 0) return fallback;

            int tagEnd = desc.IndexOf(']', tagStart);
            if (tagEnd < 0) return fallback;

            var parts = desc.Substring(tagStart + 3, tagEnd - tagStart - 3).Split(',');
            if (parts.Length != 3) return fallback;

            double x, y, z;
            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out x) ||
                !double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out y) ||
                !double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out z))
                return fallback;

            return new Vector3D(x, y, z);
        }

        // Backward-compat: radius wasn't persisted in older saves, recover it from reward value.
        public static float RewardToRadius(int reward)
        {
            double radiusKm = Math.Sqrt(reward / 120000.0);
            radiusKm = Math.Max(1.0, Math.Min(3.0, Math.Round(radiusKm)));
            return (float)(radiusKm * 1000.0);
        }
    }
}
