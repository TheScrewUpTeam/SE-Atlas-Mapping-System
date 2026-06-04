using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace TSUT.MappingSystem
{
    public class ScanInfo
    {
        public IMyEntity Antenna;
        public ulong PlayerId;
        public int TotalRays;
        public int ScanId;
        public int CurrentRayIndex;
    }

    public class ScanRequest
    {
        public IMyEntity Antenna;
        public Vector3D Position;
        public float Radius;
        public int CurrentRayIndex;
        public int TotalRays;
        public List<CellResult> BatchResults = new List<CellResult>();
        public int LastBatchTick;
    }

    public class ScanScheduler
    {
        public event Action<IMyRadioAntenna> ScanCompleted;

        private readonly Dictionary<long, ScanInfo> _activeScans = new Dictionary<long, ScanInfo>();
        private readonly List<ScanRequest> _clientScans = new List<ScanRequest>();
        private static int _nextScanId = 0;
        private const int VoxelCollisionLayer = 28;
        private int _ticks = 0;

        public ScanInfo GetActiveScan(long antennaId)
        {
            ScanInfo info;
            return _activeScans.TryGetValue(antennaId, out info) ? info : null;
        }

        public void EnqueueScan(IMyEntity antenna, float radius, ulong playerId)
        {
            if (_activeScans.ContainsKey(antenna.EntityId)) return;

            var terminalBlock = antenna as IMyTerminalBlock;
            if (terminalBlock == null || !terminalBlock.IsWorking) return;

            int rayCount = (int)MathHelper.Clamp((4 * Math.PI * radius * radius) / 100, 100, 5000000);
            int scanId = ++_nextScanId;
            int durationTicks = Math.Max(30, rayCount / Config.Instance.MaxRaycastsPerTick);

            _activeScans[antenna.EntityId] = new ScanInfo
            {
                Antenna = antenna,
                PlayerId = playerId,
                TotalRays = rayCount,
                ScanId = scanId,
                CurrentRayIndex = 0
            };

            var entry = antenna.GameLogic?.GetAs<ScannerEntry>();
            if (entry != null)
            {
                entry.IsScanning = true;
                entry.UpdateSink();
                (antenna as IMyTerminalBlock)?.RefreshCustomInfo();
            }

            var approvedPacket = new PacketScanApproved(antenna.EntityId, antenna.WorldMatrix.Translation, radius, rayCount, scanId);
            if (playerId == MyAPIGateway.Multiplayer.ServerId)
                approvedPacket.Handle(MyAPIGateway.Multiplayer.ServerId);
            else
                MapSession.Instance.Networking.SendToPlayer(approvedPacket, playerId);

            MapSession.Instance.Networking.SendToAll(new PacketScanStarted(
                antenna.EntityId, antenna.WorldMatrix.Translation, radius, rayCount, durationTicks, scanId));
        }

        public void StartClientScan(long entityId, Vector3D pos, float radius, int totalRays)
        {
            var entity = MyAPIGateway.Entities.GetEntityById(entityId);
            if (entity == null) return;

            if (_clientScans.Exists(s => s.Antenna.EntityId == entityId)) return;

            _clientScans.Add(new ScanRequest
            {
                Antenna = entity,
                Position = pos,
                Radius = radius,
                TotalRays = totalRays,
                CurrentRayIndex = 0,
                LastBatchTick = _ticks
            });
        }

        public void ProcessScanBatch(long entityId, List<CellResult> results, int currentRayIndex, bool isFinal)
        {
            var entity = MyAPIGateway.Entities.GetEntityById(entityId);
            if (entity == null) return;

            var storage = entity.Components.Get<MapStorageComponent>();
            if (storage != null && results != null && results.Count > 0)
            {
                foreach (var result in results)
                {
                    MapCell cell = new MapCell { Height = result.Height, Flags = result.Flags };
                    storage.Grid.AddCell(result.Position, cell, Config.Instance.CellSize);
                }
                storage.MarkDirty();
            }

            if (MyAPIGateway.Session.IsServer)
            {
                ScanInfo info;
                if (_activeScans.TryGetValue(entityId, out info))
                {
                    info.CurrentRayIndex = currentRayIndex;
                    if (isFinal)
                        CompleteScan(entityId, info);
                }
            }
        }

        public void Update()
        {
            _ticks++;

            if (MyAPIGateway.Session.IsServer)
                UpdateServer();

            if (_clientScans.Count > 0)
                UpdateClient();
        }

        private void UpdateServer()
        {
            var toCancel = new List<long>();
            foreach (var kvp in _activeScans)
            {
                var antenna = kvp.Value.Antenna as IMyRadioAntenna;
                if (antenna == null || !antenna.IsWorking)
                    toCancel.Add(kvp.Key);
            }
            foreach (var id in toCancel)
                CancelScan(id);
        }

        private void CancelScan(long antennaId)
        {
            ScanInfo info;
            if (!_activeScans.TryGetValue(antennaId, out info)) return;

            var entry = info.Antenna.GameLogic?.GetAs<ScannerEntry>();
            if (entry != null)
            {
                entry.IsScanning = false;
                entry.UpdateSink();
                (info.Antenna as IMyTerminalBlock)?.RefreshCustomInfo();
            }
            _activeScans.Remove(antennaId);
            MapSession.Instance.Networking.SendToAll(new PacketScanEnded(antennaId, info.ScanId));
        }

        private void CompleteScan(long antennaId, ScanInfo info)
        {
            var entry = info.Antenna.GameLogic?.GetAs<ScannerEntry>();
            if (entry != null)
            {
                entry.IsScanning = false;
                entry.UpdateSink();
                (info.Antenna as IMyTerminalBlock)?.RefreshCustomInfo();
            }
            _activeScans.Remove(antennaId);
            MapSession.Instance.Networking.SendToAll(new PacketScanEnded(antennaId, info.ScanId));

            var antenna = info.Antenna as IMyRadioAntenna;
            if (antenna != null)
                ScanCompleted?.Invoke(antenna);
        }

        public void CancelScanForAntenna(long antennaId)
        {
            CancelScan(antennaId);
        }

        private void UpdateClient()
        {
            for (int i = _clientScans.Count - 1; i >= 0; i--)
            {
                var scan = _clientScans[i];
                int maxRays = Config.Instance.MaxRaycastsPerTick;
                int casted = 0;

                while (scan.CurrentRayIndex < scan.TotalRays && casted < maxRays)
                {
                    PerformClientRaycast(scan);
                    scan.CurrentRayIndex++;
                    casted++;
                }

                bool isFinished = scan.CurrentRayIndex >= scan.TotalRays;
                if (_ticks - scan.LastBatchTick >= 60 || isFinished)
                {
                    if (scan.BatchResults.Count > 0 || isFinished)
                    {
                        var packet = new PacketScanBatch(scan.Antenna.EntityId, scan.BatchResults, scan.CurrentRayIndex, isFinished);
                        MapSession.Instance.Networking.SendToServer(packet);
                        scan.BatchResults = new List<CellResult>();
                        scan.LastBatchTick = _ticks;

                        // Mark dirty once per batch, not per-ray
                        scan.Antenna.Components.Get<MapStorageComponent>()?.MarkDirty();
                    }

                    if (isFinished)
                    {
                        MyLog.Default.WriteLine($"{Config.LogPrefix} Client scan completed for {scan.Antenna.EntityId}");
                        // Guard: on listen server, SendToServer above delivers synchronously and
                        // CancelClientScan may have already removed this entry via PacketScanEnded.
                        if (i < _clientScans.Count && _clientScans[i] == scan)
                            _clientScans.RemoveAt(i);
                    }
                }
            }
        }

        public void CancelClientScan(long antennaId)
        {
            for (int i = _clientScans.Count - 1; i >= 0; i--)
            {
                var scan = _clientScans[i];
                if (scan.Antenna.EntityId != antennaId) continue;

                if (scan.BatchResults.Count > 0)
                {
                    var packet = new PacketScanBatch(scan.Antenna.EntityId, scan.BatchResults, scan.CurrentRayIndex, false);
                    MapSession.Instance.Networking.SendToServer(packet);
                }
                _clientScans.RemoveAt(i);
            }
        }

        private void PerformClientRaycast(ScanRequest request)
        {
            double phi = Math.PI * (3 - Math.Sqrt(5));
            double y = -1 + (request.CurrentRayIndex / (double)Math.Max(1, request.TotalRays - 1)) * 2;
            double radiusAtY = Math.Sqrt(1 - y * y);
            double theta = phi * request.CurrentRayIndex;

            Vector3D dir = new Vector3D(Math.Cos(theta) * radiusAtY, y, Math.Sin(theta) * radiusAtY);
            Vector3D origin = request.Position;
            Vector3D currentStart = origin;
            Vector3D end = origin + dir * request.Radius;

            IHitInfo hit;
            int safetyCounter = 0;

            var storage = request.Antenna.Components.Get<MapStorageComponent>();

            while (safetyCounter < Config.Instance.MaxScanPunchThroughs && MyAPIGateway.Physics.CastRay(currentStart, end, out hit, VoxelCollisionLayer))
            {
                if (hit.HitEntity is IMyVoxelBase)
                {
                    MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(hit.Position);
                    byte flags = 2;
                    short height = (short)hit.Position.Y;

                    if (planet != null && Vector3D.Distance(hit.Position, planet.PositionComp.GetPosition()) <= planet.MaximumRadius + 5.0)
                    {
                        flags = 1;
                        height = (short)(Vector3D.Distance(hit.Position, planet.PositionComp.GetPosition()) - planet.AverageRadius);
                    }

                    request.BatchResults.Add(new CellResult { Position = hit.Position, Height = height, Flags = flags });

                    if (storage != null)
                    {
                        MapCell cell = new MapCell { Height = height, Flags = flags };
                        storage.Grid.AddCell(hit.Position, cell, Config.Instance.CellSize);
                    }
                    break;
                }
                else
                {
                    currentStart = hit.Position + (dir * 0.1);
                    safetyCounter++;
                    if (Vector3D.DistanceSquared(origin, currentStart) >= request.Radius * request.Radius) break;
                }
            }
        }
    }
}
