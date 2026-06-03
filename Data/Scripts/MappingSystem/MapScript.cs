using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;
using VRage.Utils;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.ModAPI;
using VRage.Game.Entity;
using VRage.Game;
using Sandbox.Game.Entities;

namespace TSUT.MappingSystem
{
    [MyTextSurfaceScript("AMS_MapApp", "Map App")]
    public class MapScript : MyTextSurfaceScriptBase
    {
        private MapRenderer _renderer;
        private MapStorageComponent _storage;
        private List<MapRenderer.MapMarker> _cachedMarkers = new List<MapRenderer.MapMarker>();

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public MapScript(Sandbox.ModAPI.Ingame.IMyTextSurface surface, VRage.Game.ModAPI.Ingame.IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public override void Run()
        {
            try
            {
                base.Run();

                if (!m_block.IsWorking)
                {
                    if (_renderer != null) _renderer.DrawNoData();
                    return;
                }

                var displayEntry = (m_block as VRage.ModAPI.IMyEntity)?.GameLogic?.GetAs<DisplayFlatEntry>();
                int surfaceIndex = -1;
                var provider = m_block as Sandbox.ModAPI.IMyTextSurfaceProvider;
                if (provider != null)
                {
                    for (int i = 0; i < provider.SurfaceCount; i++)
                    {
                        if (provider.GetSurface(i) == m_surface)
                        {
                            surfaceIndex = i;
                            break;
                        }
                    }
                }

                bool currentValid = false;
                if (_storage != null && _storage.Entity != null && !_storage.Entity.MarkedForClose)
                {
                    var antennaBlock = _storage.Entity as Sandbox.ModAPI.IMyTerminalBlock;
                    if (antennaBlock != null && antennaBlock.IsWorking && IsConnected(antennaBlock))
                    {
                        currentValid = true;
                    }
                }

                if (displayEntry != null && surfaceIndex != -1)
                {
                    var settings = displayEntry.GetSettings(surfaceIndex);
                    if (settings.HostAntennaEntityId != 0)
                    {
                        if (_storage == null || _storage.Entity.EntityId != settings.HostAntennaEntityId)
                        {
                            var hostEntity = MyAPIGateway.Entities.GetEntityById(settings.HostAntennaEntityId) as Sandbox.ModAPI.IMyTerminalBlock;
                            if (hostEntity != null && !hostEntity.MarkedForClose && hostEntity.IsWorking && IsConnected(hostEntity))
                            {
                                _storage = hostEntity.Components.Get<MapStorageComponent>();
                                currentValid = true;
                            }
                            else
                            {
                                currentValid = false;
                            }
                        }
                    }
                }

                if (!currentValid)
                {
                    _storage = FindStorage();
                }

                if (_storage != null)
                {
                    var antenna = _storage.Entity as Sandbox.ModAPI.IMyTerminalBlock;
                    var scannerEntry = (antenna != null) ? antenna.GameLogic?.GetAs<ScannerEntry>() : null;
                    var grid = scannerEntry?.Grid ?? _storage.Grid;
                    
                    if (_renderer == null)
                    {
                        _renderer = new MapRenderer(m_surface, grid, _storage.Entity.EntityId);
                    }
                    else
                    {
                        _storage.UpdateMarkers();
                        _renderer.UpdateMarkers(_storage.CachedMarkers);
                        _renderer.UpdateGrid(grid);
                    }

                    Vector2 relativePan;
                    float zoom;
                    bool rotateMap = true;

                    if (displayEntry != null && surfaceIndex != -1)
                    {
                        var settings = displayEntry.GetSettings(surfaceIndex);
                        relativePan = settings.Pan;
                        zoom = settings.Zoom;
                        rotateMap = settings.RotateMap;
                    }
                    else
                    {
                        relativePan = Vector2.Zero;
                        zoom = 20f;
                    }

                    _renderer.SetView(relativePan, zoom);
                    
                    var cubeGrid = m_block.CubeGrid as VRage.Game.ModAPI.IMyCubeGrid;
                    bool isStatic = cubeGrid.IsStatic;
                    float heading = 0f;

                    if (!isStatic)
                    {
                        if (cubeGrid != null)
                        {
                            var controller = FindController(cubeGrid);
                            if (controller != null)
                            {
                                Vector3D forward = controller.WorldMatrix.Forward;
                                
                                if (Config.Instance.AlignToGravity)
                                {
                                    MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(m_block.WorldMatrix.Translation);
                                    if (planet != null)
                                    {
                                        MatrixD projectionMatrix = ProjectionHelper.GetProjectionMatrix(m_block.WorldMatrix.Translation);
                                        // Project vehicle forward onto tangent plane
                                        Vector3D tangentForward = Vector3D.Normalize(Vector3D.Reject(forward, projectionMatrix.Up));
                                        // Angle relative to tangent plane's Forward (Geographic North)
                                        heading = (float)Math.Atan2(Vector3D.Dot(tangentForward, projectionMatrix.Right), Vector3D.Dot(tangentForward, projectionMatrix.Forward));
                                    }
                                    else
                                    {
                                        heading = (float)Math.Atan2(forward.X, -forward.Z);
                                    }
                                }
                                else
                                {
                                    // Stable North is -Z, East is +X
                                    heading = (float)Math.Atan2(forward.X, -forward.Z);
                                }
                            }
                        }
                    }

                    bool isScanning = scannerEntry?.IsLocallyScanning ?? false;
                    string eta = scannerEntry?.GetScanETA() ?? "N/A";
                    _renderer.Draw(m_block.WorldMatrix, isStatic, heading, rotateMap, isScanning, eta);
                }
                else
                {
                    if (_renderer == null)
                    {
                        _renderer = new MapRenderer(m_surface, null, 0);
                    }
                    _renderer.DrawNoData();
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Script Exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private bool IsConnected(Sandbox.ModAPI.IMyTerminalBlock block)
        {
            if (m_block == null || block == null) return false;
            if (m_block.CubeGrid == block.CubeGrid) return true;

            var grid1 = m_block.CubeGrid as VRage.Game.ModAPI.IMyCubeGrid;
            var grid2 = block.CubeGrid as VRage.Game.ModAPI.IMyCubeGrid;

            if (grid1 == null || grid2 == null) return false;

            // Use Logical link to include all subgrids and connected grids (rotors, pistons, connectors)
            return MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Logical, grid1) == 
                   MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Logical, grid2);
        }

        private void CollectAntennaMarkers(List<MapRenderer.MapMarker> markers)
        {
            var myAntenna = _storage.Entity as Sandbox.ModAPI.IMyRadioAntenna;
            if (myAntenna == null) return;
            var myAntennaPos = myAntenna.GetPosition();
            long myId = MyAPIGateway.Session.Player.IdentityId;

            var result = new List<MyEntity>();

            HashSet<VRage.ModAPI.IMyEntity> entities = new HashSet<VRage.ModAPI.IMyEntity>();

            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCubeGrid);

            foreach (var entity in entities)
            {
                var grid = entity as IMyCubeGrid;

                if (grid.EntityId == myAntenna.CubeGrid.EntityId)
                    continue;

                var antennas = new List<IMyRadioAntenna>();
                MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(antennas);

                foreach(var antenna in antennas)
                {
                    if (!antenna.IsWorking || !antenna.Enabled)
                        continue;

                    if (antenna.Radius <= 0)
                        continue;

                    if (antenna.EntityId == myAntenna.EntityId)
                        continue;

                    var targetPos = antenna.GetPosition();

                    double dist = Vector3D.Distance((Vector3D)myAntennaPos, (Vector3D)targetPos);
                    if (dist > myAntenna.Radius)
                        continue;

                    long ownerId = antenna.OwnerId;
                    var relation = MyAPIGateway.Session.Player.GetRelationTo(ownerId);
                    
                    markers.Add(new MapRenderer.MapMarker { 
                        WorldPosition = antenna.WorldMatrix.Translation, 
                        Label = antenna.CubeGrid.DisplayName, 
                        Color = GetMarkerColor(relation, ownerId, myId)
                    });
                }
            }
        }

        Color GetMarkerColor(MyRelationsBetweenPlayerAndBlock relation, long ownerId, long myId)
        {
            if (ownerId == myId)
                return Color.SkyBlue; // your own

            switch (relation)
            {
                case MyRelationsBetweenPlayerAndBlock.Owner:
                    return Color.LightBlue;

                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    return Color.Green;

                case MyRelationsBetweenPlayerAndBlock.Neutral:
                    return Color.White;

                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return Color.Red;

                default:
                    return Color.White;
                }
        }

        private void CollectGPSMarkers(List<MapRenderer.MapMarker> markers)
        {
            var player = MyAPIGateway.Session.Player;
            if (player == null) return;

            var gpsList = MyAPIGateway.Session.GPS.GetGpsList(player.IdentityId);
            foreach (var gps in gpsList)
            {
                markers.Add(new MapRenderer.MapMarker { WorldPosition = gps.Coords, Label = gps.Name, Color = gps.GPSColor });
            }
        }

        private Sandbox.ModAPI.IMyShipController FindController(VRage.Game.ModAPI.IMyCubeGrid grid)
        {
            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            if (terminalSystem == null) return null;

            var controllers = new List<Sandbox.ModAPI.IMyShipController>();
            terminalSystem.GetBlocksOfType(controllers);

            if (controllers.Count == 0) return null;

            foreach (var c in controllers)
            {
                if (c.IsMainCockpit) return c;
            }

            foreach (var c in controllers)
            {
                if (c.IsUnderControl) return c;
            }

            return controllers[0];
        }

        private MapStorageComponent FindStorage()
        {
            var modBlock = m_block;
            if (modBlock == null) return null;

            var storage = modBlock.Components.Get<MapStorageComponent>();
            if (storage != null) return storage;

            var grid = modBlock.CubeGrid as VRage.Game.ModAPI.IMyCubeGrid;
            if (grid == null) return null;

            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            if (terminalSystem == null) return null;

            var antennas = new List<Sandbox.ModAPI.IMyRadioAntenna>();
            terminalSystem.GetBlocksOfType(antennas);

            foreach (var antenna in antennas)
            {
                if (!antenna.IsWorking) continue;
                storage = antenna.Components.Get<MapStorageComponent>();
                if (storage != null) return storage;
            }

            return null;
        }

        public override void Dispose()
        {
            base.Dispose();
            _renderer = null;
            _storage = null;
        }
    }
}
