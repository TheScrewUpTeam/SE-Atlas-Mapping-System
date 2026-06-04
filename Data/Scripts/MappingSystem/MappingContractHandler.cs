using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace TSUT.MappingSystem
{
    public abstract class MappingContractHandler
    {
        public abstract MyDefinitionId DefinitionId { get; }
        public virtual int MaxPerStation => 2;

        public virtual void Init(IMyContractSystem system, HashSet<long> spawnedIds) { }

        public virtual void Unload() { }

        public abstract void Spawn(
            IMyContractSystem system,
            long stationId,
            long contractBlockId,
            Vector3D stationPos,
            MyPlanet planet,
            HashSet<long> spawnedIds);

        public abstract MappingContractMeta OnActivate(long contractId, long identityId, IMyContract contract);

        public virtual void OnCoverageMet(long contractId, MappingContractMeta meta, ulong steamId)
        {
            RemoveGps(meta);
            AddReturnGps(meta);

            if (steamId != 0)
                MapSession.Instance.Networking.SendToPlayer(
                    new PacketNotification("Survey complete. Return to station for reward.", 8000), steamId);
        }

        public virtual void OnSpawnedRemoved(long contractId) { }

        public virtual void CleanUp(MappingContractMeta meta)
        {
            RemoveGps(meta);
        }

        // ── GPS helpers ──────────────────────────────────────────────────────────

        protected static void AddReturnGps(MappingContractMeta meta)
        {
            var gps = MyAPIGateway.Session.GPS.Create(
                "Survey: Return to Station",
                "Data sync and reward awaiting.",
                meta.StationCenter,
                showOnHud: true);
            gps.GPSColor = new Color(255, 140, 0);
            MyAPIGateway.Session.GPS.AddGps(meta.ContractorIdentityId, gps);
            meta.SurveyGps = gps;
        }

        protected static void AddZoneGps(MappingContractMeta meta)
        {
            float radiusKm = meta.Radius / 1000f;
            var gps = MyAPIGateway.Session.GPS.Create(
                $"Survey Zone ({radiusKm:F0}km)",
                $"Scan {radiusKm:F0}km of terrain in this area.",
                meta.Center,
                showOnHud: true);
            gps.GPSColor = new Color(255, 140, 0);
            MyAPIGateway.Session.GPS.AddGps(meta.ContractorIdentityId, gps);
            meta.SurveyGps = gps;
        }

        protected static void RemoveGps(MappingContractMeta meta)
        {
            if (meta.SurveyGps == null) return;
            MyAPIGateway.Session.GPS.RemoveGps(meta.ContractorIdentityId, meta.SurveyGps);
            meta.SurveyGps = null;
        }
    }
}
