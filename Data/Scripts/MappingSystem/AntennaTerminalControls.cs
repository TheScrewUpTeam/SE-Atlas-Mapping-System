using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace TSUT.MappingSystem
{
    public static class AntennaTerminalControls
    {
        static bool _registered = false;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            RegisterAntennaControls();
            RegisterAntennaActions();
        }

        static void RegisterAntennaActions()
        {
            var scanAction = MyAPIGateway.TerminalControls.CreateAction<IMyRadioAntenna>("AMS_ScanAction");
            scanAction.Name = new StringBuilder("Start/Stop scan");
            scanAction.Icon = @"Textures\GUI\Icons\Actions\Start.dds";
            scanAction.Action = (b) =>
            {
                var entry = b.GameLogic?.GetAs<ScannerEntry>();
                if (entry == null) return;
                if (entry.IsLocallyScanning)
                    entry.RequestStopScan();
                else
                    entry.RequestScan();
            };
            scanAction.Writer = (b, sb) =>
            {
                var entry = b.GameLogic?.GetAs<ScannerEntry>();
                if (entry == null) return;

                if (entry.IsLocallyScanning)
                {
                    sb.Append("Stop scan");
                }
                else
                {
                    float range = AntennaHelper.GetScanRadius(b as IMyRadioAntenna);
                    if (range >= 1000f)
                        sb.Append(Math.Round(range / 1000f, 1)).Append("km");
                    else
                        sb.Append(Math.Round(range)).Append("m");
                }
            };
            scanAction.Enabled = (b) => b.IsWorking;
            MyAPIGateway.TerminalControls.AddAction<IMyRadioAntenna>(scanAction);
        }

        static void RegisterAntennaControls()
        {
            var scanButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyRadioAntenna>("AMS_ScanButton");
            scanButton.Title = MyStringId.GetOrCompute("Start Map Scan");
            scanButton.Tooltip = MyStringId.GetOrCompute("Performs a spherical scan of the surrounding area.");
            scanButton.Action = (b) =>
            {
                var entry = b.GameLogic?.GetAs<ScannerEntry>();
                entry?.RequestScan();
            };
            scanButton.Enabled = (b) => b.IsWorking;
            scanButton.Visible = (b) => !(b.GameLogic?.GetAs<ScannerEntry>()?.IsLocallyScanning ?? false);
            scanButton.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyRadioAntenna>(scanButton);

            var stopButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyRadioAntenna>("AMS_StopScanButton");
            stopButton.Title = MyStringId.GetOrCompute("Stop Scan");
            stopButton.Tooltip = MyStringId.GetOrCompute("Stops the current scan in progress.");
            stopButton.Action = (b) =>
            {
                var entry = b.GameLogic?.GetAs<ScannerEntry>();
                entry?.RequestStopScan();
            };
            stopButton.Enabled = (b) => b.IsWorking;
            stopButton.Visible = (b) => b.GameLogic?.GetAs<ScannerEntry>()?.IsLocallyScanning ?? false;
            stopButton.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyRadioAntenna>(stopButton);

            var exchangeList = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyRadioAntenna>("AMS_ExchangeList");
            exchangeList.Title = MyStringId.GetOrCompute("Antenna to exchange");
            exchangeList.VisibleRowsCount = 5;
            exchangeList.Multiselect = false;
            exchangeList.ListContent = (b, items, selected) =>
            {
                var currentEntry = b.GameLogic?.GetAs<ScannerEntry>();
                if (currentEntry == null) return;

                var grid = b.CubeGrid;
                var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
                if (terminalSystem == null) return;

                var antennas = new List<IMyRadioAntenna>();
                terminalSystem.GetBlocksOfType(antennas, (antenna) => antenna != b && antenna.IsWorking);

                bool selectionFound = false;
                foreach (var antenna in antennas)
                {
                    var item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(antenna.CustomName), MyStringId.GetOrCompute("Antenna"), antenna.EntityId);
                    items.Add(item);
                    if (currentEntry.SelectedExchangeEntityId == antenna.EntityId)
                    {
                        selected.Add(item);
                        selectionFound = true;
                    }
                }

                if (!selectionFound && currentEntry.SelectedExchangeEntityId != 0)
                {
                    currentEntry.SelectedExchangeEntityId = 0;
                    currentEntry.UpdateVisuals();
                }
            };
            exchangeList.ItemSelected = (b, selected) =>
            {
                var entry = b.GameLogic?.GetAs<ScannerEntry>();
                if (entry != null)
                {
                    entry.SelectedExchangeEntityId = selected.Count > 0 ? (long)selected[0].UserData : 0;
                    entry.UpdateVisuals();
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyRadioAntenna>(exchangeList);

            var exchangeButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyRadioAntenna>("AMS_ExchangeButton");
            exchangeButton.Title = MyStringId.GetOrCompute("Download Map Data");
            exchangeButton.Action = (b) => b.GameLogic?.GetAs<ScannerEntry>()?.RequestExchange();
            exchangeButton.Enabled = (b) => b.GameLogic?.GetAs<ScannerEntry>()?.SelectedExchangeEntityId != 0;
            MyAPIGateway.TerminalControls.AddControl<IMyRadioAntenna>(exchangeButton);
        }
    }
}
