using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using ProtoBuf;
using VRageMath;

namespace TSUT.MappingSystem
{
    [ProtoContract]
    public struct MapCell
    {
        [ProtoMember(1)] public short Height;
        [ProtoMember(2)] public byte Flags; // 0 = Unknown, 1 = Planet, 2 = Asteroid
    }

    [ProtoContract]
    public struct CellEntry
    {
        [ProtoMember(1)] public Vector2I Pos;
        [ProtoMember(2)] public MapCell Cell;
    }

    [ProtoContract]
    public class MapChunk
    {
        [ProtoMember(1)] public Vector2I ChunkPos;
        
        // ProtoBuf doesn't like Dictionary with non-primitive keys like Vector2I
        [ProtoMember(2)] public List<CellEntry> SerializedCells;

        [ProtoMember(3)] public long LastUsedTicks;

        [XmlIgnore]
        public Dictionary<Vector2I, MapCell> Cells = new Dictionary<Vector2I, MapCell>();

        public MapChunk() { }

        public MapChunk(Vector2I pos)
        {
            ChunkPos = pos;
            Touch();
        }

        public void Touch()
        {
            LastUsedTicks = DateTime.UtcNow.Ticks;
        }

        public void BeforeSerialize()
        {
            SerializedCells = new List<CellEntry>();
            foreach (var kvp in Cells)
            {
                SerializedCells.Add(new CellEntry { Pos = kvp.Key, Cell = kvp.Value });
            }
        }

        public void AfterDeserialize()
        {
            Cells.Clear();
            if (SerializedCells != null)
            {
                foreach (var entry in SerializedCells)
                {
                    Cells[entry.Pos] = entry.Cell;
                }
            }
        }
    }

    [ProtoContract]
    public struct ChunkEntry
    {
        [ProtoMember(1)] public Vector2I Pos;
        [ProtoMember(2)] public MapChunk Chunk;
    }

    [ProtoContract]
    public class MapGrid
    {
        public const int ChunkSize = 16;
        
        [XmlIgnore]
        public Dictionary<Vector2I, MapChunk> Chunks = new Dictionary<Vector2I, MapChunk>();

        [ProtoMember(1)]
        public List<ChunkEntry> SerializedChunks;

        [ProtoMember(2)]
        public long TotalHeight = 0;

        [ProtoMember(3)]
        public int CellCount = 0;

        public float AverageHeight => CellCount > 0 ? (float)TotalHeight / CellCount : 0f;

        public int ChunksSize => Chunks.Count;
        private readonly object _lock = new object();

        public event Action DataChanged;

        public void BeforeSerialize()
        {
            lock (_lock)
            {
                SerializedChunks = new List<ChunkEntry>();
                foreach (var kvp in Chunks)
                {
                    kvp.Value.BeforeSerialize();
                    SerializedChunks.Add(new ChunkEntry { Pos = kvp.Key, Chunk = kvp.Value });
                }
            }
        }

        public void AfterDeserialize()
        {
            lock (_lock)
            {
                Chunks.Clear();
                if (SerializedChunks != null)
                {
                    foreach (var entry in SerializedChunks)
                    {
                        entry.Chunk.AfterDeserialize();
                        Chunks[entry.Pos] = entry.Chunk;
                    }
                }
            }
        }

        public void AddCell(Vector3D worldPos, MapCell cellData, int cellSize)
        {
            Vector2I cellPos = ProjectionHelper.WorldToGrid(worldPos, cellSize);

            Vector2I chunkPos = new Vector2I(
                (int)Math.Floor((float)cellPos.X / ChunkSize),
                (int)Math.Floor((float)cellPos.Y / ChunkSize)
            );

            lock (_lock)
            {
                MapChunk chunk;
                if (!Chunks.TryGetValue(chunkPos, out chunk))
                {
                    chunk = new MapChunk(chunkPos);
                    Chunks[chunkPos] = chunk;
                }
                else
                {
                    chunk.Touch();
                }

                MapCell existing;
                if (chunk.Cells.TryGetValue(cellPos, out existing))
                {
                    TotalHeight -= existing.Height;
                }
                else
                {
                    CellCount++;
                }

                chunk.Cells[cellPos] = cellData;
                TotalHeight += cellData.Height;
            }

            DataChanged?.Invoke();
        }

        public MapCell? GetCell(Vector2I cellPos)
        {
            Vector2I chunkPos = new Vector2I(
                (int)Math.Floor((float)cellPos.X / ChunkSize),
                (int)Math.Floor((float)cellPos.Y / ChunkSize)
            );

            lock (_lock)
            {
                MapChunk chunk;
                if (Chunks.TryGetValue(chunkPos, out chunk))
                {
                    chunk.Touch();
                    MapCell cell;
                    if (chunk.Cells.TryGetValue(cellPos, out cell))
                        return cell;
                }
            }
            return null;
        }

        public void PruneStorage(int maxChunks)
        {
            if (maxChunks <= 0) return;

            lock (_lock)
            {
                if (Chunks.Count <= maxChunks) return;

                var sortedChunks = new List<MapChunk>(Chunks.Values);
                sortedChunks.Sort((a, b) => a.LastUsedTicks.CompareTo(b.LastUsedTicks));

                int toRemove = Chunks.Count - maxChunks;
                for (int i = 0; i < toRemove; i++)
                {
                    var chunk = sortedChunks[i];
                    
                    // Update stats
                    foreach (var cell in chunk.Cells.Values)
                    {
                        TotalHeight -= cell.Height;
                        CellCount--;
                    }

                    Chunks.Remove(chunk.ChunkPos);
                }
            }

            DataChanged?.Invoke();
        }

        public List<ChunkEntry> GetSerializedChunks()
        {
            lock (_lock)
            {
                var list = new List<ChunkEntry>();
                foreach (var kvp in Chunks)
                {
                    kvp.Value.BeforeSerialize();
                    list.Add(new ChunkEntry { Pos = kvp.Key, Chunk = kvp.Value });
                }
                return list;
            }
        }

        public void SetSerializedChunks(List<ChunkEntry> list)
        {
            lock (_lock)
            {
                Chunks.Clear();
                CellCount = 0;
                TotalHeight = 0;
                if (list == null) return;
                foreach (var entry in list)
                {
                    entry.Chunk.AfterDeserialize();
                    Chunks[entry.Pos] = entry.Chunk;
                    CellCount += entry.Chunk.Cells.Count;
                    foreach (var cell in entry.Chunk.Cells.Values)
                        TotalHeight += cell.Height;
                }
            }

            DataChanged?.Invoke();
        }

        public void Merge(MapGrid other)
        {
            if (other == null) return;
            lock (_lock)
            {
                foreach (var otherChunkKvp in other.Chunks)
                {
                    MapChunk myChunk;
                    if (!Chunks.TryGetValue(otherChunkKvp.Key, out myChunk))
                    {
                        myChunk = new MapChunk(otherChunkKvp.Key);
                        Chunks[otherChunkKvp.Key] = myChunk;
                    }

                    foreach (var otherCellKvp in otherChunkKvp.Value.Cells)
                    {
                        MapCell existing;
                        if (myChunk.Cells.TryGetValue(otherCellKvp.Key, out existing))
                        {
                            TotalHeight -= existing.Height;
                        }
                        else
                        {
                            CellCount++;
                        }
                        myChunk.Cells[otherCellKvp.Key] = otherCellKvp.Value;
                        TotalHeight += otherCellKvp.Value.Height;
                    }
                }
            }
            DataChanged?.Invoke();
        }
    }
}
