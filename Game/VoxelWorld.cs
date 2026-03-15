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

    private void MarkDirty(int ci)
    {
        _dirty[ci] = true;
        int cx = ChunkIndexToCoords(ci, out int cy, out int cz);
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            int nx = cx + dx, ny = cy + dy, nz = cz + dz;
            if ((uint)nx < CHUNKS_X && (uint)ny < CHUNKS_Y && (uint)nz < CHUNKS_Z)
                _dirty[ChunkIndex(nx, ny, nz)] = true;
        }
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
        MarkDirty(ci);
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
                    MarkDirty(ci);
                }
            }
        }
    }

    // Returns the index of the first non-empty chunk hit by the ray, or -1 if none.
    public int RaycastChunk(Vector3 origin, Vector3 dir)
    {
        Span<(float t, int i)> hits = stackalloc (float, int)[MAX_CHUNKS];
        int hitCount = 0;

        for (int i = 0; i < MAX_CHUNKS; i++)
        {
            Vector3 invDir = new Vector3(1f / dir.X, 1f / dir.Y, 1f / dir.Z);
            Vector3 t0 = (ChunkAabbMin[i] - origin) * invDir;
            Vector3 t1 = (ChunkAabbMax[i] - origin) * invDir;
            float tMin = MathF.Max(MathF.Max(MathF.Min(t0.X, t1.X), MathF.Min(t0.Y, t1.Y)), MathF.Min(t0.Z, t1.Z));
            float tMax = MathF.Min(MathF.Min(MathF.Max(t0.X, t1.X), MathF.Max(t0.Y, t1.Y)), MathF.Max(t0.Z, t1.Z));
            if (tMax >= 0 && tMin <= tMax)
                hits[hitCount++] = (tMin, i);
        }

        // Sort hits by distance (insertion sort — hitCount is small in practice)
        for (int i = 1; i < hitCount; i++)
        {
            var cur = hits[i];
            int j = i - 1;
            while (j >= 0 && hits[j].t > cur.t) { hits[j + 1] = hits[j]; j--; }
            hits[j + 1] = cur;
        }

        for (int h = 0; h < hitCount; h++)
        {
            int ci = hits[h].i;
            var data = _voxels.AsSpan(ci * Chunk.VOLUME, Chunk.VOLUME);
            foreach (byte b in data) if (b != 0) return ci;
        }
        return -1;
    }

    public int GetClosestChunkIndex(Vector3 pos)
    {
        int cx = Math.Clamp((int)(pos.X / CHUNK_SIZE), 0, CHUNKS_X - 1);
        int cy = Math.Clamp((int)(pos.Y / CHUNK_SIZE), 0, CHUNKS_Y - 1);
        int cz = Math.Clamp((int)(pos.Z / CHUNK_SIZE), 0, CHUNKS_Z - 1);
        return ChunkIndex(cx, cy, cz);
    }

    public void DeleteChunk(int chunkIndex)
    {
        Array.Clear(_voxels, chunkIndex * Chunk.VOLUME, Chunk.VOLUME);
        MarkDirty(chunkIndex);
    }

    public void MarkAllChunksDirty()
    {
        for (int i = 0; i < MAX_CHUNKS; i++) _dirty[i] = true;
    }

    public void DeleteClosestChunk(Vector3 eye)
    {
        int closest = 0;
        float minDist = float.MaxValue;
        for (int i = 0; i < MAX_CHUNKS; i++)
        {
            float d = Vector3.DistanceSquared(eye, (ChunkAabbMin[i] + ChunkAabbMax[i]) * 0.5f);
            if (d < minDist) { minDist = d; closest = i; }
        }
        Array.Clear(_voxels, closest * Chunk.VOLUME, Chunk.VOLUME);
        MarkDirty(closest);
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
