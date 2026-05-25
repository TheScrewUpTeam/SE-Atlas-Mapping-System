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
    public class ScanRequest
    {
        public IMyEntity Antenna;
        public Vector3D Position;
        public float Radius;
        public ulong RequestingPlayer;
        public int CurrentRayIndex;
        public int TotalRays;
        public List<CellResult> BatchResults = new List<CellResult>();
        public int LastBatchTick;
    }

    public class ScanScheduler
    {
        public event Action<IMyRadioAntenna> ScanCompleted;

        private readonly List<ScanRequest> _activeScans = new List<ScanRequest>();
        private readonly List<ScanRequest> _clientScans = new List<ScanRequest>();
        private const int VoxelCollisionLayer = 28;
        private int _ticks = 0;

        public ScanRequest GetActiveScan(long antennaId)
        {
            return _activeScans.Find(s => s.Antenna.EntityId == antennaId);
        }

        public void EnqueueScan(IMyEntity antenna, float radius, ulong playerId)
        {
            // Only check if THIS antenna is already scanning
            if (GetActiveScan(antenna.EntityId) != null) return;

            var terminalBlock = antenna as IMyTerminalBlock;
            if (terminalBlock == null || !terminalBlock.IsWorking) return;

            int rayCount = (int)MathHelper.Clamp((4 * Math.PI * radius * radius) / 100, 100, 5000000);

            var request = new ScanRequest
            {
                Antenna = antenna,
                Position = antenna.WorldMatrix.Translation,
                Radius = radius,
                RequestingPlayer = playerId,
                TotalRays = rayCount,
                CurrentRayIndex = 0
            };

            _activeScans.Add(request);

            var entry = antenna.GameLogic?.GetAs<ScannerEntry>();
            if (entry != null)
            {
                entry.IsScanning = true;
                entry.UpdateSink();
                (antenna as IMyTerminalBlock)?.RefreshCustomInfo();
            }

            var startPacket = new PacketScanStart(antenna.EntityId, request.Position, request.Radius, request.TotalRays);
            if (playerId == MyAPIGateway.Multiplayer.ServerId)
            {
                startPacket.Handle(MyAPIGateway.Multiplayer.ServerId);
            }
            else
            {
                // Send only to requester to avoid everyone raycasting at once
                MapSession.Instance.Networking.SendToPlayer(startPacket, playerId);
                MapSession.Instance.Networking.SendToAll(new PacketScanState(antenna.EntityId, true, 0, rayCount, false, 0));
            }

            int durationTicks = Math.Max(30, request.TotalRays / Config.Instance.MaxRaycastsPerTick);
            MapSession.Instance.Networking.SendToAll(new PacketScanVisual(request.Position, request.Radius, durationTicks, antenna.EntityId));
        }

        public void StartClientScan(long entityId, VRageMath.Vector3D pos, float radius, int totalRays)
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

        public void ProcessScanResults(long entityId, List<CellResult> results, int currentRayIndex)
        {
            var entity = MyAPIGateway.Entities.GetEntityById(entityId);
            if (entity == null) return;

            var storage = entity.Components.Get<MapStorageComponent>();
            if (storage == null) return;

            if (results != null && results.Count > 0)
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
                var scan = GetActiveScan(entityId);
                if (scan != null)
                {
                    scan.CurrentRayIndex = currentRayIndex;
                }
                
                MapSession.Instance.Networking.SendToAll(new PacketScanState(entityId, true, currentRayIndex, scan?.TotalRays ?? 0));
            }
        }

        public void Update()
        {
            _ticks++;

            if (MyAPIGateway.Session.IsServer)
            {
                UpdateServer();
            }
            
            if (_clientScans.Count > 0)
            {
                UpdateClient();
            }
        }

        private void UpdateServer()
        {
            for (int i = _activeScans.Count - 1; i >= 0; i--)
            {
                var scan = _activeScans[i];
                
                var antenna = scan.Antenna as IMyRadioAntenna;
                if (antenna == null || !antenna.IsWorking)
                {
                    CancelScan(scan, i);
                    continue;
                }

                if (scan.CurrentRayIndex >= scan.TotalRays)
                {
                    CompleteScan(scan, i);
                }
            }
        }

        private void CancelScan(ScanRequest scan, int index)
        {
            var entry = scan.Antenna.GameLogic?.GetAs<ScannerEntry>();
            if (entry != null)
            {
                entry.IsScanning = false;
                entry.UpdateSink();
                (scan.Antenna as IMyTerminalBlock)?.RefreshCustomInfo();
            }
            MapSession.Instance.Networking.SendToAll(new PacketScanState(scan.Antenna.EntityId, false, scan.CurrentRayIndex, scan.TotalRays));
            _activeScans.RemoveAt(index);
        }

        private void CompleteScan(ScanRequest scan, int index)
        {
            var entry = scan.Antenna.GameLogic?.GetAs<ScannerEntry>();
            if (entry != null)
            {
                entry.IsScanning = false;
                entry.UpdateSink();
                (scan.Antenna as IMyTerminalBlock)?.RefreshCustomInfo();
            }
            MapSession.Instance.Networking.SendToAll(new PacketScanState(scan.Antenna.EntityId, false, scan.TotalRays, scan.TotalRays));
            _activeScans.RemoveAt(index);

            var antenna = scan.Antenna as IMyRadioAntenna;
            if (antenna != null)
                ScanCompleted?.Invoke(antenna);
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

                // Send batch every ~1s (60 ticks) or if finished
                bool isFinished = scan.CurrentRayIndex >= scan.TotalRays;
                if (_ticks - scan.LastBatchTick >= 60 || isFinished)
                {
                    if (scan.BatchResults.Count > 0 || isFinished)
                    {
                        var packet = new PacketScanResults(scan.Antenna.EntityId, scan.BatchResults, scan.CurrentRayIndex);
                        MapSession.Instance.Networking.SendToServer(packet);
                        
                        scan.BatchResults = new List<CellResult>();
                        scan.LastBatchTick = _ticks;
                    }

                    if (isFinished)
                    {
                        MyLog.Default.WriteLine($"{Config.LogPrefix} Client-side scan loop COMPLETED for {scan.Antenna.EntityId}");
                        _clientScans.RemoveAt(i);
                    }
                }
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
                    var voxel = hit.HitEntity as IMyVoxelBase;
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
                        storage.MarkDirty();
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

        public bool IsPending(long antennaId) => false; // Queue removed
    }
}
