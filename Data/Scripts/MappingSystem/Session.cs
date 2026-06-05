using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;

namespace TSUT.MappingSystem
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public partial class MapSession : MySessionComponentBase
    {
        public static MapSession Instance { get; private set; }
        public Config Config;
        public ScanScheduler Scheduler;
        public Networking Networking;
        public MappingContractSystem Contracts;
        public List<ClientContractInfo> ClientContracts = new List<ClientContractInfo>();

        private int _ticks = 0;
        private bool _contractSpawnPending = true;

        public override void LoadData()
        {
            Instance = this;
            Config = Config.Instance;

            Networking = new Networking(Config.NetworkId);
            Networking.Register();

            Scheduler = new ScanScheduler();

            Contracts = new MappingContractSystem();
            Contracts.Init();
        }

        public override void BeforeStart()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControlGetter;
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;

            if (!MyAPIGateway.Session.IsServer)
                Networking.SendToServer(new PacketContractRequest());
        }

        protected override void UnloadData()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter -= OnCustomControlGetter;
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
            Contracts?.Unload();
            Networking?.Unregister();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            var cmd = messageText.Trim().ToLower();

            if (cmd == "/mapdebug")
            {
                sendToOthers = false;
                ScanScheduler.DebugVisualize = !ScanScheduler.DebugVisualize;
                ScanScheduler.DebugRaysList.Clear();
                MyAPIGateway.Utilities.ShowNotification($"Debug rays: {(ScanScheduler.DebugVisualize ? "ON" : "OFF")}", 3000);
                return;
            }

            if (cmd == "/mapdebugnomiss")
            {
                sendToOthers = false;
                ScanScheduler.DebugRaysList.RemoveAll(r => !r.Hit);
                MyAPIGateway.Utilities.ShowNotification($"Misses removed. {ScanScheduler.DebugRaysList.Count} hits remain.", 3000);
                return;
            }

            if (cmd != "/mapspawn" && cmd != "/mapresetcontracts") return;
            sendToOthers = false;

            if (!MyAPIGateway.Session.IsUserAdmin(MyAPIGateway.Multiplayer.MyId))
            {
                MyAPIGateway.Utilities.ShowNotification("Insufficient permissions.", 3000);
                return;
            }

            if (cmd == "/mapresetcontracts")
            {
                Contracts?.ResetSpawnedContracts();
                MyAPIGateway.Utilities.ShowNotification("Pending mapping contracts reset and respawned.", 3000);
            }
            else
            {
                Contracts?.SpawnContractsAtNPCStations();
                MyAPIGateway.Utilities.ShowNotification("Checking mapping contracts...", 3000);
            }
        }

        public override void UpdateAfterSimulation()
        {
            if (_contractSpawnPending && MyAPIGateway.Session.IsServer)
            {
                _contractSpawnPending = false;
                Contracts?.LoadContractMetas();
                Contracts?.SpawnContractsAtNPCStations();

                // Push restored contracts to local player (listen server / single player)
                var localPlayer = MyAPIGateway.Session.Player;
                if (localPlayer != null)
                    Contracts?.SendContractsToPlayer(MyAPIGateway.Multiplayer.MyId, localPlayer.IdentityId);
            }

            if (Scheduler != null)
            {
                Scheduler.Update();
            }

            Contracts?.Update();

            _ticks++;

            UpdateVisuals();
        }
    }
}
