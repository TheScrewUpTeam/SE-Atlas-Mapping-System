using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace TSUT.MappingSystem
{
    public static class DisplayTerminalControls
    {
        static bool _controlsCreated = false;
        static readonly HashSet<string> _registeredTypes = new HashSet<string>();
        static readonly List<IMyTerminalControl> _controls = new List<IMyTerminalControl>();
        static readonly List<IMyTerminalAction> _actions = new List<IMyTerminalAction>();

        static void EnsureCreated()
        {
            if (_controlsCreated) return;
            _controlsCreated = true;
            CreateControls();
            CreateActions();
        }

        static void RegisterForType<T>() where T : class, IMyTerminalBlock
        {
            if (!_registeredTypes.Add(typeof(T).Name)) return;
            EnsureCreated();
            foreach (var control in _controls)
                MyAPIGateway.TerminalControls.AddControl<T>(control);
            foreach (var action in _actions)
                MyAPIGateway.TerminalControls.AddAction<T>(action);
        }

        public static void RegisterForTextPanel()         => RegisterForType<IMyTextPanel>();
        public static void RegisterForCockpit()           => RegisterForType<IMyCockpit>();
        public static void RegisterForCryoChamber()       => RegisterForType<IMyCryoChamber>();
        public static void RegisterForButtonPanel()       => RegisterForType<IMyButtonPanel>();
        public static void RegisterForProgrammableBlock() => RegisterForType<IMyProgrammableBlock>();
        public static void RegisterForMedicalRoom()       => RegisterForType<IMyMedicalRoom>();
        public static void RegisterForStoreBlock()        => RegisterForType<IMyStoreBlock>();
        public static void RegisterForVendingMachine()    => RegisterForType<IMyVendingMachine>();
        public static void RegisterForSafeZoneBlock()     => RegisterForType<IMySafeZoneBlock>();

        static void CreateControls()
        {
            var nextScreenButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyTerminalBlock>("AMS_NextScreen");
            nextScreenButton.Title = MyStringId.GetOrCompute("Next Map Screen");
            nextScreenButton.Action = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.CycleNextSurface();
            nextScreenButton.Visible = (b) => {
                var provider = b as IMyTextSurfaceProvider;
                if (provider == null) return false;
                int mapCount = 0;
                for (int i = 0; i < provider.SurfaceCount; i++)
                {
                    var surface = provider.GetSurface(i);
                    if (surface != null && surface.Script == "AMS_MapApp") mapCount++;
                }
                return mapCount > 1;
            };
            _controls.Add(nextScreenButton);

            var zoomSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyTerminalBlock>("AMS_ZoomSlider");
            zoomSlider.Title = MyStringId.GetOrCompute("Map Zoom");
            zoomSlider.Tooltip = MyStringId.GetOrCompute("Adjust map zoom for selected screen.");
            zoomSlider.SetLimits(1f, 500f);
            zoomSlider.Getter = (b) => {
                var e = b.GameLogic?.GetAs<DisplayFlatEntry>();
                return e != null ? e.GetSettings(e.SelectedSurfaceIndex).Zoom : 20f;
            };
            zoomSlider.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    entry.GetSettings(entry.SelectedSurfaceIndex).Zoom = v;
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            zoomSlider.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                sb.Append(Math.Round(entry != null ? entry.GetSettings(entry.SelectedSurfaceIndex).Zoom : 20f, 1)).Append(" m/px");
            };
            zoomSlider.Visible = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
            _controls.Add(zoomSlider);

            var panXSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyTerminalBlock>("AMS_PanX");
            panXSlider.Title = MyStringId.GetOrCompute("Map Pan X (Offset)");
            panXSlider.SetLimits(-50000, 50000);
            panXSlider.Getter = (b) => { var e = b.GameLogic?.GetAs<DisplayFlatEntry>(); return e != null ? e.GetSettings(e.SelectedSurfaceIndex).Pan.X : 0f; };
            panXSlider.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    entry.GetSettings(entry.SelectedSurfaceIndex).Pan.X = v;
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            panXSlider.Writer = (b, sb) => { var e = b.GameLogic?.GetAs<DisplayFlatEntry>(); sb.Append(Math.Round(e != null ? e.GetSettings(e.SelectedSurfaceIndex).Pan.X : 0f, 0)); };
            panXSlider.Visible = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
            _controls.Add(panXSlider);

            var panYSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyTerminalBlock>("AMS_PanY");
            panYSlider.Title = MyStringId.GetOrCompute("Map Pan Y (Offset)");
            panYSlider.SetLimits(-50000, 50000);
            panYSlider.Getter = (b) => { var e = b.GameLogic?.GetAs<DisplayFlatEntry>(); return e != null ? e.GetSettings(e.SelectedSurfaceIndex).Pan.Y : 0f; };
            panYSlider.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    entry.GetSettings(entry.SelectedSurfaceIndex).Pan.Y = v;
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            panYSlider.Writer = (b, sb) => { var e = b.GameLogic?.GetAs<DisplayFlatEntry>(); sb.Append(Math.Round(e != null ? e.GetSettings(e.SelectedSurfaceIndex).Pan.Y : 0f, 0)); };
            panYSlider.Visible = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
            _controls.Add(panYSlider);

            var rotateCheckbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyTerminalBlock>("AMS_RotateMap");
            rotateCheckbox.Title = MyStringId.GetOrCompute("Rotate Map");
            rotateCheckbox.Getter = (b) => { var e = b.GameLogic?.GetAs<DisplayFlatEntry>(); return e != null ? e.GetSettings(e.SelectedSurfaceIndex).RotateMap : true; };
            rotateCheckbox.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    entry.GetSettings(entry.SelectedSurfaceIndex).RotateMap = v;
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            rotateCheckbox.Visible = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
            _controls.Add(rotateCheckbox);

            var antennaList = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyTerminalBlock>("AMS_AntennaList");
            antennaList.Title = MyStringId.GetOrCompute("Source Antenna");
            antennaList.VisibleRowsCount = 5;
            antennaList.Multiselect = false;
            antennaList.ListContent = (b, items, selected) =>
            {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry == null) return;

                var settings = entry.GetSettings(entry.SelectedSurfaceIndex);
                bool selectionFound = false;
                foreach (var antenna in GetWorkingAntennas(b))
                {
                    var item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(antenna.CustomName), MyStringId.GetOrCompute("Antenna"), antenna.EntityId);
                    items.Add(item);
                    if (settings.HostAntennaEntityId == antenna.EntityId)
                    {
                        selected.Add(item);
                        selectionFound = true;
                    }
                }

                if (!selectionFound && settings.HostAntennaEntityId != 0)
                {
                    settings.HostAntennaEntityId = 0;
                    entry.UpdateVisuals();
                }
            };
            antennaList.ItemSelected = (b, selected) =>
            {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null)
                {
                    var settings = entry.GetSettings(entry.SelectedSurfaceIndex);
                    settings.HostAntennaEntityId = selected.Count > 0 ? (long)selected[0].UserData : 0;
                    entry.UpdateVisuals();
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            antennaList.Visible = (b) => {
                bool hasMap = b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
                int antennaCount = GetWorkingAntennas(b).Count;
                return hasMap && antennaCount > 1;
            };
            _controls.Add(antennaList);
        }

        static void CreateActions()
        {
            var nextAction = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("AMS_NextScreenAction");
            nextAction.Name = new StringBuilder("Next Map Screen");
            nextAction.Icon = @"Textures\GUI\Icons\Actions\Right.dds";
            nextAction.Action = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.CycleNextSurface();
            nextAction.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    var provider = b as IMyTextSurfaceProvider;
                    if (provider != null)
                        sb.Append(provider.GetSurface(entry.SelectedSurfaceIndex).DisplayName);
                }
            };
            nextAction.Enabled = (b) => {
                var provider = b as IMyTextSurfaceProvider;
                if (provider == null) return false;
                int mapCount = 0;
                for (int i = 0; i < provider.SurfaceCount; i++)
                {
                    var surface = provider.GetSurface(i);
                    if (surface != null && surface.Script == "AMS_MapApp") mapCount++;
                }
                return mapCount > 1;
            };
            _actions.Add(nextAction);

            var zoomIn = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("AMS_ZoomIn");
            zoomIn.Name = new StringBuilder("Map Zoom In");
            zoomIn.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            zoomIn.Action = (b) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    var s = entry.GetSettings(entry.SelectedSurfaceIndex);
                    s.Zoom = MathHelper.Clamp(s.Zoom / 1.25f, 1f, 500f);
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            zoomIn.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) sb.Append(Math.Round(entry.GetSettings(entry.SelectedSurfaceIndex).Zoom, 1)).Append("m");
            };
            zoomIn.Enabled = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
            _actions.Add(zoomIn);

            var zoomOut = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("AMS_ZoomOut");
            zoomOut.Name = new StringBuilder("Map Zoom Out");
            zoomOut.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
            zoomOut.Action = (b) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    var s = entry.GetSettings(entry.SelectedSurfaceIndex);
                    s.Zoom = MathHelper.Clamp(s.Zoom * 1.25f, 1f, 500f);
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            zoomOut.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) sb.Append(Math.Round(entry.GetSettings(entry.SelectedSurfaceIndex).Zoom, 1)).Append("m");
            };
            zoomOut.Enabled = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
            _actions.Add(zoomOut);

            var panXIncr = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("AMS_PanXIncr");
            panXIncr.Name = new StringBuilder("Map Pan X +1km");
            panXIncr.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            panXIncr.Action = (b) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    var s = entry.GetSettings(entry.SelectedSurfaceIndex);
                    s.Pan.X += 1000f;
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            panXIncr.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) sb.Append(Math.Round(entry.GetSettings(entry.SelectedSurfaceIndex).Pan.X, 0));
            };
            panXIncr.Enabled = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
            _actions.Add(panXIncr);

            var panXDecr = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("AMS_PanXDecr");
            panXDecr.Name = new StringBuilder("Map Pan X -1km");
            panXDecr.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
            panXDecr.Action = (b) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    var s = entry.GetSettings(entry.SelectedSurfaceIndex);
                    s.Pan.X -= 1000f;
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            panXDecr.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) sb.Append(Math.Round(entry.GetSettings(entry.SelectedSurfaceIndex).Pan.X, 0));
            };
            panXDecr.Enabled = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
            _actions.Add(panXDecr);

            var panYIncr = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("AMS_PanYIncr");
            panYIncr.Name = new StringBuilder("Map Pan Y +1km");
            panYIncr.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
            panYIncr.Action = (b) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    var s = entry.GetSettings(entry.SelectedSurfaceIndex);
                    s.Pan.Y += 1000f;
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            panYIncr.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) sb.Append(Math.Round(entry.GetSettings(entry.SelectedSurfaceIndex).Pan.Y, 0));
            };
            panYIncr.Enabled = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
            _actions.Add(panYIncr);

            var panYDecr = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("AMS_PanYDecr");
            panYDecr.Name = new StringBuilder("Map Pan Y -1km");
            panYDecr.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            panYDecr.Action = (b) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    var s = entry.GetSettings(entry.SelectedSurfaceIndex);
                    s.Pan.Y -= 1000f;
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            panYDecr.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) sb.Append(Math.Round(entry.GetSettings(entry.SelectedSurfaceIndex).Pan.Y, 0));
            };
            panYDecr.Enabled = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
            _actions.Add(panYDecr);

            var rotateToggle = MyAPIGateway.TerminalControls.CreateAction<IMyTerminalBlock>("AMS_RotateToggle");
            rotateToggle.Name = new StringBuilder("Toggle Map Rotation");
            rotateToggle.Icon = @"Textures\GUI\Icons\Actions\Reset.dds";
            rotateToggle.Action = (b) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) {
                    var s = entry.GetSettings(entry.SelectedSurfaceIndex);
                    s.RotateMap = !s.RotateMap;
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            rotateToggle.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<DisplayFlatEntry>();
                if (entry != null) sb.Append(entry.GetSettings(entry.SelectedSurfaceIndex).RotateMap ? "Rot: ON" : "Rot: OFF");
            };
            rotateToggle.Enabled = (b) => b.GameLogic?.GetAs<DisplayFlatEntry>()?.HasAnyMapSurface() ?? false;
            _actions.Add(rotateToggle);
        }

        static List<IMyRadioAntenna> GetWorkingAntennas(IMyTerminalBlock block)
        {
            var list = new List<IMyRadioAntenna>();
            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(block.CubeGrid);
            terminalSystem?.GetBlocksOfType(list, (a) => a.IsWorking);
            return list;
        }
    }
}
