namespace VoxPopuli.Game;

using System.Numerics;
using System.Runtime.InteropServices;

/// <summary>
/// Represents the entire voxel world as a flat array of chunks.
/// The world is divided into a fixed grid of chunks along each axis.
/// </summary>
public sealed class VoxelWorld
{
    public const int WorldSizeX = 768, WorldSizeY = 64, WorldSizeZ = 768; // 24 * 32

    private const int CHUNK_SIZE = Chunk.SIZE;
    private const int CHUNKS_X = 24, CHUNKS_Y = 2, CHUNKS_Z = 24;
    public const int MAX_CHUNKS = 1152; // 24 * 2 * 24

    // Single flat allocation — chunk i occupies [i*VOLUME .. (i+1)*VOLUME)
    internal readonly byte[] _voxels = new byte[MAX_CHUNKS * Chunk.VOLUME];

    private readonly bool[] _dirty = new bool[MAX_CHUNKS];
    private readonly List<int> _dirtyList = new(MAX_CHUNKS);

    /// <summary>Minimum corner (in world space) of each chunk's axis-aligned bounding box.</summary>
    public Vector3[] ChunkAabbMin = new Vector3[MAX_CHUNKS];

    /// <summary>Maximum corner (in world space) of each chunk's axis-aligned bounding box.</summary>
    public Vector3[] ChunkAabbMax = new Vector3[MAX_CHUNKS];

    /// <summary>Initializes the world and precomputes AABB bounds for every chunk.</summary>
    public VoxelWorld()
    {
        for (int i = 0; i < MAX_CHUNKS; i++)
        {
            var origin = ChunkOrigin(i);
            ChunkAabbMin[i] = origin;
            ChunkAabbMax[i] = origin + new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE);
        }
    }

    /// <summary>Returns a zero-allocation view over chunk i's voxel slice.</summary>
    public Chunk GetChunk(int chunkIndex) =>
        _voxels.AsSpan(chunkIndex * Chunk.VOLUME, Chunk.VOLUME);

    /// <summary>Converts chunk grid coordinates to a flat chunk index.</summary>
    public static int ChunkIndex(int cx, int cy, int cz)
        => cx + cy * CHUNKS_X + cz * CHUNKS_X * CHUNKS_Y;

    /// <summary>
    /// Converts a flat chunk index back to chunk grid coordinates.
    /// Returns the X coordinate and sets <paramref name="cy"/> and <paramref name="cz"/> via out parameters.
    /// </summary>
    public static int ChunkIndexToCoords(int chunkIndex, out int cy, out int cz)
    {
        int cx = chunkIndex % CHUNKS_X;
        cy = (chunkIndex / CHUNKS_X) % CHUNKS_Y;
        cz = chunkIndex / (CHUNKS_X * CHUNKS_Y);
        return cx;
    }

    /// <summary>Returns the world-space origin (minimum corner) of the chunk at the given flat index.</summary>
    public static Vector3 ChunkOrigin(int chunkIndex)
    {
        int cx = ChunkIndexToCoords(chunkIndex, out int cy, out int cz);
        return new Vector3(cx * CHUNK_SIZE, cy * CHUNK_SIZE, cz * CHUNK_SIZE);
    }

    /// <summary>Returns the voxel type at the given world-space coordinates, or 0 if out of bounds.</summary>
    public byte GetVoxel(int x, int y, int z)
    {
        if ((uint)x >= WorldSizeX || (uint)y >= WorldSizeY || (uint)z >= WorldSizeZ)
        {
            return 0;
        }
        int cx = x / CHUNK_SIZE, cy = y / CHUNK_SIZE, cz = z / CHUNK_SIZE;
        int chunkIndex = ChunkIndex(cx, cy, cz);
        return _voxels[chunkIndex * Chunk.VOLUME + Chunk.Index(x % CHUNK_SIZE, y % CHUNK_SIZE, z % CHUNK_SIZE)];
    }

    /// <summary>
    /// Marks the given chunk and all its 26 face/edge/corner neighbours as dirty
    /// so they will be re-meshed on the next drain.
    /// </summary>
    private void MarkDirty(int chunkIndex)
    {
        _dirty[chunkIndex] = true;
        int cx = ChunkIndexToCoords(chunkIndex, out int cy, out int cz);

        // Iterate over all 26 neighbouring chunk offsets (excluding the chunk itself)
        for (int neighborOffsetX = -1; neighborOffsetX <= 1; neighborOffsetX++)
            for (int neighborOffsetY = -1; neighborOffsetY <= 1; neighborOffsetY++)
                for (int neighborOffsetZ = -1; neighborOffsetZ <= 1; neighborOffsetZ++)
                {
                    if (neighborOffsetX == 0 && neighborOffsetY == 0 && neighborOffsetZ == 0) continue;
                    int neighborChunkX = cx + neighborOffsetX;
                    int neighborChunkY = cy + neighborOffsetY;
                    int neighborChunkZ = cz + neighborOffsetZ;
                    if ((uint)neighborChunkX < CHUNKS_X && (uint)neighborChunkY < CHUNKS_Y && (uint)neighborChunkZ < CHUNKS_Z)
                        _dirty[ChunkIndex(neighborChunkX, neighborChunkY, neighborChunkZ)] = true;
                }
    }

    /// <summary>Sets the voxel type at the given world-space coordinates and marks the affected chunk dirty.</summary>
    public void SetVoxel(int x, int y, int z, byte typeId)
    {
        if ((uint)x >= WorldSizeX || (uint)y >= WorldSizeY || (uint)z >= WorldSizeZ)
        {
            return;
        }
        int cx = x / CHUNK_SIZE, cy = y / CHUNK_SIZE, cz = z / CHUNK_SIZE;
        int chunkIndex = ChunkIndex(cx, cy, cz);
        _voxels[chunkIndex * Chunk.VOLUME + Chunk.Index(x % CHUNK_SIZE, y % CHUNK_SIZE, z % CHUNK_SIZE)] = typeId;
        MarkDirty(chunkIndex);
    }

    /// <summary>
    /// Fills an axis-aligned rectangular region of voxels with the given type.
    /// The region is clamped to world bounds.
    /// </summary>
    public void SetBlock(int x, int y, int z, int sizeX, int sizeY, int sizeZ, byte typeId)
    {
        // Clamp the region to valid world coordinates
        int minX = Math.Clamp(x, 0, WorldSizeX - 1);
        int minY = Math.Clamp(y, 0, WorldSizeY - 1);
        int minZ = Math.Clamp(z, 0, WorldSizeZ - 1);
        int maxX = Math.Clamp(x + sizeX - 1, 0, WorldSizeX - 1);
        int maxY = Math.Clamp(y + sizeY - 1, 0, WorldSizeY - 1);
        int maxZ = Math.Clamp(z + sizeZ - 1, 0, WorldSizeZ - 1);

        for (int voxelX = minX; voxelX <= maxX; voxelX++)
        {
            for (int voxelY = minY; voxelY <= maxY; voxelY++)
            {
                for (int voxelZ = minZ; voxelZ <= maxZ; voxelZ++)
                {
                    int cx = voxelX / CHUNK_SIZE, cy = voxelY / CHUNK_SIZE, cz = voxelZ / CHUNK_SIZE;
                    int chunkIndex = ChunkIndex(cx, cy, cz);
                    _voxels[chunkIndex * Chunk.VOLUME + Chunk.Index(voxelX % CHUNK_SIZE, voxelY % CHUNK_SIZE, voxelZ % CHUNK_SIZE)] = typeId;
                    MarkDirty(chunkIndex);
                }
            }
        }
    }

    /// <summary>Returns the index of the chunk that contains (or is closest to) the given world-space position.</summary>
    public int GetClosestChunkIndex(Vector3 pos)
    {
        int cx = Math.Clamp((int)(pos.X / CHUNK_SIZE), 0, CHUNKS_X - 1);
        int cy = Math.Clamp((int)(pos.Y / CHUNK_SIZE), 0, CHUNKS_Y - 1);
        int cz = Math.Clamp((int)(pos.Z / CHUNK_SIZE), 0, CHUNKS_Z - 1);
        return ChunkIndex(cx, cy, cz);
    }

    /// <summary>Clears all voxels in the specified chunk and marks it dirty.</summary>
    public void DeleteChunk(int chunkIndex)
    {
        Array.Clear(_voxels, chunkIndex * Chunk.VOLUME, Chunk.VOLUME);
        MarkDirty(chunkIndex);
    }

    /// <summary>Marks every chunk in the world as dirty, forcing a full re-mesh on the next drain.</summary>
    public void MarkAllChunksDirty()
    {
        for (int i = 0; i < MAX_CHUNKS; i++) _dirty[i] = true;
    }

    /// <summary>
    /// Collects all dirty chunk indices into a span, clears the dirty flags, and returns the span.
    /// The returned span is valid only until the next call to this method.
    /// </summary>
    public ReadOnlySpan<int> DrainDirtyChunks()
    {
        _dirtyList.Clear();
        for (int i = 0; i < MAX_CHUNKS; i++)
        {
            if (_dirty[i])
            {
                _dirtyList.Add(i);
            }
        }
        for (int i = 0; i < MAX_CHUNKS; i++)
        {
            _dirty[i] = false;
        }
        return CollectionsMarshal.AsSpan(_dirtyList);
    }
}
