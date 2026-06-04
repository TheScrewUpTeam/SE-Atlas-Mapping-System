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
        public IMyCubeGrid AntennaCubeGrid;
        public MatrixD GravityRotation;
        public MyPlanet Planet;
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

            MyPlanet nearestPlanet = MyGamePruningStructure.GetClosestPlanet(antenna.WorldMatrix.Translation);
            int cellSize = Config.Instance.CellSize;
            int rayCount = nearestPlanet != null
                ? (int)MathHelper.Clamp(Math.PI * radius * radius / (cellSize * cellSize) * Config.Instance.ScanOversampleFactor, 100, 5000000)
                : (int)MathHelper.Clamp(4 * Math.PI * radius * radius / 100, 100, 5000000);
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

            IMyCubeGrid antennaCubeGrid = (entity as IMyTerminalBlock)?.CubeGrid;
            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(pos);

            MatrixD gravityRotation = MatrixD.Identity;
            if (planet != null)
            {
                Vector3D localDown = Vector3D.Normalize(planet.PositionComp.GetPosition() - pos);
                Vector3D worldDown = new Vector3D(0, -1, 0);
                Vector3D axis = Vector3D.Cross(worldDown, localDown);
                double axisLen = axis.Length();
                if (axisLen > 1e-6)
                {
                    double angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(worldDown, localDown), -1.0, 1.0));
                    gravityRotation = MatrixD.CreateFromAxisAngle(axis / axisLen, angle);
                }
                else if (Vector3D.Dot(worldDown, localDown) < 0)
                {
                    gravityRotation = MatrixD.CreateFromAxisAngle(Vector3D.Right, Math.PI);
                }
            }

            _clientScans.Add(new ScanRequest
            {
                Antenna = entity,
                Position = pos,
                Radius = radius,
                TotalRays = totalRays,
                CurrentRayIndex = 0,
                LastBatchTick = _ticks,
                AntennaCubeGrid = antennaCubeGrid,
                GravityRotation = gravityRotation,
                Planet = planet
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
            // Downward hemisphere only: y from -1 to 0
            double y = -1.0 + (request.CurrentRayIndex / (double)Math.Max(1, request.TotalRays - 1));
            double radiusAtY = Math.Sqrt(1.0 - y * y);
            double theta = phi * request.CurrentRayIndex;

            Vector3D dir = new Vector3D(Math.Cos(theta) * radiusAtY, y, Math.Sin(theta) * radiusAtY);
            dir = Vector3D.TransformNormal(dir, request.GravityRotation);

            if (request.Planet != null)
            {
                Vector3D planetCenter = request.Planet.PositionComp.GetPosition();
                Vector3D originLocal = request.Position - planetCenter;

                double dotOD = Vector3D.Dot(originLocal, dir);
                double c = originLocal.LengthSquared() - (double)request.Planet.AverageRadius * request.Planet.AverageRadius;
                double disc = dotOD * dotOD - c;
                if (disc < 0) return;

                double t = -dotOD - Math.Sqrt(disc);
                if (t < 0) t = -dotOD + Math.Sqrt(disc);
                if (t < 0 || t > request.Radius) return;

                Vector3 localNominal = (Vector3)(originLocal + dir * t);
                Vector3 surfaceLocal = request.Planet.GetClosestSurfacePointLocal(ref localNominal);
                Vector3D surfaceWorld = planetCenter + (Vector3D)surfaceLocal;

                if (Vector3D.DistanceSquared(surfaceWorld, request.Position) > (double)request.Radius * request.Radius)
                    return;

                Vector3D surfaceUp = Vector3D.Normalize((Vector3D)surfaceLocal);
                Vector3D losStart = surfaceWorld + surfaceUp * 0.5;
                Vector3D losEnd = request.Position;
                Vector3D losDir = Vector3D.Normalize(losEnd - losStart);

                bool losBlocked = false;
                IHitInfo hit;
                Vector3D current = losStart;

                while (MyAPIGateway.Physics.CastRay(current, losEnd, out hit))
                {
                    if (Vector3D.DistanceSquared(hit.Position, losEnd) < 0.25) break;

                    var hitGrid = hit.HitEntity as IMyCubeGrid;
                    if (hitGrid != null && request.AntennaCubeGrid != null && hitGrid.IsSameConstructAs(request.AntennaCubeGrid))
                    {
                        current = hit.Position + losDir * 0.5;
                        continue;
                    }

                    losBlocked = true;
                    break;
                }

                if (losBlocked) return;

                short height = (short)(surfaceLocal.Length() - request.Planet.AverageRadius);
                var storage = request.Antenna.Components.Get<MapStorageComponent>();
                request.BatchResults.Add(new CellResult { Position = surfaceWorld, Height = height, Flags = 1 });
                if (storage != null)
                    storage.Grid.AddCell(surfaceWorld, new MapCell { Height = height, Flags = 1 }, Config.Instance.CellSize);
                return;
            }

            // Fallback: original raycast for asteroids (no planet)
            Vector3D origin = request.Position;
            Vector3D fallbackStart = origin;
            Vector3D end = origin + dir * request.Radius;
            var fallbackStorage = request.Antenna.Components.Get<MapStorageComponent>();
            int safetyCounter = 0;
            IHitInfo fallbackHit;

            while (safetyCounter < Config.Instance.MaxScanPunchThroughs && MyAPIGateway.Physics.CastRay(fallbackStart, end, out fallbackHit, VoxelCollisionLayer))
            {
                if (fallbackHit.HitEntity is IMyVoxelBase)
                {
                    short height = (short)fallbackHit.Position.Y;
                    request.BatchResults.Add(new CellResult { Position = fallbackHit.Position, Height = height, Flags = 2 });
                    if (fallbackStorage != null)
                        fallbackStorage.Grid.AddCell(fallbackHit.Position, new MapCell { Height = height, Flags = 2 }, Config.Instance.CellSize);
                    break;
                }
                fallbackStart = fallbackHit.Position + dir * 0.1;
                safetyCounter++;
                if (Vector3D.DistanceSquared(origin, fallbackStart) >= request.Radius * request.Radius) break;
            }
        }
    }
}
