using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace TSUT.MappingSystem
{
    public static class ProjectorTerminalControls
    {
        static bool _registered = false;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            RegisterProjectorControls();
            RegisterProjectorActions();
        }

        static void RegisterProjectorActions()
        {
            Func<IMyTerminalBlock, bool> isEnabled = (b) => b.GameLogic?.GetAs<HolotableEntry>() != null;

            var toggle = MyAPIGateway.TerminalControls.CreateAction<IMyProjector>("AMS_HolotableToggleAction");
            toggle.Name = new StringBuilder("Toggle Map Projection");
            toggle.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            toggle.Action = (b) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Enabled = !entry.Settings.Enabled;
                    entry.UpdateProjection();
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            toggle.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) sb.Append(entry.Settings.Enabled ? "Proj: ON" : "Proj: OFF");
            };
            toggle.Enabled = isEnabled;
            MyAPIGateway.TerminalControls.AddAction<IMyProjector>(toggle);

            var resCycle = MyAPIGateway.TerminalControls.CreateAction<IMyProjector>("AMS_HolotableResCycle");
            resCycle.Name = new StringBuilder("Cycle Projection Res");
            resCycle.Icon = @"Textures\GUI\Icons\Actions\Reset.dds";
            resCycle.Action = (b) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    int current = entry.Settings.Resolution;
                    if (current < 64) entry.Settings.Resolution = 64;
                    else if (current < 96) entry.Settings.Resolution = 96;
                    else if (current < 128) entry.Settings.Resolution = 128;
                    else entry.Settings.Resolution = 32;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            resCycle.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) sb.Append(entry.Settings.Resolution).Append("px");
            };
            resCycle.Enabled = isEnabled;
            MyAPIGateway.TerminalControls.AddAction<IMyProjector>(resCycle);

            var zoomIn = MyAPIGateway.TerminalControls.CreateAction<IMyProjector>("AMS_HolotableZoomIn");
            zoomIn.Name = new StringBuilder("Proj Zoom In");
            zoomIn.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            zoomIn.Action = (b) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Zoom = MathHelper.Clamp(entry.Settings.Zoom - 100f, 10f, 2000f);
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            zoomIn.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) sb.Append(Math.Round(entry.Settings.Zoom, 0)).Append("m");
            };
            zoomIn.Enabled = isEnabled;
            MyAPIGateway.TerminalControls.AddAction<IMyProjector>(zoomIn);

            var zoomOut = MyAPIGateway.TerminalControls.CreateAction<IMyProjector>("AMS_HolotableZoomOut");
            zoomOut.Name = new StringBuilder("Proj Zoom Out");
            zoomOut.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
            zoomOut.Action = (b) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Zoom = MathHelper.Clamp(entry.Settings.Zoom + 100f, 10f, 2000f);
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            zoomOut.Writer = zoomIn.Writer;
            zoomOut.Enabled = isEnabled;
            MyAPIGateway.TerminalControls.AddAction<IMyProjector>(zoomOut);

            var panXIncr = MyAPIGateway.TerminalControls.CreateAction<IMyProjector>("AMS_HolotablePanXIncr");
            panXIncr.Name = new StringBuilder("Proj Pan X +1km");
            panXIncr.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            panXIncr.Action = (b) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Pan.X += 1000f;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            panXIncr.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) sb.Append(Math.Round(entry.Settings.Pan.X, 0));
            };
            panXIncr.Enabled = isEnabled;
            MyAPIGateway.TerminalControls.AddAction<IMyProjector>(panXIncr);

            var panXDecr = MyAPIGateway.TerminalControls.CreateAction<IMyProjector>("AMS_HolotablePanXDecr");
            panXDecr.Name = new StringBuilder("Proj Pan X -1km");
            panXDecr.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
            panXDecr.Action = (b) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Pan.X -= 1000f;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            panXDecr.Writer = panXIncr.Writer;
            panXDecr.Enabled = isEnabled;
            MyAPIGateway.TerminalControls.AddAction<IMyProjector>(panXDecr);

            var panYIncr = MyAPIGateway.TerminalControls.CreateAction<IMyProjector>("AMS_HolotablePanYIncr");
            panYIncr.Name = new StringBuilder("Proj Pan Y +1km");
            panYIncr.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            panYIncr.Action = (b) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Pan.Y += 1000f;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            panYIncr.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) sb.Append(Math.Round(entry.Settings.Pan.Y, 0));
            };
            panYIncr.Enabled = isEnabled;
            MyAPIGateway.TerminalControls.AddAction<IMyProjector>(panYIncr);

            var panYDecr = MyAPIGateway.TerminalControls.CreateAction<IMyProjector>("AMS_HolotablePanYDecr");
            panYDecr.Name = new StringBuilder("Proj Pan Y -1km");
            panYDecr.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
            panYDecr.Action = (b) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Pan.Y -= 1000f;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            panYDecr.Writer = panYIncr.Writer;
            panYDecr.Enabled = isEnabled;
            MyAPIGateway.TerminalControls.AddAction<IMyProjector>(panYDecr);

            var vScaleIncr = MyAPIGateway.TerminalControls.CreateAction<IMyProjector>("AMS_HolotableVScaleIncr");
            vScaleIncr.Name = new StringBuilder("Proj V-Scale Increase");
            vScaleIncr.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            vScaleIncr.Action = (b) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.VerticalScale = MathHelper.Clamp(entry.Settings.VerticalScale + 0.1f, 0.1f, 10f);
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            vScaleIncr.Writer = (b, sb) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) sb.Append(Math.Round(entry.Settings.VerticalScale, 1)).Append("x");
            };
            vScaleIncr.Enabled = isEnabled;
            MyAPIGateway.TerminalControls.AddAction<IMyProjector>(vScaleIncr);

            var vScaleDecr = MyAPIGateway.TerminalControls.CreateAction<IMyProjector>("AMS_HolotableVScaleDecr");
            vScaleDecr.Name = new StringBuilder("Proj V-Scale Decrease");
            vScaleDecr.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
            vScaleDecr.Action = (b) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.VerticalScale = MathHelper.Clamp(entry.Settings.VerticalScale - 0.1f, 0.1f, 10f);
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            vScaleDecr.Writer = vScaleIncr.Writer;
            vScaleDecr.Enabled = isEnabled;
            MyAPIGateway.TerminalControls.AddAction<IMyProjector>(vScaleDecr);
        }

        static void RegisterProjectorControls()
        {
            Func<IMyTerminalBlock, bool> isVisible = (b) => b.GameLogic?.GetAs<HolotableEntry>() != null;

            List<IMyTerminalControl> nativeControls;
            MyAPIGateway.TerminalControls.GetControls<IMyProjector>(out nativeControls);
            var lockIds = new HashSet<string> { "X", "Y", "Z", "RotX" };
            foreach (var ctrl in nativeControls)
            {
                if (!lockIds.Contains(ctrl.Id)) continue;
                var prev = ctrl.Enabled;
                ctrl.Enabled = (b) =>
                {
                    var entry = b?.GameLogic?.GetAs<HolotableEntry>();
                    if (entry != null && entry.Settings.AutoCompensate) return false;
                    return prev == null || prev(b);
                };
            }

            var enable = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyProjector>("AMS_HolotableEnable");
            enable.Title = MyStringId.GetOrCompute("Enable Map Projection");
            enable.Getter = (b) => b.GameLogic?.GetAs<HolotableEntry>()?.Settings.Enabled ?? false;
            enable.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Enabled = v;
                    entry.UpdateProjection();
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            enable.Visible = isVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(enable);

            var resSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyProjector>("AMS_HolotableRes");
            resSlider.Title = MyStringId.GetOrCompute("Map Resolution");
            resSlider.SetLimits(32, 128);
            resSlider.Getter = (b) => b.GameLogic?.GetAs<HolotableEntry>()?.Settings.Resolution ?? 64;
            resSlider.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Resolution = (int)v;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            resSlider.Writer = (b, sb) => sb.Append((int)resSlider.Getter(b)).Append("x").Append((int)resSlider.Getter(b));
            resSlider.Visible = isVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(resSlider);

            var zoomSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyProjector>("AMS_HolotableZoom");
            zoomSlider.Title = MyStringId.GetOrCompute("Map Zoom (Scale)");
            zoomSlider.SetLimits(10, 2000);
            zoomSlider.Getter = (b) => b.GameLogic?.GetAs<HolotableEntry>()?.Settings.Zoom ?? 100f;
            zoomSlider.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Zoom = v;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            zoomSlider.Writer = (b, sb) => sb.Append(Math.Round(zoomSlider.Getter(b), 0)).Append("m");
            zoomSlider.Visible = isVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(zoomSlider);

            var scaleSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyProjector>("AMS_HolotableScale");
            scaleSlider.Title = MyStringId.GetOrCompute("Vertical Scale");
            scaleSlider.SetLimits(0.1f, 10f);
            scaleSlider.Getter = (b) => b.GameLogic?.GetAs<HolotableEntry>()?.Settings.VerticalScale ?? 1.0f;
            scaleSlider.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.VerticalScale = v;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            scaleSlider.Writer = (b, sb) => sb.Append(scaleSlider.Getter(b)).Append("x");
            scaleSlider.Visible = isVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(scaleSlider);

            var panX = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyProjector>("AMS_HolotablePanX");
            panX.Title = MyStringId.GetOrCompute("Pan X");
            panX.SetLimits(-50000, 50000);
            panX.Getter = (b) => b.GameLogic?.GetAs<HolotableEntry>()?.Settings.Pan.X ?? 0f;
            panX.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Pan.X = v;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            panX.Writer = (b, sb) => sb.Append(Math.Round(panX.Getter(b), 0));
            panX.Visible = isVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(panX);

            var panY = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyProjector>("AMS_HolotablePanY");
            panY.Title = MyStringId.GetOrCompute("Pan Y");
            panY.SetLimits(-50000, 50000);
            panY.Getter = (b) => b.GameLogic?.GetAs<HolotableEntry>()?.Settings.Pan.Y ?? 0f;
            panY.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.Pan.Y = v;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            panY.Writer = (b, sb) => sb.Append(Math.Round(panY.Getter(b), 0));
            panY.Visible = isVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(panY);

            var rotateMap = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyProjector>("AMS_HolotableRotate");
            rotateMap.Title = MyStringId.GetOrCompute("Rotate Map");
            rotateMap.Getter = (b) => b.GameLogic?.GetAs<HolotableEntry>()?.Settings.RotateMap ?? true;
            rotateMap.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.RotateMap = v;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            rotateMap.Visible = isVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(rotateMap);

            var updateInterval = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyProjector>("AMS_HolotableUpdateInterval");
            updateInterval.Title = MyStringId.GetOrCompute("Update Interval");
            updateInterval.SetLimits(1, 120);
            updateInterval.Getter = (b) => b.GameLogic?.GetAs<HolotableEntry>()?.Settings.UpdateIntervalSeconds ?? 5;
            updateInterval.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.UpdateIntervalSeconds = Math.Max(1, (int)Math.Round(v));
                    entry.Save();
                    entry.SyncToNetwork();
                }
            };
            updateInterval.Writer = (b, sb) => sb.Append(updateInterval.Getter(b)).Append("s");
            updateInterval.Visible = isVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(updateInterval);

            var autoCompensate = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyProjector>("AMS_HolotableAutoCompensate");
            autoCompensate.Title = MyStringId.GetOrCompute("Auto Compensate Movement");
            autoCompensate.Tooltip = MyStringId.GetOrCompute("Locks offset/rotation sliders and uses them to visually track ship movement between map updates.");
            autoCompensate.Getter = (b) => b.GameLogic?.GetAs<HolotableEntry>()?.Settings.AutoCompensate ?? false;
            autoCompensate.Setter = (b, v) => {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.SetAutoCompensate(v);
                    entry.SyncToNetwork();
                }
            };
            autoCompensate.Visible = isVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(autoCompensate);

            var antennaList = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyProjector>("AMS_HolotableAntenna");
            antennaList.Title = MyStringId.GetOrCompute("Source Antenna");
            antennaList.VisibleRowsCount = 5;
            antennaList.Multiselect = false;
            antennaList.ListContent = (b, items, selected) =>
            {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry == null) return;
                var grid = b.CubeGrid;
                var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
                if (terminalSystem == null) return;
                var antennas = new List<IMyRadioAntenna>();
                terminalSystem.GetBlocksOfType(antennas, (antenna) => antenna.IsWorking);
                bool selectionFound = false;
                foreach (var antenna in antennas)
                {
                    var item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(antenna.CustomName), MyStringId.GetOrCompute("Antenna"), antenna.EntityId);
                    items.Add(item);
                    if (entry.Settings.HostAntennaEntityId == antenna.EntityId) { selected.Add(item); selectionFound = true; }
                }
                if (!selectionFound && entry.Settings.HostAntennaEntityId != 0) { entry.Settings.HostAntennaEntityId = 0; }
            };
            antennaList.ItemSelected = (b, selected) =>
            {
                var entry = b.GameLogic?.GetAs<HolotableEntry>();
                if (entry != null) {
                    entry.Settings.HostAntennaEntityId = selected.Count > 0 ? (long)selected[0].UserData : 0;
                    entry.MarkDirty();
                    entry.SyncToNetwork();
                }
            };
            antennaList.Visible = isVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyProjector>(antennaList);
        }
    }
}
