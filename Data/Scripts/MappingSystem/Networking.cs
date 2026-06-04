using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace TSUT.MappingSystem
{
    [ProtoContract]
    [ProtoInclude(10, typeof(PacketScanRequest))]
    [ProtoInclude(11, typeof(PacketChunkSync))]
    [ProtoInclude(12, typeof(PacketScanStarted))]
    [ProtoInclude(13, typeof(PacketExchangeRequest))]
    [ProtoInclude(14, typeof(PacketScanEnded))]
    [ProtoInclude(15, typeof(PacketScanApproved))]
    [ProtoInclude(16, typeof(PacketScanBatch))]
    [ProtoInclude(17, typeof(PacketNotification))]
    [ProtoInclude(18, typeof(PacketHolotableSync))]
    [ProtoInclude(19, typeof(PacketDisplaySync))]
    [ProtoInclude(20, typeof(PacketScanStopRequest))]
    [ProtoInclude(21, typeof(PacketContractSync))]
    [ProtoInclude(22, typeof(PacketContractEnded))]
    [ProtoInclude(23, typeof(PacketContractRequest))]
    public abstract class PacketBase
    {
        public abstract void Handle(ulong senderId);
    }

    // Server → requester only: start raycasting
    [ProtoContract]
    public class PacketScanApproved : PacketBase
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public VRageMath.Vector3D Position;
        [ProtoMember(3)] public float Radius;
        [ProtoMember(4)] public int TotalRays;
        [ProtoMember(5)] public int ScanId;

        public PacketScanApproved() { }
        public PacketScanApproved(long entityId, VRageMath.Vector3D pos, float radius, int totalRays, int scanId)
        {
            EntityId = entityId;
            Position = pos;
            Radius = radius;
            TotalRays = totalRays;
            ScanId = scanId;
        }

        public override void Handle(ulong senderId)
        {
            MapSession.Instance.Scheduler.StartClientScan(EntityId, Position, Radius, TotalRays);
        }
    }

    // Server → ALL: scan started, drives UI state + visual on all clients
    [ProtoContract]
    public class PacketScanStarted : PacketBase
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public VRageMath.Vector3D Position;
        [ProtoMember(3)] public float Radius;
        [ProtoMember(4)] public int TotalRays;
        [ProtoMember(5)] public int DurationTicks;
        [ProtoMember(6)] public int ScanId;

        public PacketScanStarted() { }
        public PacketScanStarted(long entityId, VRageMath.Vector3D pos, float radius, int totalRays, int durationTicks, int scanId)
        {
            EntityId = entityId;
            Position = pos;
            Radius = radius;
            TotalRays = totalRays;
            DurationTicks = durationTicks;
            ScanId = scanId;
        }

        public override void Handle(ulong senderId)
        {
            var entity = MyAPIGateway.Entities.GetEntityById(EntityId);
            var entry = entity?.GameLogic?.GetAs<ScannerEntry>();
            entry?.UpdateScanStarted(ScanId, TotalRays);

            MapSession.Instance.AddScanVisual(Position, Radius, DurationTicks, EntityId);
        }
    }

    // Server → ALL: scan ended (complete or cancelled), clears UI state + visual
    [ProtoContract]
    public class PacketScanEnded : PacketBase
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public int ScanId;

        public PacketScanEnded() { }
        public PacketScanEnded(long entityId, int scanId)
        {
            EntityId = entityId;
            ScanId = scanId;
        }

        public override void Handle(ulong senderId)
        {
            var entity = MyAPIGateway.Entities.GetEntityById(EntityId);
            var entry = entity?.GameLogic?.GetAs<ScannerEntry>();
            entry?.UpdateScanEnded(ScanId);

            MapSession.Instance.Scheduler?.CancelClientScan(EntityId);
            MapSession.Instance?.StopVisualForAntenna(EntityId);
        }
    }

    // Client → server: map data batch; server relays to others
    [ProtoContract]
    public class PacketScanBatch : PacketBase
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public List<CellResult> Results;
        [ProtoMember(3)] public int CurrentRayIndex;
        [ProtoMember(4)] public bool IsFinal;

        public PacketScanBatch() { }
        public PacketScanBatch(long entityId, List<CellResult> results, int currentRayIndex, bool isFinal)
        {
            EntityId = entityId;
            Results = results;
            CurrentRayIndex = currentRayIndex;
            IsFinal = isFinal;
        }

        public override void Handle(ulong senderId)
        {
            MapSession.Instance.Scheduler.ProcessScanBatch(EntityId, Results, CurrentRayIndex, IsFinal);

            if (MyAPIGateway.Session.IsServer)
                MapSession.Instance.Networking.SendToOthers(this, senderId);
        }
    }

    [ProtoContract]
    public struct CellResult
    {
        [ProtoMember(1)] public VRageMath.Vector3D Position;
        [ProtoMember(2)] public short Height;
        [ProtoMember(3)] public byte Flags;
    }

    [ProtoContract]
    public class PacketScanRequest : PacketBase
    {
        [ProtoMember(1)] public long EntityId;

        public PacketScanRequest() { }
        public PacketScanRequest(long entityId) { EntityId = entityId; }

        public override void Handle(ulong senderId)
        {
            if (!MyAPIGateway.Session.IsServer) return;

            var entity = MyAPIGateway.Entities.GetEntityById(EntityId);
            var antenna = entity as IMyRadioAntenna;
            var entry = entity?.GameLogic?.GetAs<ScannerEntry>();

            if (antenna != null && antenna.IsWorking && entry != null)
            {
                float radius = AntennaHelper.GetScanRadius(antenna);
                MapSession.Instance.Scheduler.EnqueueScan(entity, radius, senderId);
                MyLog.Default.WriteLine($"{Config.LogPrefix} Received ScanRequest from {senderId} for {EntityId}");
            }
        }
    }

    [ProtoContract]
    public class PacketScanStopRequest : PacketBase
    {
        [ProtoMember(1)] public long EntityId;

        public PacketScanStopRequest() { }
        public PacketScanStopRequest(long entityId) { EntityId = entityId; }

        public override void Handle(ulong senderId)
        {
            if (!MyAPIGateway.Session.IsServer) return;

            if (MapSession.Instance.Scheduler?.GetActiveScan(EntityId) != null)
            {
                MapSession.Instance.Scheduler.CancelScanForAntenna(EntityId);
                MapSession.Instance.Networking.SendToPlayer(new PacketNotification("Scan stopped.", 2000), senderId);
            }
        }
    }

    // Server → contracting player: contract zone info for LCD/terminal display
    [ProtoContract]
    public class PacketContractSync : PacketBase
    {
        [ProtoMember(1)] public long ContractId;
        [ProtoMember(2)] public VRageMath.Vector3D Center;
        [ProtoMember(3)] public float Radius;
        [ProtoMember(4)] public long AcceptedTicks;
        [ProtoMember(5)] public VRageMath.Vector3D StationCenter;
        [ProtoMember(6)] public bool CoverageMet;
        [ProtoMember(7)] public string ContractName;

        public PacketContractSync() { }
        public PacketContractSync(long contractId, VRageMath.Vector3D center, float radius, long acceptedTicks, VRageMath.Vector3D stationCenter, bool coverageMet, string contractName)
        {
            ContractId = contractId; Center = center; Radius = radius;
            AcceptedTicks = acceptedTicks; StationCenter = stationCenter;
            CoverageMet = coverageMet; ContractName = contractName;
        }

        public override void Handle(ulong senderId)
        {
            var existing = MapSession.Instance.ClientContracts.Find(c => c.ContractId == ContractId);
            if (existing != null)
            {
                existing.Center = Center; existing.Radius = Radius;
                existing.AcceptedTicks = AcceptedTicks; existing.StationCenter = StationCenter;
                existing.CoverageMet = CoverageMet; existing.ContractName = ContractName;
            }
            else
            {
                MapSession.Instance.ClientContracts.Add(new ClientContractInfo
                {
                    ContractId = ContractId, Center = Center, Radius = Radius,
                    AcceptedTicks = AcceptedTicks, StationCenter = StationCenter,
                    CoverageMet = CoverageMet, ContractName = ContractName
                });
            }
        }
    }

    // Server → contracting player: contract ended (complete/fail/abandon)
    [ProtoContract]
    public class PacketContractEnded : PacketBase
    {
        [ProtoMember(1)] public long ContractId;

        public PacketContractEnded() { }
        public PacketContractEnded(long contractId) { ContractId = contractId; }

        public override void Handle(ulong senderId)
        {
            MapSession.Instance.ClientContracts.RemoveAll(c => c.ContractId == ContractId);
        }
    }

    // Client → server: request active contracts on join
    [ProtoContract]
    public class PacketContractRequest : PacketBase
    {
        public PacketContractRequest() { }

        public override void Handle(ulong senderId)
        {
            if (!MyAPIGateway.Session.IsServer) return;
            MapSession.Instance.Contracts?.SendContractsToPlayer(senderId);
        }
    }

    [ProtoContract]
    public class PacketExchangeRequest : PacketBase
    {
        [ProtoMember(1)] public long SourceEntityId;
        [ProtoMember(2)] public long TargetEntityId;

        public PacketExchangeRequest() { }
        public PacketExchangeRequest(long source, long target)
        {
            SourceEntityId = source;
            TargetEntityId = target;
        }

        public override void Handle(ulong senderId)
        {
            if (!MyAPIGateway.Session.IsServer) return;

            MyLog.Default.WriteLine($"{Config.LogPrefix} [Exchange] Request from player {senderId}: source={SourceEntityId} target={TargetEntityId}");

            var sourceEntity = MyAPIGateway.Entities.GetEntityById(SourceEntityId);
            var targetEntity = MyAPIGateway.Entities.GetEntityById(TargetEntityId);

            MyLog.Default.WriteLine($"{Config.LogPrefix} [Exchange] sourceEntity={(sourceEntity != null ? sourceEntity.DisplayName : "NULL")} targetEntity={(targetEntity != null ? targetEntity.DisplayName : "NULL")}");

            var sourceStorage = sourceEntity?.Components.Get<MapStorageComponent>();
            var targetStorage = targetEntity?.Components.Get<MapStorageComponent>();

            MyLog.Default.WriteLine($"{Config.LogPrefix} [Exchange] source storage={(sourceStorage != null ? $"chunks={sourceStorage.Grid.Chunks.Count} cells={sourceStorage.Grid.CellCount}" : "NULL")} target storage={(targetStorage != null ? $"chunks={targetStorage.Grid.Chunks.Count} cells={targetStorage.Grid.CellCount}" : "NULL")}");

            if (sourceStorage != null && targetStorage != null)
            {
                int cellsBefore = targetStorage.Grid.CellCount;
                targetStorage.MergeData(sourceStorage.Grid);
                int cellsAfter = targetStorage.Grid.CellCount;
                MyLog.Default.WriteLine($"{Config.LogPrefix} [Exchange] Merged: target cells {cellsBefore} -> {cellsAfter}, chunks={targetStorage.Grid.Chunks.Count}");
                MapSession.Instance.Networking.SendToAll(new PacketChunkSync(TargetEntityId, targetStorage.Grid.GetSerializedChunks()));
                MapSession.Instance.Networking.SendToPlayer(new PacketNotification("Data exchange complete.", 3000), senderId);
                MyLog.Default.WriteLine($"{Config.LogPrefix} [Exchange] Sync sent to all, notification sent to {senderId}");
            }
            else
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} [Exchange] FAILED: sourceStorage={sourceStorage != null} targetStorage={targetStorage != null}");
            }
        }
    }

    [ProtoContract]
    public class PacketChunkSync : PacketBase
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public List<ChunkEntry> Chunks;

        public PacketChunkSync() { }
        public PacketChunkSync(long entityId, List<ChunkEntry> chunks)
        {
            EntityId = entityId;
            Chunks = chunks;
        }

        public override void Handle(ulong senderId)
        {
            var entity = MyAPIGateway.Entities.GetEntityById(EntityId);
            var storage = entity?.Components.Get<MapStorageComponent>();
            if (storage != null)
                storage.ReceiveChunkSync(Chunks);
        }
    }

    [ProtoContract]
    public class PacketNotification : PacketBase
    {
        [ProtoMember(1)] public string Message;
        [ProtoMember(2)] public int DurationMs;

        public PacketNotification() { }
        public PacketNotification(string message, int durationMs = 5000)
        {
            Message = message;
            DurationMs = durationMs;
        }

        public override void Handle(ulong senderId)
        {
            MyAPIGateway.Utilities.ShowNotification(Message, DurationMs);
        }
    }

    [ProtoContract]
    public class PacketHolotableSync : PacketBase
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public string Data;

        public PacketHolotableSync() { }
        public PacketHolotableSync(long entityId, string data) { EntityId = entityId; Data = data; }

        public override void Handle(ulong senderId)
        {
            var entity = MyAPIGateway.Entities.GetEntityById(EntityId);
            var entry = entity?.GameLogic?.GetAs<HolotableEntry>();
            if (entry != null)
            {
                entry.LoadFromString(Data);
                entry.MarkDirty();
            }
            if (MyAPIGateway.Session.IsServer)
                MapSession.Instance.Networking.SendToOthers(this, senderId);
        }
    }

    [ProtoContract]
    public class PacketDisplaySync : PacketBase
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public string Data;

        public PacketDisplaySync() { }
        public PacketDisplaySync(long entityId, string data) { EntityId = entityId; Data = data; }

        public override void Handle(ulong senderId)
        {
            var entity = MyAPIGateway.Entities.GetEntityById(EntityId);
            var entry = entity?.GameLogic?.GetAs<DisplayFlatEntry>();
            if (entry != null)
            {
                entry.LoadFromString(Data);
                entry.Save();
                entry.UpdateVisuals();
            }
            if (MyAPIGateway.Session.IsServer)
                MapSession.Instance.Networking.SendToOthers(this, senderId);
        }
    }

    [ProtoContract]
    public class PacketWrapper
    {
        [ProtoMember(1)] public ulong SenderId;
        [ProtoMember(2)] public PacketBase Packet;
    }

    public class Networking
    {
        private readonly ushort _id;

        public Networking(ushort id)
        {
            _id = id;
        }

        public void Register()
        {
            MyAPIGateway.Multiplayer.RegisterMessageHandler(_id, OnMessageReceived);
        }

        public void Unregister()
        {
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(_id, OnMessageReceived);
        }

        private void OnMessageReceived(byte[] data)
        {
            try
            {
                var wrapper = MyAPIGateway.Utilities.SerializeFromBinary<PacketWrapper>(data);
                if (wrapper != null && wrapper.Packet != null)
                    wrapper.Packet.Handle(wrapper.SenderId);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"{Config.LogPrefix} Network error in OnMessageReceived: {ex.Message}");
            }
        }

        public void SendToServer(PacketBase packet)
        {
            var wrapper = new PacketWrapper { SenderId = MyAPIGateway.Multiplayer.MyId, Packet = packet };
            var data = MyAPIGateway.Utilities.SerializeToBinary(wrapper);
            MyAPIGateway.Multiplayer.SendMessageToServer(_id, data);
        }

        public void SendToAll(PacketBase packet)
        {
            var wrapper = new PacketWrapper { SenderId = MyAPIGateway.Multiplayer.MyId, Packet = packet };
            var data = MyAPIGateway.Utilities.SerializeToBinary(wrapper);
            MyAPIGateway.Multiplayer.SendMessageToOthers(_id, data);

            // Local delivery if server
            if (MyAPIGateway.Session.IsServer)
                packet.Handle(MyAPIGateway.Multiplayer.ServerId);
        }

        public void SendToPlayer(PacketBase packet, ulong playerId)
        {
            if (playerId == MyAPIGateway.Multiplayer.MyId)
            {
                packet.Handle(playerId);
                return;
            }

            var wrapper = new PacketWrapper { SenderId = MyAPIGateway.Multiplayer.MyId, Packet = packet };
            var data = MyAPIGateway.Utilities.SerializeToBinary(wrapper);
            MyAPIGateway.Multiplayer.SendMessageTo(_id, data, playerId);
        }

        public void SendToOthers(PacketBase packet, ulong excludePlayerId)
        {
            var wrapper = new PacketWrapper { SenderId = MyAPIGateway.Multiplayer.MyId, Packet = packet };
            var data = MyAPIGateway.Utilities.SerializeToBinary(wrapper);

            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            foreach (var player in players)
            {
                if (player.SteamUserId != excludePlayerId && player.SteamUserId != MyAPIGateway.Multiplayer.MyId)
                    MyAPIGateway.Multiplayer.SendMessageTo(_id, data, player.SteamUserId);
            }
        }
    }
}
