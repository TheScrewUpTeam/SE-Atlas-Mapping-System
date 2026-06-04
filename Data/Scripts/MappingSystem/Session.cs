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
        private const int SyncIntervalTicks = 300; // Sync every 5 seconds
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
            if (messageText.Trim().ToLower() == "/mapdebug")
            {
                sendToOthers = false;
                if (Scheduler != null)
                {
                    Scheduler.DebugVisualize = !Scheduler.DebugVisualize;
                    Scheduler.DebugRaysList.Clear();
                    MyAPIGateway.Utilities.ShowNotification($"Scan debug rays: {(Scheduler.DebugVisualize ? "ON — start a scan" : "OFF")}", 4000);
                }
                return;
            }

            if (messageText.Trim().ToLower() == "/mapdebugnomiss")
            {
                sendToOthers = false;
                if (Scheduler != null)
                {
                    int before = Scheduler.DebugRaysList.Count;
                    Scheduler.DebugRaysList.RemoveAll(r => !r.Hit);
                    int removed = before - Scheduler.DebugRaysList.Count;
                    MyAPIGateway.Utilities.ShowNotification($"Removed {removed} missed rays, {Scheduler.DebugRaysList.Count} hits remain.", 4000);
                }
                return;
            }

            if (messageText.Trim().ToLower() != "/mapspawn") return;
            sendToOthers = false;

            if (!MyAPIGateway.Session.IsUserAdmin(MyAPIGateway.Multiplayer.MyId))
            {
                MyAPIGateway.Utilities.ShowNotification("Insufficient permissions.", 3000);
                return;
            }

            Contracts?.SpawnContractsAtNPCStations();
            MyAPIGateway.Utilities.ShowNotification("Checking mapping contracts...", 3000);
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

            if (++_ticks % SyncIntervalTicks == 0)
            {
                PerformRadioSync();
            }

            UpdateVisuals();
        }
    }
}
