namespace VoxPopuli.Game;

using System.Numerics;
using System.Runtime.InteropServices;

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

    public Vector3[] ChunkAabbMin = new Vector3[MAX_CHUNKS];
    public Vector3[] ChunkAabbMax = new Vector3[MAX_CHUNKS];

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

    public static int ChunkIndex(int cx, int cy, int cz)
        => cx + cy * CHUNKS_X + cz * CHUNKS_X * CHUNKS_Y;

    public static int ChunkIndexToCoords(int chunkIndex, out int cy, out int cz)
    {
        int cx = chunkIndex % CHUNKS_X;
        cy = (chunkIndex / CHUNKS_X) % CHUNKS_Y;
        cz = chunkIndex / (CHUNKS_X * CHUNKS_Y);
        return cx;
    }

    public static Vector3 ChunkOrigin(int chunkIndex)
    {
        int cx = ChunkIndexToCoords(chunkIndex, out int cy, out int cz);
        return new Vector3(cx * CHUNK_SIZE, cy * CHUNK_SIZE, cz * CHUNK_SIZE);
    }

    public byte GetVoxel(int x, int y, int z)
    {
        if ((uint)x >= WorldSizeX || (uint)y >= WorldSizeY || (uint)z >= WorldSizeZ)
        {
            return 0;
        }
        int cx = x / CHUNK_SIZE, cy = y / CHUNK_SIZE, cz = z / CHUNK_SIZE;
        int ci = ChunkIndex(cx, cy, cz);
        return _voxels[ci * Chunk.VOLUME + Chunk.Index(x % CHUNK_SIZE, y % CHUNK_SIZE, z % CHUNK_SIZE)];
    }

    public void SetVoxel(int x, int y, int z, byte typeId)
    {
        if ((uint)x >= WorldSizeX || (uint)y >= WorldSizeY || (uint)z >= WorldSizeZ)
        {
            return;
        }
        int cx = x / CHUNK_SIZE, cy = y / CHUNK_SIZE, cz = z / CHUNK_SIZE;
        int ci = ChunkIndex(cx, cy, cz);
        _voxels[ci * Chunk.VOLUME + Chunk.Index(x % CHUNK_SIZE, y % CHUNK_SIZE, z % CHUNK_SIZE)] = typeId;
        _dirty[ci] = true;
    }

    public void SetBlock(int x, int y, int z, int sizeX, int sizeY, int sizeZ, byte typeId)
    {
        int x1 = Math.Clamp(x, 0, WorldSizeX - 1);
        int y1 = Math.Clamp(y, 0, WorldSizeY - 1);
        int z1 = Math.Clamp(z, 0, WorldSizeZ - 1);
        int x2 = Math.Clamp(x + sizeX - 1, 0, WorldSizeX - 1);
        int y2 = Math.Clamp(y + sizeY - 1, 0, WorldSizeY - 1);
        int z2 = Math.Clamp(z + sizeZ - 1, 0, WorldSizeZ - 1);

        for (int vx = x1; vx <= x2; vx++)
        {
            for (int vy = y1; vy <= y2; vy++)
            {
                for (int vz = z1; vz <= z2; vz++)
                {
                    int cx = vx / CHUNK_SIZE, cy = vy / CHUNK_SIZE, cz = vz / CHUNK_SIZE;
                    int ci = ChunkIndex(cx, cy, cz);
                    _voxels[ci * Chunk.VOLUME + Chunk.Index(vx % CHUNK_SIZE, vy % CHUNK_SIZE, vz % CHUNK_SIZE)] = typeId;
                    _dirty[ci] = true;
                }
            }
        }
    }

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
