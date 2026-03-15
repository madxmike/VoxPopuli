namespace VoxPopuli.Game;

using System.Numerics;
using System.Runtime.InteropServices;

public sealed class VoxelWorld
{
    public const int WorldSizeX = 512, WorldSizeY = 64, WorldSizeZ = 512;

    private const int CHUNK_SIZE = Chunk.SIZE;
    private const int CHUNKS_X = 16, CHUNKS_Y = 2, CHUNKS_Z = 16;
    public const int MAX_CHUNKS = 512; // 16 * 2 * 16

    public readonly Chunk[] Chunks;

    private readonly bool[] _dirty = new bool[MAX_CHUNKS];

    public Vector3[] ChunkAabbMin = new Vector3[MAX_CHUNKS];
    public Vector3[] ChunkAabbMax = new Vector3[MAX_CHUNKS];

    private readonly List<int> _dirtyList = new(MAX_CHUNKS);

    public VoxelWorld()
    {
        Chunks = new Chunk[MAX_CHUNKS];
        for (int i = 0; i < MAX_CHUNKS; i++)
        {
            Chunks[i] = new Chunk();
            var origin = ChunkOrigin(i);
            ChunkAabbMin[i] = origin;
            ChunkAabbMax[i] = origin + new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE);
        }
    }

    public static int ChunkIndex(int cx, int cy, int cz)
        => cx + cy * CHUNKS_X + cz * CHUNKS_X * CHUNKS_Y;

    public static Vector3 ChunkOrigin(int chunkIndex)
    {
        int cx = chunkIndex % CHUNKS_X;
        int cy = (chunkIndex / CHUNKS_X) % CHUNKS_Y;
        int cz = chunkIndex / (CHUNKS_X * CHUNKS_Y);
        return new Vector3(cx * CHUNK_SIZE, cy * CHUNK_SIZE, cz * CHUNK_SIZE);
    }

    public byte GetVoxel(int x, int y, int z)
    {
        if ((uint)x >= WorldSizeX || (uint)y >= WorldSizeY || (uint)z >= WorldSizeZ)
            return 0;

        int cx = x / CHUNK_SIZE, cy = y / CHUNK_SIZE, cz = z / CHUNK_SIZE;
        return Chunks[ChunkIndex(cx, cy, cz)].Get(x % CHUNK_SIZE, y % CHUNK_SIZE, z % CHUNK_SIZE);
    }

    public void SetVoxel(int x, int y, int z, byte typeId)
    {
        if ((uint)x >= WorldSizeX || (uint)y >= WorldSizeY || (uint)z >= WorldSizeZ)
            return;

        int cx = x / CHUNK_SIZE, cy = y / CHUNK_SIZE, cz = z / CHUNK_SIZE;
        int ci = ChunkIndex(cx, cy, cz);
        Chunks[ci].Set(x % CHUNK_SIZE, y % CHUNK_SIZE, z % CHUNK_SIZE, typeId);
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
        for (int vy = y1; vy <= y2; vy++)
        for (int vz = z1; vz <= z2; vz++)
        {
            int cx = vx / CHUNK_SIZE, cy = vy / CHUNK_SIZE, cz = vz / CHUNK_SIZE;
            int ci = ChunkIndex(cx, cy, cz);
            Chunks[ci].Set(vx % CHUNK_SIZE, vy % CHUNK_SIZE, vz % CHUNK_SIZE, typeId);
            _dirty[ci] = true;
        }
    }

    public ReadOnlySpan<int> DrainDirtyChunks()
    {
        _dirtyList.Clear();
        for (int i = 0; i < MAX_CHUNKS; i++)
        {
            if (_dirty[i]) _dirtyList.Add(i);
        }
        for (int i = 0; i < MAX_CHUNKS; i++)
            _dirty[i] = false;

        return CollectionsMarshal.AsSpan(_dirtyList);
    }
}
