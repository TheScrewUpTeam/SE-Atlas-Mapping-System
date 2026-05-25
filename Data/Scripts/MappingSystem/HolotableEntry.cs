using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace TSUT.MappingSystem
{
    public class HolotableSettings
    {
        public bool Enabled = false;
        public long HostAntennaEntityId = 0;
        public int Resolution = 64;
        public float Zoom = 100f; // Sample distance scale
        public float VerticalScale = 1.0f;
        public Vector2 Pan = Vector2.Zero;
        public bool RotateMap = true;
        public int UpdateIntervalSeconds = 5;
        public bool AutoCompensate = false;
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Projector), false)]
    public class HolotableEntry : MyGameLogicComponent
    {
        private struct CellData { public int X, Y, EndH; public Color Color; }
        public HolotableSettings Settings = new HolotableSettings();
        private IMyProjector _projector;
        private MapStorageComponent _storage;
        private bool _needsRefresh = false;
        private bool _isLoaded = false;
        private int _ticks = 0;
        private int _scaleApplyTicks = 0;
        private int _pendingEnableTicks = 0;
        private bool _immediateRefresh = false;
        private int _refreshInterval = 6; // Default for 60fps (6 * 10 = 60 ticks)

        private long _lastRenderHash = -1L;
        private DateTime _lastRawUpdateTime = DateTime.MinValue;
        private MapStorageComponent _subscribedStorage = null;

        private float _baseOffsetX, _baseOffsetY, _baseOffsetZ;
        private float _basePitch;
        private Vector3D _lastProjectionPos;
        private float _lastProjectionHeading;

        public static readonly Guid HolotableSettingsGuid = new Guid("B7373F26-3C72-4742-87C2-79013A63A296");

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _projector = Entity as IMyProjector;
            if (_projector == null) return;

            _refreshInterval = (int)Math.Round(1.0 / (10 * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS));
            _projector.AppendingCustomInfo += AppendCustomInfo;
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        private void AppendCustomInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            if (Settings.Enabled)
            {
                sb.AppendLine();
                sb.AppendLine("AMS Holotable Active");
                sb.AppendLine($"Resolution: {Settings.Resolution}x{Settings.Resolution}");
                if (Settings.HostAntennaEntityId != 0)
                {
                    var antenna = MyAPIGateway.Entities.GetEntityById(Settings.HostAntennaEntityId) as IMyTerminalBlock;
                    sb.AppendLine($"Source: {antenna?.CustomName ?? "Unknown Antenna"}");
                }
                else
                {
                    sb.AppendLine("Source: Auto (Local)");
                }
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            ProjectorTerminalControls.Register();

            if (Load())
            {
                _isLoaded = true;
                _needsRefresh = true;

                if (_projector != null)
                {
                    _lastProjectionPos = _projector.WorldMatrix.Translation;
                    _lastProjectionHeading = Settings.RotateMap ? GetCurrentHeading() : 0f;
                    _baseOffsetX = _projector.GetValueFloat("X");
                    _baseOffsetY = _projector.GetValueFloat("Y");
                    _baseOffsetZ = _projector.GetValueFloat("Z");
                    _basePitch   = _projector.GetValueFloat("RotX");
                }

                UpdateVisuals();
            }
        }

        public override void UpdateAfterSimulation10()
        {
            if (_projector == null || _projector.MarkedForClose) return;

            if (_pendingEnableTicks > 0)
            {
                _pendingEnableTicks--;
                if (_pendingEnableTicks == 0)
                    _projector.Enabled = true;
                return; // Skip IsWorking check — IsWorking may not reflect Enabled=true yet
            }

            if (!_projector.IsWorking)
            {
                if (_projector.IsProjecting && MyAPIGateway.Session.IsServer) _projector.Enabled = false;
                return;
            }

            _ticks++;

            if (_scaleApplyTicks > 0)
            {
                _scaleApplyTicks--;
                _projector.SetValueFloat("Scale", 5.12f / Settings.Resolution);
            }

            if (Settings.Enabled)
            {
                bool currentValid = false;
                if (_storage != null && _storage.Entity != null && !_storage.Entity.MarkedForClose)
                {
                    var antennaBlock = _storage.Entity as Sandbox.ModAPI.IMyTerminalBlock;
                    if (antennaBlock != null && antennaBlock.IsWorking)
                    {
                        currentValid = true;
                    }
                }

                if (!currentValid || (_ticks % 50 == 0))
                {
                    var newStorage = FindStorage();
                    if (newStorage != _storage)
                    {
                        SubscribeToStorage(newStorage);
                        _storage = newStorage;
                        _needsRefresh = true;
                    }
                    else if (!currentValid && _storage != null)
                    {
                        SubscribeToStorage(null);
                        _storage = null;
                        _needsRefresh = true;
                    }
                }

                if (!currentValid && _storage == null && _needsRefresh)
                {
                    UpdateProjection();
                    _needsRefresh = false;
                }

                if (!_needsRefresh)
                    ApplyMovementCompensation();

                if (_ticks % _refreshInterval == 0 || _immediateRefresh)
                {
                    if (_storage != null)
                        _storage.UpdateMarkers();

                    if (_needsRefresh)
                    {
                        _immediateRefresh = false;
                        UpdateProjection();
                        _needsRefresh = false;
                        _lastRenderHash = -1L;
                        _lastRawUpdateTime = DateTime.MinValue;
                    }
                    else
                    {
                        int updateTicks = Math.Max(1, Settings.UpdateIntervalSeconds) * _refreshInterval;
                        if (_ticks % updateTicks == 0 && _storage != null && _storage.LastActualUpdate != _lastRawUpdateTime)
                        {
                            _lastRawUpdateTime = _storage.LastActualUpdate;
                            long renderHash = ComputeRenderHash();
                            if (renderHash != _lastRenderHash)
                            {
                                _lastRenderHash = renderHash;
                                UpdateProjection();
                            }
                        }
                    }
                }
            }
        }

        private void ApplyMovementCompensation()
        {
            if (_projector == null || !Settings.AutoCompensate) return;

            Vector3D currentPos = _projector.WorldMatrix.Translation;
            Vector3D worldDelta = currentPos - _lastProjectionPos;
            float currentHeading = Settings.RotateMap ? GetCurrentHeading() : 0f;

            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(currentPos);
            MatrixD projMatrix;
            if (Config.Instance.AlignToGravity && planet != null)
                projMatrix = ProjectionHelper.GetProjectionMatrix(currentPos);
            else
                projMatrix = MatrixD.Identity;

            float worldPerBlock = Settings.Zoom * (64f / Settings.Resolution);
            float dRight   = (float)Vector3D.Dot(worldDelta, projMatrix.Right)   / worldPerBlock;
            float dForward = (float)Vector3D.Dot(worldDelta, projMatrix.Forward) / worldPerBlock;

            // Rotate delta into blueprint space using last rendered heading
            float invCos = (float)Math.Cos(-_lastProjectionHeading);
            float invSin = (float)Math.Sin(-_lastProjectionHeading);
            float mapDX = dRight * invCos - dForward * invSin;
            float mapDZ = dRight * invSin + dForward * invCos;

            // Heading delta → degrees for pitch
            float headingDelta = currentHeading - _lastProjectionHeading;
            while (headingDelta >  Math.PI) headingDelta -= (float)(2 * Math.PI);
            while (headingDelta < -Math.PI) headingDelta += (float)(2 * Math.PI);
            float pitchDelta = headingDelta * (180f / (float)Math.PI);

            _projector.SetValueFloat("X",    _baseOffsetX + mapDX);
            _projector.SetValueFloat("Y",    _baseOffsetY);
            _projector.SetValueFloat("Z",    _baseOffsetZ + mapDZ);
            _projector.SetValueFloat("RotX", _basePitch + pitchDelta);
        }

        public void SetAutoCompensate(bool value)
        {
            if (value == Settings.AutoCompensate) return;
            Settings.AutoCompensate = value;

            if (value)
            {
                if (_projector != null)
                {
                    _baseOffsetX = _projector.GetValueFloat("X");
                    _baseOffsetY = _projector.GetValueFloat("Y");
                    _baseOffsetZ = _projector.GetValueFloat("Z");
                    _basePitch   = _projector.GetValueFloat("RotX");
                    _lastProjectionPos     = _projector.WorldMatrix.Translation;
                    _lastProjectionHeading = Settings.RotateMap ? GetCurrentHeading() : 0f;
                }
            }
            else
            {
                if (_projector != null)
                {
                    _projector.SetValueFloat("X",    _baseOffsetX);
                    _projector.SetValueFloat("Y",    _baseOffsetY);
                    _projector.SetValueFloat("Z",    _baseOffsetZ);
                    _projector.SetValueFloat("RotX", _basePitch);
                }
            }

            Save();
        }

        private float GetCurrentHeading()
        {
            if (_projector == null) return 0f;
            var controller = FindController(_projector.CubeGrid as IMyCubeGrid);
            if (controller != null)
            {
                Vector3D forward = controller.WorldMatrix.Forward;
                return (float)Math.Atan2(forward.X, -forward.Z);
            }
            return 0f;
        }

        private long ComputeRenderHash()
        {
            if (_storage == null || _projector == null) return 0L;
            var grid = _storage.Grid;
            if (grid == null) return 0L;

            int res = Settings.Resolution;
            int cellSize = Config.Instance.CellSize;
            Vector3D antennaPos = _projector.WorldMatrix.Translation;
            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(antennaPos);

            MatrixD projectionMatrix;
            Vector3D viewCenter;
            if (Config.Instance.AlignToGravity && planet != null)
            {
                projectionMatrix = ProjectionHelper.GetProjectionMatrix(antennaPos);
                viewCenter = antennaPos + (projectionMatrix.Right * Settings.Pan.X) + (projectionMatrix.Forward * Settings.Pan.Y);
                projectionMatrix = ProjectionHelper.GetProjectionMatrix(viewCenter);
            }
            else
            {
                projectionMatrix = MatrixD.Identity;
                viewCenter = antennaPos + new Vector3D(Settings.Pan.X, 0, Settings.Pan.Y);
            }

            float heading = Settings.RotateMap ? GetCurrentHeading() : 0f;
            float cos = (float)Math.Cos(heading);
            float sin = (float)Math.Sin(heading);
            float worldPerBlock = Settings.Zoom * (64f / res);

            unchecked
            {
                long hash = 17L;

                // Sample every 4th cell - same stride as the pre-calc loop in GenerateMapBlueprint
                for (int x = -res / 2; x < res / 2; x += 4)
                {
                    for (int y = -res / 2; y < res / 2; y += 4)
                    {
                        float lx = x * worldPerBlock;
                        float ly = y * worldPerBlock;
                        Vector3D samplePos;
                        if (Settings.RotateMap)
                        {
                            float rx = lx * cos - ly * sin;
                            float ry = lx * sin + ly * cos;
                            samplePos = viewCenter + projectionMatrix.Right * rx + projectionMatrix.Forward * ry;
                        }
                        else
                        {
                            samplePos = viewCenter + projectionMatrix.Right * lx + projectionMatrix.Forward * ly;
                        }
                        var cellPos = ProjectionHelper.WorldToGrid(samplePos, cellSize);
                        var cell = grid.GetCell(cellPos);
                        hash = hash * 31 + (cell.HasValue ? ((long)cell.Value.Height * 7 + cell.Value.Flags) : 0L);
                    }
                }

                // Hash only markers visible in the current viewport
                float invCos = (float)Math.Cos(-heading);
                float invSin = (float)Math.Sin(-heading);
                foreach (var marker in _storage.CachedMarkers)
                {
                    Vector3D mOffset = marker.WorldPosition - viewCenter;
                    float mLocalH, mLocalV;
                    if (Config.Instance.AlignToGravity && planet != null)
                    {
                        mLocalH = (float)Vector3D.Dot(mOffset, projectionMatrix.Right);
                        mLocalV = (float)Vector3D.Dot(mOffset, projectionMatrix.Forward);
                    }
                    else
                    {
                        mLocalH = (float)mOffset.X;
                        mLocalV = (float)-mOffset.Z;
                    }
                    int mx, my;
                    if (Settings.RotateMap)
                    {
                        mx = (int)Math.Round((mLocalH * invCos - mLocalV * invSin) / worldPerBlock);
                        my = (int)Math.Round((mLocalH * invSin + mLocalV * invCos) / worldPerBlock);
                    }
                    else
                    {
                        mx = (int)Math.Round(mLocalH / worldPerBlock);
                        my = (int)Math.Round(mLocalV / worldPerBlock);
                    }
                    if (Math.Abs(mx) < res / 2 && Math.Abs(my) < res / 2)
                    {
                        hash = hash * 31 + mx;
                        hash = hash * 31 + my;
                        hash = hash * 31 + (long)marker.Color.PackedValue;
                    }
                }

                return hash;
            }
        }

        private void SubscribeToStorage(MapStorageComponent storage)
        {
            if (_subscribedStorage != null)
            {
                _subscribedStorage.ChunkSyncReceived -= OnChunkSyncReceived;
                _subscribedStorage = null;
            }
            if (storage != null)
            {
                storage.ChunkSyncReceived += OnChunkSyncReceived;
                _subscribedStorage = storage;
            }
        }

        private void OnChunkSyncReceived()
        {
            _needsRefresh = true;
            _immediateRefresh = true;
        }

        public void MarkDirty()
        {
            _needsRefresh = true;
            _immediateRefresh = true;
            Save();
        }

        public void UpdateVisuals()
        {
            if (_projector != null)
            {
                _projector.SetDetailedInfoDirty();
                _projector.RefreshCustomInfo();
                bool original = _projector.ShowInTerminal;
                _projector.ShowInTerminal = !original;
                _projector.ShowInTerminal = original;
            }
        }

        public void UpdateProjection()
        {
            if (!Settings.Enabled || _projector == null || !_projector.Enabled) return;

            if (Settings.AutoCompensate)
            {
                _projector.SetValueFloat("X",    _baseOffsetX);
                _projector.SetValueFloat("Y",    _baseOffsetY);
                _projector.SetValueFloat("Z",    _baseOffsetZ);
                _projector.SetValueFloat("RotX", _basePitch);
            }

            UpdateVisuals();

            try
            {
                MyObjectBuilder_CubeGrid blueprint;
                if (_storage == null)
                {
                    blueprint = GenerateNoDataBlueprint();
                }
                else
                {
                    var grid = _storage.Grid;
                    if (grid == null) return;
                    blueprint = GenerateMapBlueprint(grid);
                }

                _projector.SetProjectedGrid(blueprint);
                if (MyAPIGateway.Session.IsServer)
                {
                    _projector.Enabled = false;
                    _pendingEnableTicks = 2;
                }

                if (Settings.AutoCompensate)
                {
                    _lastProjectionPos     = _projector.WorldMatrix.Translation;
                    _lastProjectionHeading = Settings.RotateMap ? GetCurrentHeading() : 0f;
                }

                _scaleApplyTicks = 5;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Holotable Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private MyObjectBuilder_CubeGrid GenerateNoDataBlueprint()
        {
            var ob = new MyObjectBuilder_CubeGrid()
            {
                GridSizeEnum = MyCubeSize.Small,
                IsStatic = false,
                PersistentFlags = MyPersistentEntityFlags2.None,
                PositionAndOrientation = new MyPositionAndOrientation(Matrix.Identity),
                EntityId = 0,
                DisplayName = "AMS_NoData"
            };

            string text = "NO MAP DATA";
            // Center the text horizontally. Each letter is approx 2 blocks wide including gap.
            int totalWidth = text.Length * 2;
            int xStart = -totalWidth / 2;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ') continue;

                var block = new MyObjectBuilder_CubeBlock()
                {
                    SubtypeName = $"SmallSymbol{c}",
                    Min = new Vector3I(xStart + (i * 2), 0, 0),
                    ColorMaskHSV = ColorToBlockHSV(Color.Red),
                    SkinSubtypeId = "Weldless",
                    // Up = Forward face of letter, Backward = Top of letter relative to grid Up
                    BlockOrientation = new MyBlockOrientation(Base6Directions.Direction.Up, Base6Directions.Direction.Backward)
                };
                ob.CubeBlocks.Add(block);
            }

            // Add a single armor block underneath (at Y=-1) to ensure it's a valid grid and has a center
            ob.CubeBlocks.Add(new MyObjectBuilder_CubeBlock()
            {
                SubtypeName = "SmallBlockArmorBlock",
                Min = new Vector3I(0, -1, 0),
                ColorMaskHSV = ColorToBlockHSV(Color.Black),
                SkinSubtypeId = "Weldless"
            });

            return ob;
        }

        private MapCell? SampleCell(MapGrid grid, Vector2 center, float sampleSize, int cellSize)
        {
            float radius = sampleSize * 0.5f;

            if (sampleSize <= cellSize * 1.1f)
            {
                return grid.GetCell(new Vector2I((int)Math.Floor(center.X / cellSize), (int)Math.Floor(center.Y / cellSize)));
            }

            int minX = (int)Math.Floor((center.X - radius) / cellSize);
            int maxX = (int)Math.Floor((center.X + radius) / cellSize);
            int minY = (int)Math.Floor((center.Y - radius) / cellSize);
            int maxY = (int)Math.Floor((center.Y + radius) / cellSize);

            long totalHeight = 0;
            int count = 0;
            byte flag = 0;

            int stride = Math.Max(1, (maxX - minX) / 8); 

            for (int x = minX; x <= maxX; x += stride)
            {
                for (int y = minY; y <= maxY; y += stride)
                {
                    var cell = grid.GetCell(new Vector2I(x, y));
                    if (cell.HasValue)
                    {
                        totalHeight += cell.Value.Height;
                        count++;
                        flag = Math.Max(flag, cell.Value.Flags);
                    }
                }
            }

            if (count == 0) return null;
            return new MapCell { Height = (short)(totalHeight / count), Flags = flag };
        }

        private MyObjectBuilder_CubeGrid GenerateMapBlueprint(MapGrid grid)
        {
            int res = Settings.Resolution;
            int cellSize = Config.Instance.CellSize;

            Vector3D antennaPos = _projector.WorldMatrix.Translation;
            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(antennaPos);
            
            MatrixD projectionMatrix;
            Vector3D viewCenter;

            if (Config.Instance.AlignToGravity && planet != null)
            {
                projectionMatrix = ProjectionHelper.GetProjectionMatrix(antennaPos);
                viewCenter = antennaPos + (projectionMatrix.Right * Settings.Pan.X) + (projectionMatrix.Forward * Settings.Pan.Y);
                // Re-center projection at viewCenter
                projectionMatrix = ProjectionHelper.GetProjectionMatrix(viewCenter);
            }
            else
            {
                projectionMatrix = MatrixD.Identity;
                viewCenter = antennaPos + new Vector3D(Settings.Pan.X, 0, Settings.Pan.Y);
            }

            float heading = 0f;
            if (Settings.RotateMap)
            {
                heading = GetCurrentHeading();
            }
            
            float cos = (float)Math.Cos(heading);
            float sin = (float)Math.Sin(heading);

            // Zoom = Meters per block at 64 resolution.
            float worldPerBlock = Settings.Zoom * (64f / res);

            // Pre-calculate local average height for visible area
            long localTotalHeight = 0;
            int localCount = 0;
            for (int x = -res / 2; x < res / 2; x += 4) // Sample every 4th for speed
            {
                for (int y = -res / 2; y < res / 2; y += 4)
                {
                    float lx = x * worldPerBlock;
                    float ly = y * worldPerBlock;
                    
                    Vector3D samplePos;
                    if (Settings.RotateMap)
                    {
                        float rx = lx * cos - ly * sin;
                        float ry = lx * sin + ly * cos;
                        samplePos = viewCenter + (projectionMatrix.Right * rx) + (projectionMatrix.Forward * ry);
                    }
                    else
                    {
                        samplePos = viewCenter + (projectionMatrix.Right * lx) + (projectionMatrix.Forward * ly);
                    }
                    
                    var cellPos = ProjectionHelper.WorldToGrid(samplePos, cellSize);
                    var c = grid.GetCell(cellPos);
                    if (c.HasValue)
                    {
                        localTotalHeight += c.Value.Height;
                        localCount++;
                    }
                }
            }

            float avgHeight = localCount > 0 ? (float)localTotalHeight / localCount : grid.AverageHeight;

            var ob = new MyObjectBuilder_CubeGrid()
            {
                GridSizeEnum = MyCubeSize.Small,
                IsStatic = false,
                PersistentFlags = MyPersistentEntityFlags2.None,
                PositionAndOrientation = new MyPositionAndOrientation(Matrix.Identity),
                EntityId = 0,
                DisplayName = "AMS_MapProjection"
            };

            // Pre-calculate Antenna Marker Position
            Vector3D sourcePos = _storage.Entity.WorldMatrix.Translation;
            Vector3D worldOffset = sourcePos - viewCenter;
            
            int antX, antY;
            float localOffsetHorizontal, localOffsetVertical;

            if (Config.Instance.AlignToGravity && planet != null)
            {
                localOffsetHorizontal = (float)Vector3D.Dot(worldOffset, projectionMatrix.Right);
                localOffsetVertical = (float)Vector3D.Dot(worldOffset, projectionMatrix.Forward);
            }
            else
            {
                localOffsetHorizontal = (float)worldOffset.X;
                localOffsetVertical = (float)-worldOffset.Z;
            }

            if (Settings.RotateMap)
            {
                float invCos = (float)Math.Cos(-heading);
                float invSin = (float)Math.Sin(-heading);
                float rx = localOffsetHorizontal * invCos - localOffsetVertical * invSin;
                float ry = localOffsetHorizontal * invSin + localOffsetVertical * invCos;
                antX = (int)Math.Round(rx / worldPerBlock);
                antY = (int)Math.Round(ry / worldPerBlock);
            }
            else
            {
                antX = (int)Math.Round(localOffsetHorizontal / worldPerBlock);
                antY = (int)Math.Round(localOffsetVertical / worldPerBlock);
            }

            float antHeight = (float)(sourcePos.Y - avgHeight);
            if (Config.Instance.AlignToGravity && planet != null)
            {
                antHeight = (float)(Vector3D.Distance(sourcePos, planet.PositionComp.GetPosition()) - (planet.AverageRadius + avgHeight));
            }

            int antH = (int)Math.Round((antHeight / cellSize) * Settings.VerticalScale);
            int antHeightAtSpot = -11;

            // Pass 1: collect visible cells, find minimum height across the viewport
            var visibleCells = new List<CellData>();
            int minEndH = 10;

            for (int x = -res / 2; x < res / 2; x++)
            {
                for (int y = -res / 2; y < res / 2; y++)
                {
                    float lx = x * worldPerBlock;
                    float ly = y * worldPerBlock;

                    Vector3D samplePos;
                    if (Settings.RotateMap)
                    {
                        float rx = lx * cos - ly * sin;
                        float ry = lx * sin + ly * cos;
                        samplePos = viewCenter + (projectionMatrix.Right * rx) + (projectionMatrix.Forward * ry);
                    }
                    else
                    {
                        samplePos = viewCenter + (projectionMatrix.Right * lx) + (projectionMatrix.Forward * ly);
                    }

                    var cellPos = ProjectionHelper.WorldToGrid(samplePos, cellSize);
                    var cell = grid.GetCell(cellPos);
                    if (cell.HasValue)
                    {
                        Color color = GetHeightColor(cell.Value, avgHeight);
                        float height = cell.Value.Height - avgHeight;
                        int endH = MathHelper.Clamp((int)Math.Round((height / cellSize) * Settings.VerticalScale), -10, 10);

                        if (x == antX && y == antY) antHeightAtSpot = endH;
                        if (endH < minEndH) minEndH = endH;

                        visibleCells.Add(new CellData { X = x, Y = y, EndH = endH, Color = color });
                    }
                }
            }

            // Pass 2: build columns from shared floor (minEndH) up to each cell's height
            foreach (var c in visibleCells)
            {
                for (int h = minEndH; h <= c.EndH; h++)
                {
                    ob.CubeBlocks.Add(new MyObjectBuilder_CubeBlock()
                    {
                        SubtypeName = "SmallBlockArmorBlock",
                        Min = new Vector3I(c.X, h, c.Y),
                        ColorMaskHSV = ColorToBlockHSV(c.Color),
                        SkinSubtypeId = "Weldless"
                    });
                }
            }

            int finalAntH = MathHelper.Clamp(antHeightAtSpot, -10, 10);

            // Local Marker: 3x3 White platform for visibility
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    ob.CubeBlocks.Add(new MyObjectBuilder_CubeBlock()
                    {
                        SubtypeName = "SmallBlockArmorBlock",
                        Min = new Vector3I(antX + dx, finalAntH, antY + dy),
                        ColorMaskHSV = ColorToBlockHSV(Color.White),
                        SkinSubtypeId = "Weldless"
                    });
                }
            }

            // Red antenna on top of the middle of the 3x3 white platform
            var redAntenna = new MyObjectBuilder_RadioAntenna()
            {
                SubtypeName = "SmallBlockRadioAntenna",
                Min = new Vector3I(antX, finalAntH + 1, antY),
                ColorMaskHSV = ColorToBlockHSV(Color.Red),
                SkinSubtypeId = "Weldless",
                BlockOrientation = new MyBlockOrientation(
                    Base6Directions.Direction.Up, // Point the rod Up
                    Base6Directions.GetDirection(Vector3.Transform(Vector3.Backward, Quaternion.CreateFromAxisAngle(Vector3.Up, heading))) // Base yaw
                ),
                EntityId = 0,
                Enabled = true
            };
            ob.CubeBlocks.Add(redAntenna);

            // Remote Markers (Grids/GPS) from shared storage
            if (_storage != null)
            {
                foreach (var marker in _storage.CachedMarkers)
                {
                    Vector3D mWorldOffset = marker.WorldPosition - viewCenter;
                    int mx, my;
                    
                    float mLocalOffsetHorizontal, mLocalOffsetVertical;
                    if (Config.Instance.AlignToGravity && planet != null)
                    {
                        mLocalOffsetHorizontal = (float)Vector3D.Dot(mWorldOffset, projectionMatrix.Right);
                        mLocalOffsetVertical = (float)Vector3D.Dot(mWorldOffset, projectionMatrix.Forward);
                    }
                    else
                    {
                        mLocalOffsetHorizontal = (float)mWorldOffset.X;
                        mLocalOffsetVertical = (float)-mWorldOffset.Z;
                    }

                    if (Settings.RotateMap)
                    {
                        float invCos = (float)Math.Cos(-heading);
                        float invSin = (float)Math.Sin(-heading);
                        float rx = mLocalOffsetHorizontal * invCos - mLocalOffsetVertical * invSin;
                        float ry = mLocalOffsetHorizontal * invSin + mLocalOffsetVertical * invCos;
                        mx = (int)Math.Round(rx / worldPerBlock);
                        my = (int)Math.Round(ry / worldPerBlock);
                    }
                    else
                    {
                        mx = (int)Math.Round(mLocalOffsetHorizontal / worldPerBlock);
                        my = (int)Math.Round(mLocalOffsetVertical / worldPerBlock);
                    }

                    // Check if marker is within resolution bounds
                    if (Math.Abs(mx) < res / 2 && Math.Abs(my) < res / 2)
                    {
                        float mHeight = (float)(marker.WorldPosition.Y - avgHeight);
                        int mh = MathHelper.Clamp((int)Math.Round((mHeight / cellSize) * Settings.VerticalScale), -10, 10);

                        var ball = new MyObjectBuilder_SpaceBall()
                        {
                            SubtypeName = "SpaceBallSmall",
                            Min = new Vector3I(mx, mh, my),
                            ColorMaskHSV = ColorToBlockHSV(marker.Color),
                            SkinSubtypeId = "Weldless",
                            EntityId = 0,
                            Enabled = true
                        };
                        ob.CubeBlocks.Add(ball);
                    }
                }
            }

            // Fallback: Ensure at least one block exists to avoid game errors
            if (ob.CubeBlocks.Count == 0)
            {
                ob.CubeBlocks.Add(new MyObjectBuilder_CubeBlock()
                {
                    SubtypeName = "SmallBlockArmorBlock",
                    Min = Vector3I.Zero,
                    ColorMaskHSV = ColorToBlockHSV(Color.DarkRed),
                    SkinSubtypeId = "Weldless"
                });
            }

            return ob;
        }

        private Vector3 ColorToBlockHSV(Color color)
        {
            Vector3 hsv = color.ColorToHSV();
            return new Vector3(hsv.X, hsv.Y * 2f - 1f, hsv.Z * 2f - 1f);
        }

        private Color GetHeightColor(MapCell cell, float avgHeight)
        {
            if (cell.Flags == 2) return Color.Gray;
            float h = cell.Height - avgHeight;
            if (h < -50) return Color.Lerp(Color.Navy, Color.Blue, MathHelper.Clamp((h + 500) / 450f, 0, 1));
            if (h < 0) return Color.Lerp(Color.Blue, Color.Cyan, (h + 50) / 50f);
            if (h < 20) return Color.Lerp(Color.SandyBrown, Color.DarkGreen, h / 20f);
            if (h < 200) return Color.Lerp(Color.DarkGreen, Color.Green, (h - 20) / 180f);
            if (h < 600) return Color.Lerp(Color.Green, Color.Olive, (h - 200) / 400f);
            if (h < 1200) return Color.Lerp(Color.Olive, Color.SaddleBrown, (h - 600) / 600f);
            if (h < 2000) return Color.Lerp(Color.SaddleBrown, Color.DimGray, (h - 1200) / 800f);
            return Color.Lerp(Color.DimGray, Color.White, MathHelper.Clamp((h - 2000) / 1000f, 0, 1));
        }

        private bool IsConnected(IMyTerminalBlock block)
        {
            if (_projector == null || block == null) return false;
            if (_projector.CubeGrid == block.CubeGrid) return true;

            var grid1 = _projector.CubeGrid as IMyCubeGrid;
            var grid2 = block.CubeGrid as IMyCubeGrid;

            if (grid1 == null || grid2 == null) return false;

            return MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Logical, grid1) == 
                   MyAPIGateway.GridGroups.GetGridGroup(GridLinkTypeEnum.Logical, grid2);
        }

        private MapStorageComponent FindStorage()
        {
            if (Settings.HostAntennaEntityId != 0)
            {
                var antenna = MyAPIGateway.Entities.GetEntityById(Settings.HostAntennaEntityId) as IMyTerminalBlock;
                if (antenna != null && antenna.IsWorking && IsConnected(antenna)) return antenna.Components.Get<MapStorageComponent>();
            }

            var antennas = new List<IMyRadioAntenna>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(_projector.CubeGrid).GetBlocksOfType(antennas);
            foreach (var a in antennas)
            {
                if (!a.IsWorking) continue;
                var storage = a.Components.Get<MapStorageComponent>();
                if (storage != null) return storage;
            }
            return null;
        }

        private IMyShipController FindController(IMyCubeGrid grid)
        {
            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            if (terminalSystem == null) return null;
            var controllers = new List<IMyShipController>();
            terminalSystem.GetBlocksOfType(controllers);
            if (controllers.Count == 0) return null;
            foreach (var c in controllers) if (c.IsMainCockpit) return c;
            foreach (var c in controllers) if (c.IsUnderControl) return c;
            return controllers[0];
        }

        public string BuildSaveString()
        {
            var sb = new StringBuilder();
            sb.Append(Settings.Enabled ? "1" : "0").Append(";")
              .Append(Settings.HostAntennaEntityId).Append(";")
              .Append(Settings.Resolution).Append(";")
              .Append(Settings.Zoom).Append(";")
              .Append(Settings.VerticalScale).Append(";")
              .Append(Settings.Pan.X).Append(";")
              .Append(Settings.Pan.Y).Append(";")
              .Append(Settings.RotateMap ? "1" : "0").Append(";")
              .Append(Settings.UpdateIntervalSeconds).Append(";")
              .Append(Settings.AutoCompensate ? "1" : "0");
            return sb.ToString();
        }

        public bool LoadFromString(string raw)
        {
            try
            {
                if (string.IsNullOrEmpty(raw)) return false;
                string[] parts = raw.Split(';');
                if (parts.Length < 8) return false;
                Settings.Enabled = parts[0] == "1";
                long.TryParse(parts[1], out Settings.HostAntennaEntityId);
                int.TryParse(parts[2], out Settings.Resolution);
                float.TryParse(parts[3], out Settings.Zoom);
                float.TryParse(parts[4], out Settings.VerticalScale);
                float.TryParse(parts[5], out Settings.Pan.X);
                float.TryParse(parts[6], out Settings.Pan.Y);
                Settings.RotateMap = parts[7] == "1";
                if (parts.Length >= 9) int.TryParse(parts[8], out Settings.UpdateIntervalSeconds);
                if (parts.Length >= 10) Settings.AutoCompensate = parts[9] == "1";
                return true;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Holotable Load Err: {ex.Message}");
                return false;
            }
        }

        public void SyncToNetwork()
        {
            if (_projector == null || !MyAPIGateway.Multiplayer.MultiplayerActive || MapSession.Instance == null) return;
            var packet = new PacketHolotableSync(_projector.EntityId, BuildSaveString());
            if (MyAPIGateway.Session.IsServer)
                MapSession.Instance.Networking.SendToOthers(packet, 0); // No local delivery — settings already applied, avoids redundant MarkDirty
            else
                MapSession.Instance.Networking.SendToServer(packet);
        }

        public void Save()
        {
            try
            {
                if (_projector == null) return;
                if (_projector.Storage == null) _projector.Storage = new MyModStorageComponent();
                _projector.Storage[HolotableSettingsGuid] = BuildSaveString();
            }
            catch (Exception ex) { MyLog.Default.WriteLine($"{Config.LogPrefix} Holotable Save Err: {ex.Message}"); }
        }

        public bool Load()
        {
            try
            {
                if (_projector == null || _projector.Storage == null || !_projector.Storage.ContainsKey(HolotableSettingsGuid)) return false;
                return LoadFromString(_projector.Storage[HolotableSettingsGuid]);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Holotable Load Err: {ex.Message}");
                return false;
            }
        }

        public override void Close()
        {
            SubscribeToStorage(null);
            if (_projector != null)
            {
                _projector.AppendingCustomInfo -= AppendCustomInfo;
            }
            Save();
            _projector = null;
        }
    }
}
