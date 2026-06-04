using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using Sandbox.Game.EntityComponents;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Game;
using VRage.Game.ModAPI;

namespace TSUT.MappingSystem
{
    public class MapStorageComponent : MyEntityComponentBase
    {
        public MapGrid Grid { get; private set; }
        private bool _isDirty = false;
        private readonly object _gridLock = new object();

        public List<MapRenderer.MapMarker> CachedMarkers = new List<MapRenderer.MapMarker>();
        private DateTime _lastMarkerUpdateAttempt = DateTime.MinValue;
        public DateTime LastActualUpdate = DateTime.MinValue;
        private long _lastMarkerHash = 0;

        public event Action DataChanged;
        public event Action MarkersUpdated;
        public event Action ChunkSyncReceived;

        public MapStorageComponent()
        {
            Grid = new MapGrid();
            Grid.DataChanged += OnGridDataChanged;
        }

        private void OnGridDataChanged()
        {
            LastActualUpdate = DateTime.Now;
            DataChanged?.Invoke();
        }

        public void UpdateMarkers()
        {
            try
            {
                if ((DateTime.Now - _lastMarkerUpdateAttempt).TotalSeconds < Config.Instance.MarkerUpdateIntervalSeconds)
                    return;

                _lastMarkerUpdateAttempt = DateTime.Now;

                var newMarkers = new List<MapRenderer.MapMarker>();
                CollectAntennaMarkers(newMarkers);
                CollectGPSMarkers(newMarkers);
                newMarkers.Sort((a, b) => {
                    int c = a.WorldPosition.X.CompareTo(b.WorldPosition.X);
                    if (c != 0) return c;
                    c = a.WorldPosition.Y.CompareTo(b.WorldPosition.Y);
                    if (c != 0) return c;
                    return a.WorldPosition.Z.CompareTo(b.WorldPosition.Z);
                });

                long newHash = CalculateMarkerHash(newMarkers);

                if (newHash != _lastMarkerHash)
                {
                    CachedMarkers = newMarkers;
                    _lastMarkerHash = newHash;
                    LastActualUpdate = DateTime.Now;
                    MarkersUpdated?.Invoke();
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error updating markers in storage: {ex.Message}");
            }
        }

        private long CalculateMarkerHash(List<MapRenderer.MapMarker> markers)
        {
            unchecked
            {
                long hash = 17;
                foreach (var m in markers)
                {
                    // Include position (rounded), color, and label
                    hash = hash * 31 + (long)Math.Round(m.WorldPosition.X * 10);
                    hash = hash * 31 + (long)Math.Round(m.WorldPosition.Y * 10);
                    hash = hash * 31 + (long)Math.Round(m.WorldPosition.Z * 10);
                    hash = hash * 31 + m.Color.PackedValue;
                    hash = hash * 31 + (m.Label?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }

        private void CollectAntennaMarkers(List<MapRenderer.MapMarker> markers)
        {
            var myAntenna = Entity as Sandbox.ModAPI.IMyRadioAntenna;
            if (myAntenna == null) return;
            var myAntennaPos = myAntenna.GetPosition();
            long myId = MyAPIGateway.Session.Player.IdentityId;

            HashSet<VRage.ModAPI.IMyEntity> entities = new HashSet<VRage.ModAPI.IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCubeGrid);

            foreach (var entity in entities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null || grid.EntityId == myAntenna.CubeGrid.EntityId) continue;

                var antennas = new List<IMyRadioAntenna>();
                MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(antennas);

                foreach (var antenna in antennas)
                {
                    if (!antenna.IsWorking || !antenna.Enabled || antenna.Radius <= 0 || antenna.EntityId == myAntenna.EntityId) continue;

                    var targetPos = antenna.GetPosition();
                    if (Vector3D.Distance((Vector3D)myAntennaPos, (Vector3D)targetPos) > myAntenna.Radius) continue;

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

        private Color GetMarkerColor(MyRelationsBetweenPlayerAndBlock relation, long ownerId, long myId)
        {
            if (ownerId == myId) return Color.SkyBlue;
            switch (relation)
            {
                case MyRelationsBetweenPlayerAndBlock.Owner: return Color.LightBlue;
                case MyRelationsBetweenPlayerAndBlock.FactionShare: return Color.Green;
                case MyRelationsBetweenPlayerAndBlock.Neutral: return Color.White;
                case MyRelationsBetweenPlayerAndBlock.Enemies: return Color.Red;
                default: return Color.White;
            }
        }

        public void MarkDirty()
        {
            _isDirty = true;
            LastActualUpdate = DateTime.Now;
            DataChanged?.Invoke();
        }

        public void ReceiveChunkSync(List<ChunkEntry> chunks)
        {
            Grid.SetSerializedChunks(chunks);
            MarkDirty();
            ChunkSyncReceived?.Invoke();
        }

        public void MergeData(MapGrid other)
        {
            if (other == null) return;
            Grid.Merge(other);
            MarkDirty();
        }

        public override void OnAddedToScene()
        {
            Load();
        }

        public override bool IsSerialized()
        {
            return true;
        }

        public override MyObjectBuilder_ComponentBase Serialize(bool copy = false)
        {
            if (_isDirty)
            {
                Save();
                _isDirty = false;
            }
            return base.Serialize(copy);
        }

        public void Save()
        {
            try
            {
                if (Entity == null)
                {
                    return;
                }

                int capacity = GetCapacity();
                
                if (Entity.Storage == null)
                    Entity.Storage = new MyModStorageComponent();

                byte[] data;
                lock (_gridLock)
                {
                    Grid.PruneStorage(capacity);
                    Grid.BeforeSerialize();
                    data = MyAPIGateway.Utilities.SerializeToBinary(Grid);
                }

                string base64 = Convert.ToBase64String(data);
                Entity.Storage[Config.StorageGuid] = base64;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error saving storage for entity {Entity?.EntityId}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public int GetCapacity()
        {
            var antenna = Entity as IMyTerminalBlock;
            if (antenna == null || antenna.CubeGrid == null) return Config.Instance.MaxChunksSmallGrid;

            if (antenna.CubeGrid.IsStatic)
                return Config.Instance.MaxChunksStaticGrid;

            if (antenna.CubeGrid.GridSizeEnum == VRage.Game.MyCubeSize.Large)
                return Config.Instance.MaxChunksLargeGrid;

            return Config.Instance.MaxChunksSmallGrid;
        }

        public void Load()
        {
            if (Entity == null)
            {
                return;
            }

            if (Entity.Storage == null)
            {
                return;
            }

            if (!Entity.Storage.ContainsKey(Config.StorageGuid))
            {
                return;
            }

            try
            {
                string base64 = Entity.Storage[Config.StorageGuid];
                if (string.IsNullOrEmpty(base64))
                {
                    return;
                }

                byte[] data = Convert.FromBase64String(base64);
                var grid = MyAPIGateway.Utilities.SerializeFromBinary<MapGrid>(data);
                
                if (grid != null)
                {
                    lock (_gridLock)
                    {
                        grid.AfterDeserialize();
                        
                        Grid = grid;
                        Grid.DataChanged += OnGridDataChanged;
                    }
                    DataChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error loading storage for entity {Entity?.EntityId}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override string ComponentTypeDebugString => "AMS_Storage";
    }
}
