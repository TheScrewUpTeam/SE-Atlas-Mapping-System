using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace TSUT.MappingSystem
{
    public class SurfaceSettings
    {
        public float Zoom = 20f;
        public Vector2 Pan = Vector2.Zero;
        public bool RotateMap = true;
        public long HostAntennaEntityId = 0;
    }

    public abstract class DisplayFlatEntry : MyGameLogicComponent
    {
        public int SelectedSurfaceIndex = 0;
        public Dictionary<int, SurfaceSettings> Settings = new Dictionary<int, SurfaceSettings>();
        
        protected IMyTerminalBlock _block;
        private bool _isInitialized = false;
        private bool _refreshing = false;
        private int _lastMapSurfaceCount = -1;

        public static readonly Guid DisplaySettingsGuid = new Guid("A7373F26-3C72-4742-87C2-79013A63A295");

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _block = Entity as IMyTerminalBlock;
            if (_block == null || !(_block is IMyTextSurfaceProvider))
                return;

            _block.AppendingCustomInfo += AppendCustomInfo;
            _block.PropertiesChanged += OnPropertiesChanged;
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
            _isInitialized = true;
        }

        private void OnPropertiesChanged(IMyTerminalBlock block)
        {
            if (_refreshing) return;
            if (!(block is IMyTextSurfaceProvider)) return;

            int current = CountMapSurfaces();
            if (current == _lastMapSurfaceCount) return;
            _lastMapSurfaceCount = current;

            _refreshing = true;
            UpdateVisuals();
            _refreshing = false;
        }

        private void AppendCustomInfo(IMyTerminalBlock block, StringBuilder sb)
        {
            if (HasAnyMapSurface())
            {
                var provider = block as IMyTextSurfaceProvider;
                if (provider != null)
                {
                    var surface = provider.GetSurface(SelectedSurfaceIndex);
                    string name = surface.DisplayName;
                    sb.AppendLine();
                    sb.Append("AMS Selected Screen: Screen #").Append(SelectedSurfaceIndex).Append(" (").Append(name).Append(")");
                }
            }
        }

        public override void UpdateAfterSimulation100()
        {
            if (ValidateAndCleanupSettings())
            {
                Save();
            }
        }

        public bool ValidateAndCleanupSettings()
        {
            var provider = _block as IMyTextSurfaceProvider;
            if (provider == null) return false;

            List<int> mapSurfaces = new List<int>();
            for (int i = 0; i < provider.SurfaceCount; i++)
            {
                var surface = provider.GetSurface(i);
                if (surface != null && surface.Script == "AMS_MapApp")
                    mapSurfaces.Add(i);
            }

            bool changed = false;

            List<int> toRemove = new List<int>();
            foreach (var key in Settings.Keys)
            {
                if (!mapSurfaces.Contains(key))
                {
                    toRemove.Add(key);
                }
            }

            if (toRemove.Count > 0)
            {
                foreach (var key in toRemove) Settings.Remove(key);
                changed = true;
            }

            if (mapSurfaces.Count > 0)
            {
                if (!mapSurfaces.Contains(SelectedSurfaceIndex))
                {
                    SelectedSurfaceIndex = mapSurfaces[0];
                    changed = true;
                    _block?.SetDetailedInfoDirty();
                    _block?.RefreshCustomInfo();
                    UpdateVisuals();
                }
            }

            return changed;
        }

        protected virtual void RegisterTerminalControls() { }

        public override void UpdateOnceBeforeFrame()
        {
            RegisterTerminalControls();

            Load();
            ValidateAndCleanupSettings();
            _lastMapSurfaceCount = CountMapSurfaces();

            if (_block != null)
            {
                _block.SetDetailedInfoDirty();
                _block.RefreshCustomInfo();
            }
        }

        public SurfaceSettings GetSettings(int index)
        {
            SurfaceSettings s;
            if (!Settings.TryGetValue(index, out s))
            {
                s = new SurfaceSettings();
                Settings[index] = s;
            }
            return s;
        }

        public void CycleNextSurface()
        {
            ValidateAndCleanupSettings();

            var provider = _block as IMyTextSurfaceProvider;
            if (provider == null) return;

            int count = provider.SurfaceCount;
            if (count <= 1) return;

            List<int> mapSurfaces = new List<int>();
            for (int i = 0; i < count; i++)
            {
                var surface = provider.GetSurface(i);
                if (surface != null && surface.Script == "AMS_MapApp")
                    mapSurfaces.Add(i);
            }

            if (mapSurfaces.Count <= 1) return;

            int listIdx = mapSurfaces.IndexOf(SelectedSurfaceIndex);
            if (listIdx == -1)
            {
                SelectedSurfaceIndex = mapSurfaces[0];
            }
            else
            {
                SelectedSurfaceIndex = mapSurfaces[(listIdx + 1) % mapSurfaces.Count];
            }

            if (_block != null)
            {
                _block.SetDetailedInfoDirty();
                _block.RefreshCustomInfo();
            }
            UpdateVisuals();
            Save();
            SyncToNetwork();
        }

        public int CountMapSurfaces()
        {
            var provider = _block as IMyTextSurfaceProvider;
            if (provider == null) return 0;

            int count = 0;
            for (int i = 0; i < provider.SurfaceCount; i++)
            {
                var surface = provider.GetSurface(i);
                if (surface != null && surface.Script == "AMS_MapApp")
                    count++;
            }
            return count;
        }

        public bool HasAnyMapSurface() => CountMapSurfaces() > 0;

        public void UpdateVisuals()
        {
            if (_block != null)
            {
                _block.SetDetailedInfoDirty();
                _block.RefreshCustomInfo();
                bool original = _block.ShowInTerminal;
                _block.ShowInTerminal = !original;
                _block.ShowInTerminal = original;
            }
        }

        public string BuildSaveString()
        {
            var sb = new StringBuilder();
            sb.Append(SelectedSurfaceIndex).Append(";");
            foreach (var kvp in Settings)
            {
                sb.Append(kvp.Key).Append(",")
                  .Append(kvp.Value.Zoom).Append(",")
                  .Append(kvp.Value.Pan.X).Append(",")
                  .Append(kvp.Value.Pan.Y).Append(",")
                  .Append(kvp.Value.RotateMap ? "1" : "0").Append(",")
                  .Append(kvp.Value.HostAntennaEntityId).Append("|");
            }
            return sb.ToString();
        }

        public void LoadFromString(string raw)
        {
            try
            {
                if (string.IsNullOrEmpty(raw)) return;
                string[] parts = raw.Split(';');
                if (parts.Length < 2) return;
                int.TryParse(parts[0], out SelectedSurfaceIndex);
                string[] entries = parts[1].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var entry in entries)
                {
                    string[] vals = entry.Split(',');
                    if (vals.Length < 5) continue;
                    int idx;
                    if (int.TryParse(vals[0], out idx))
                    {
                        var s = new SurfaceSettings();
                        float.TryParse(vals[1], out s.Zoom);
                        float.TryParse(vals[2], out s.Pan.X);
                        float.TryParse(vals[3], out s.Pan.Y);
                        s.RotateMap = vals[4] == "1";
                        if (vals.Length >= 6) long.TryParse(vals[5], out s.HostAntennaEntityId);
                        Settings[idx] = s;
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error loading display settings: {ex.Message}");
            }
        }

        public void SyncToNetwork()
        {
            if (_block == null || !MyAPIGateway.Multiplayer.MultiplayerActive || MapSession.Instance == null) return;
            var packet = new PacketDisplaySync(_block.EntityId, BuildSaveString());
            if (MyAPIGateway.Session.IsServer)
                MapSession.Instance.Networking.SendToAll(packet);
            else
                MapSession.Instance.Networking.SendToServer(packet);
        }

        public void Save()
        {
            try
            {
                if (_block == null) return;
                if (_block.Storage == null) _block.Storage = new MyModStorageComponent();
                _block.Storage[DisplaySettingsGuid] = BuildSaveString();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error saving display settings: {ex.Message}");
            }
        }

        public void Load()
        {
            try
            {
                if (_block == null || _block.Storage == null || !_block.Storage.ContainsKey(DisplaySettingsGuid))
                    return;
                LoadFromString(_block.Storage[DisplaySettingsGuid]);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Error loading display settings: {ex.Message}");
            }
        }

        public override void Close()
        {
            if (_isInitialized)
            {
                if (_block != null)
                {
                    _block.AppendingCustomInfo -= AppendCustomInfo;
                    _block.PropertiesChanged -= OnPropertiesChanged;
                }
                Save();
            }
            _block = null;
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TextPanel), false)]
    public class DisplayFlatEntry_TextPanel : DisplayFlatEntry
    {
        protected override void RegisterTerminalControls() => DisplayTerminalControls.RegisterForTextPanel();
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Cockpit), false)]
    public class DisplayFlatEntry_Cockpit : DisplayFlatEntry
    {
        protected override void RegisterTerminalControls() => DisplayTerminalControls.RegisterForCockpit();
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CryoChamber), false)]
    public class DisplayFlatEntry_CryoChamber : DisplayFlatEntry
    {
        protected override void RegisterTerminalControls() => DisplayTerminalControls.RegisterForCryoChamber();
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ButtonPanel), false)]
    public class DisplayFlatEntry_ButtonPanel : DisplayFlatEntry
    {
        protected override void RegisterTerminalControls() => DisplayTerminalControls.RegisterForButtonPanel();
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MyProgrammableBlock), false)]
    public class DisplayFlatEntry_ProgrammableBlock : DisplayFlatEntry
    {
        protected override void RegisterTerminalControls() => DisplayTerminalControls.RegisterForProgrammableBlock();
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SurvivalKit), false)]
    public class DisplayFlatEntry_SurvivalKit : DisplayFlatEntry { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MedicalRoom), false)]
    public class DisplayFlatEntry_MedicalRoom : DisplayFlatEntry
    {
        protected override void RegisterTerminalControls() => DisplayTerminalControls.RegisterForMedicalRoom();
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_StoreBlock), false)]
    public class DisplayFlatEntry_StoreBlock : DisplayFlatEntry
    {
        protected override void RegisterTerminalControls() => DisplayTerminalControls.RegisterForStoreBlock();
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ContractBlock), false)]
    public class DisplayFlatEntry_ContractBlock : DisplayFlatEntry { }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_VendingMachine), false)]
    public class DisplayFlatEntry_VendingMachine : DisplayFlatEntry
    {
        protected override void RegisterTerminalControls() => DisplayTerminalControls.RegisterForVendingMachine();
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SafeZone), false)]
    public class DisplayFlatEntry_SafeZoneBlock : DisplayFlatEntry
    {
        protected override void RegisterTerminalControls() => DisplayTerminalControls.RegisterForSafeZoneBlock();
    }
}
