using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace TSUT.MappingSystem
{
    public partial class MapSession
    {
        private void PerformRadioSync()
        {
            if (!MyAPIGateway.Session.IsServer) return;

            var entities = new HashSet<VRage.ModAPI.IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyRadioAntenna);

            var antennaList = new List<IMyRadioAntenna>();
            foreach (var e in entities) antennaList.Add((IMyRadioAntenna)e);

            for (int i = 0; i < antennaList.Count; i++)
            {
                for (int j = i + 1; j < antennaList.Count; j++)
                {
                    var a = antennaList[i];
                    var b = antennaList[j];

                    if (a.IsWorking && b.IsWorking && AreAntennasConnected(a, b))
                    {
                        SyncAntennas(a, b);
                    }
                }
            }
        }

        private bool AreAntennasConnected(IMyRadioAntenna a, IMyRadioAntenna b)
        {
            double distSq = Vector3D.DistanceSquared(a.WorldMatrix.Translation, b.WorldMatrix.Translation);
            double range = Math.Max(a.Radius, b.Radius);
            return distSq <= range * range;
        }

        private void SyncAntennas(IMyRadioAntenna a, IMyRadioAntenna b)
        {
            var storageA = a.Components.Get<MapStorageComponent>();
            var storageB = b.Components.Get<MapStorageComponent>();

            if (storageA == null || storageB == null) return;

            // Simple "merge" sync - Server authoritative logic
            bool changedA = MergeMapGrids(storageB.Grid, storageA.Grid);
            bool changedB = MergeMapGrids(storageA.Grid, storageB.Grid);

            if (changedA)
            {
                storageA.MarkDirty();
                MapSession.Instance.Networking.SendToAll(new PacketChunkSync(a.EntityId, storageA.Grid.GetSerializedChunks()));
            }

            if (changedB)
            {
                storageB.MarkDirty();
                MapSession.Instance.Networking.SendToAll(new PacketChunkSync(b.EntityId, storageB.Grid.GetSerializedChunks()));
            }
        }

        private bool MergeMapGrids(MapGrid source, MapGrid target)
        {
            bool anyChanged = false;
            foreach (var sourceKvp in source.Chunks)
            {
                MapChunk targetChunk;
                if (!target.Chunks.TryGetValue(sourceKvp.Key, out targetChunk))
                {
                    target.Chunks[sourceKvp.Key] = new MapChunk(sourceKvp.Key);
                    targetChunk = target.Chunks[sourceKvp.Key];
                    anyChanged = true;
                }

                foreach (var cellKvp in sourceKvp.Value.Cells)
                {
                    if (!targetChunk.Cells.ContainsKey(cellKvp.Key))
                    {
                        targetChunk.Cells[cellKvp.Key] = cellKvp.Value;
                        anyChanged = true;
                    }
                }
            }
            return anyChanged;
        }
    }
}
