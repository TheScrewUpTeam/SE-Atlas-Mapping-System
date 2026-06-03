using System;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace TSUT.MappingSystem
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_RadioAntenna), false)]
    public class ScannerEntry : MyGameLogicComponent
    {
        private IMyRadioAntenna _antenna;
        private MapStorageComponent _storage;
        private MyResourceSinkComponent _sink;

        public bool IsScanning { get; set; }
        public bool IsLocallyScanning => MyAPIGateway.Session.IsServer ? IsScanning : _clientIsScanning;
        public long SelectedExchangeEntityId = 0;
        public MapGrid Grid => _storage?.Grid;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _antenna = Entity as IMyRadioAntenna;
            if (_antenna != null)
            {
                _antenna.AppendingCustomInfo += AppendCustomInfo;
                
                var storage = GetStorage();
                if (storage != null)
                {
                    storage.DataChanged += OnDataChanged;
                }

                _sink = Entity.Components.Get<MyResourceSinkComponent>();
                if (_sink != null)
                {
                    _sink.SetRequiredInputFuncByType(MyResourceDistributorComponent.ElectricityId, CalculateRequiredInput);
                }
            }
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        private float CalculateRequiredInput()
        {
            float baseInput = _sink.MaxRequiredInputByType(MyResourceDistributorComponent.ElectricityId);
            return IsScanning ? baseInput * AntennaHelper.GetScanPowerMultiplier(_antenna) : baseInput;
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            _storage?.Save();
            return base.GetObjectBuilder(copy);
        }

        public void UpdateSink()
        {
            _sink?.Update();
            MyLog.Default.WriteLine($"{Config.LogPrefix} Direct Sink update.");
        }

        private void AntennaPropertiesChanged(IMyTerminalBlock block)
        {
            _antenna?.SetDetailedInfoDirty();
            _antenna?.RefreshCustomInfo();
            _sink?.Update();
        }

        private void OnDataChanged()
        {
            _antenna?.SetDetailedInfoDirty();
            _antenna?.RefreshCustomInfo();
        }

        private bool _clientIsScanning;
        private int _clientCurrentRay;
        private int _clientTotalRays;

        public void UpdateState(bool isScanning, int currentRay, int totalRays)
        {
            bool stateChanged = _clientIsScanning != isScanning;

            _clientIsScanning = isScanning;
            _clientCurrentRay = currentRay;
            _clientTotalRays = totalRays;
            _antenna?.SetDetailedInfoDirty();
            _antenna?.RefreshCustomInfo();

            if (stateChanged)
            {
                UpdateVisuals();
            }
        }

        public override void UpdateBeforeSimulation10()
        {
            if (MyAPIGateway.Session.IsServer) return;

            bool changed = false;
            int raysPerUpdate = Config.Instance.MaxRaycastsPerTick * 10;

            if (_clientIsScanning)
            {
                if (_clientCurrentRay < _clientTotalRays)
                {
                    _clientCurrentRay = Math.Min(_clientTotalRays, _clientCurrentRay + raysPerUpdate);
                    changed = true;
                }
            }

            if (changed)
            {
                _antenna?.SetDetailedInfoDirty();
                _antenna?.RefreshCustomInfo();
            }
        }

        public float GetScanProgress()
        {
            if (MyAPIGateway.Session.IsServer)
            {
                if (!IsScanning) return 0f;
                var scan = MapSession.Instance.Scheduler?.GetActiveScan(_antenna.EntityId);
                if (scan == null) return 0f;
                return (float)scan.CurrentRayIndex / scan.TotalRays;
            }
            else
            {
                if (!_clientIsScanning || _clientTotalRays == 0) return 0f;
                return (float)_clientCurrentRay / _clientTotalRays;
            }
        }

        public string GetScanETA()
        {
            int remainingRays;
            if (MyAPIGateway.Session.IsServer)
            {
                var scheduler = MapSession.Instance.Scheduler;
                if (scheduler == null) return "N/A";

                var activeScan = scheduler.GetActiveScan(_antenna.EntityId);
                if (activeScan == null) return "N/A";
                remainingRays = activeScan.TotalRays - activeScan.CurrentRayIndex;
            }
            else
            {
                if (!_clientIsScanning) return "N/A";
                remainingRays = _clientTotalRays - _clientCurrentRay;
            }

            int raysPerTick = Config.Instance.MaxRaycastsPerTick;
            int remainingTicks = (remainingRays + raysPerTick - 1) / raysPerTick;

            int totalSeconds = (int)(remainingTicks / 60f);
            TimeSpan timeSpan = TimeSpan.FromSeconds(totalSeconds);
            return timeSpan.ToString(@"mm\:ss");
        }

        private void AppendCustomInfo(IMyTerminalBlock block, StringBuilder info)
        {
            float scanRadius = AntennaHelper.GetScanRadius(_antenna);
            var storage = GetStorage();
            int chunks = storage?.Grid?.ChunksSize ?? 0;
            int capacity = storage?.GetCapacity() ?? 0;
            int cells = storage?.Grid?.CellCount ?? 0;

            bool scanning = MyAPIGateway.Session.IsServer ? IsScanning : _clientIsScanning;
            string status = scanning ? "Scanning" : "Idle";

            info.AppendLine($"Status: {status}");
            info.AppendLine($"ETA: {(scanning ? GetScanETA() : "N/A")}");
            info.AppendLine($"\nMap Scan Radius: {Math.Round(scanRadius)} m");
            info.AppendLine($"Map Data Stored:");
            info.AppendLine($"  Chunks: {chunks} / {capacity}");
            info.AppendLine($"  Cells: {cells}");
        }

        public override void UpdateOnceBeforeFrame()
        {
            AntennaTerminalControls.Register();

            if (_antenna != null)
            {
                var prop = _antenna.GetProperty("Radius");
                if (prop != null)
                {
                    _antenna.PropertiesChanged += AntennaPropertiesChanged;
                }
            }
        }

        private MapStorageComponent GetStorage()
        {
            if (_storage != null) return _storage;
            
            _storage = Entity.Components.Get<MapStorageComponent>();
            if (_storage == null)
            {
                _storage = new MapStorageComponent();
                Entity.Components.Add(_storage);
                _storage.Load();
                
                _storage.DataChanged += OnDataChanged;
            }
            return _storage;
        }

        public void RequestScan()
        {
            if (MapSession.Instance?.Networking == null) return;

            if (!_antenna.IsWorking)
            {
                MyAPIGateway.Utilities.ShowNotification("Antenna must be ON and functional.", 2000, MyFontEnum.Red);
                return;
            }

            bool scanning = MyAPIGateway.Session.IsServer ? IsScanning : _clientIsScanning;

            if (scanning)
            {
                MyAPIGateway.Utilities.ShowNotification("Scan already in progress.", 2000, MyFontEnum.Red);
                return;
            }

            if (MyAPIGateway.Session.IsServer)
            {
                float radius = AntennaHelper.GetScanRadius(_antenna);
                if (MapSession.Instance.Scheduler != null)
                {
                    MapSession.Instance.Scheduler.EnqueueScan(Entity, radius, MyAPIGateway.Multiplayer.MyId);
                    MyAPIGateway.Utilities.ShowNotification($"Scan started {Math.Round(radius)}m...", 2000, MyFontEnum.Green);
                }
            }
            else
            {
                MapSession.Instance.Networking.SendToServer(new PacketScanRequest(Entity.EntityId));
                MyAPIGateway.Utilities.ShowNotification("Scan requested...", 2000, MyFontEnum.Green);
            }
        }

        public void RequestStopScan()
        {
            if (MapSession.Instance?.Networking == null) return;

            bool scanning = MyAPIGateway.Session.IsServer ? IsScanning : _clientIsScanning;

            if (!scanning)
            {
                MyAPIGateway.Utilities.ShowNotification("No scan in progress.", 2000, MyFontEnum.Red);
                return;
            }

            if (MyAPIGateway.Session.IsServer)
            {
                MapSession.Instance.Scheduler?.CancelScanForAntenna(Entity.EntityId);
                MyAPIGateway.Utilities.ShowNotification("Scan stopped.", 2000, MyFontEnum.Green);
            }
            else
            {
                MapSession.Instance.Networking.SendToServer(new PacketScanStopRequest(Entity.EntityId));
                MyAPIGateway.Utilities.ShowNotification("Stop requested...", 2000, MyFontEnum.Green);
            }
        }

        public void RequestExchange()
        {
            if (SelectedExchangeEntityId == 0) return;
            if (MapSession.Instance?.Networking == null) return;

            if (MyAPIGateway.Session.IsServer)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} [Exchange] Server-local: source={SelectedExchangeEntityId} target={Entity.EntityId}");
                var sourceEntity = MyAPIGateway.Entities.GetEntityById(SelectedExchangeEntityId);
                var sourceStorage = sourceEntity?.Components.Get<MapStorageComponent>();
                var targetStorage = GetStorage();

                MyLog.Default.WriteLine($"{Config.LogPrefix} [Exchange] source storage={(sourceStorage != null ? $"chunks={sourceStorage.Grid.Chunks.Count} cells={sourceStorage.Grid.CellCount}" : "NULL")} target storage={(targetStorage != null ? $"chunks={targetStorage.Grid.Chunks.Count} cells={targetStorage.Grid.CellCount}" : "NULL")}");

                if (sourceStorage != null && targetStorage != null)
                {
                    int cellsBefore = targetStorage.Grid.CellCount;
                    targetStorage.MergeData(sourceStorage.Grid);
                    int cellsAfter = targetStorage.Grid.CellCount;
                    MyLog.Default.WriteLine($"{Config.LogPrefix} [Exchange] Merged: target cells {cellsBefore} -> {cellsAfter}");
                    MapSession.Instance?.Networking.SendToAll(new PacketChunkSync(Entity.EntityId, targetStorage.Grid.GetSerializedChunks()));
                    MyAPIGateway.Utilities.ShowNotification("Data exchange complete.", 2000, MyFontEnum.Green);
                }
                else
                {
                    MyLog.Default.WriteLine($"{Config.LogPrefix} [Exchange] FAILED: sourceStorage={sourceStorage != null} targetStorage={targetStorage != null}");
                }
            }
            else
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} [Exchange] Client sending request: source={SelectedExchangeEntityId} target={Entity.EntityId}");
                MapSession.Instance.Networking.SendToServer(new PacketExchangeRequest(SelectedExchangeEntityId, Entity.EntityId));
                MyAPIGateway.Utilities.ShowNotification("Exchange requested...", 2000, MyFontEnum.Green);
            }
        }

        public void UpdateVisuals()
        {
            if (_antenna != null)
            {
                bool original = _antenna.ShowInTerminal;
                _antenna.ShowInTerminal = !original;
                _antenna.ShowInTerminal = original;
            }
        }

        public override void Close()
        {
            if (_antenna != null)
            {
                _antenna.AppendingCustomInfo -= AppendCustomInfo;
                _antenna.PropertiesChanged -= AntennaPropertiesChanged;
            }

            if (_storage != null)
            {
                _storage.DataChanged -= OnDataChanged;
            }

            _antenna = null;
            _storage = null;
        }
    }
}
