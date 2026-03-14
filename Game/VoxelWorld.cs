namespace VoxPopuli.Game;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

public sealed class VoxelWorld
{
    public const int WorldSizeX = 512, WorldSizeY = 64, WorldSizeZ = 512;

    private const int CHUNK_SIZE = 32, PADDED = 34;
    private const int CHUNKS_X = 16, CHUNKS_Y = 2, CHUNKS_Z = 16;
    private const int MAX_CHUNKS = 512; // 16 * 2 * 16

    // Cold: voxel data per chunk, padded to 34³ = 39304 bytes
    private readonly byte[][] _chunkVoxels = new byte[MAX_CHUNKS][];

    // Warm: dirty flags
    private readonly bool[] _chunkDirty = new bool[MAX_CHUNKS];

    // Hot: AABBs (public fields, not properties)
    public Vector3[] ChunkAabbMin = new Vector3[MAX_CHUNKS];
    public Vector3[] ChunkAabbMax = new Vector3[MAX_CHUNKS];

    // Reusable list for DrainDirtyChunks — no per-drain allocation
    private readonly List<int> _dirtyList = new(MAX_CHUNKS);

    public VoxelWorld()
    {
        for (int i = 0; i < MAX_CHUNKS; i++)
        {
            _chunkVoxels[i] = new byte[PADDED * PADDED * PADDED]; // 39304
            var origin = ChunkOrigin(i);
            ChunkAabbMin[i] = origin;
            ChunkAabbMax[i] = origin + new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE);
        }
    }

    // Chunk index from chunk coords
    public static int ChunkIndex(int cx, int cy, int cz)
        => cx + cy * CHUNKS_X + cz * CHUNKS_X * CHUNKS_Y;

    // World-space origin of a chunk given its flat index
    public static Vector3 ChunkOrigin(int chunkIndex)
    {
        int cx = chunkIndex % CHUNKS_X;
        int cy = (chunkIndex / CHUNKS_X) % CHUNKS_Y;
        int cz = chunkIndex / (CHUNKS_X * CHUNKS_Y);
        return new Vector3(cx * CHUNK_SIZE, cy * CHUNK_SIZE, cz * CHUNK_SIZE);
    }

    // Padded array index for local coords x,y,z ∈ [0,31]
    private static int PaddedIndex(int lx, int ly, int lz)
        => (lx + 1) + (ly + 1) * PADDED + (lz + 1) * PADDED * PADDED;

    public byte GetVoxel(int x, int y, int z)
    {
        if ((uint)x >= WorldSizeX || (uint)y >= WorldSizeY || (uint)z >= WorldSizeZ)
            return 0;

        int cx = x / CHUNK_SIZE, cy = y / CHUNK_SIZE, cz = z / CHUNK_SIZE;
        int lx = x % CHUNK_SIZE, ly = y % CHUNK_SIZE, lz = z % CHUNK_SIZE;
        return _chunkVoxels[ChunkIndex(cx, cy, cz)][PaddedIndex(lx, ly, lz)];
    }

    public void SetVoxel(int x, int y, int z, byte typeId)
    {
        if ((uint)x >= WorldSizeX || (uint)y >= WorldSizeY || (uint)z >= WorldSizeZ)
            return;

        int cx = x / CHUNK_SIZE, cy = y / CHUNK_SIZE, cz = z / CHUNK_SIZE;
        int lx = x % CHUNK_SIZE, ly = y % CHUNK_SIZE, lz = z % CHUNK_SIZE;
        int ci = ChunkIndex(cx, cy, cz);

        _chunkVoxels[ci][PaddedIndex(lx, ly, lz)] = typeId;
        _chunkDirty[ci] = true;

        // Write-through ghost borders into neighbor chunks
        WriteGhostBorders(cx, cy, cz, lx, ly, lz, typeId);
    }

    // Writes voxel value into neighbor chunk ghost slots for any border local coord.
    private void WriteGhostBorders(int cx, int cy, int cz, int lx, int ly, int lz, byte typeId)
    {
        // For each axis, if on a border, write into the neighbor's padded ghost slot.
        // dx/dy/dz: neighbor offset; gx/gy/gz: ghost coord in neighbor's padded array.
        Span<int> dx = stackalloc int[2];
        Span<int> dy = stackalloc int[2];
        Span<int> dz = stackalloc int[2];
        Span<int> gx = stackalloc int[2];
        Span<int> gy = stackalloc int[2];
        Span<int> gz = stackalloc int[2];

        int xCount = 0, yCount = 0, zCount = 0;

        if (lx == 0)            { dx[xCount] = -1; gx[xCount++] = CHUNK_SIZE; }     // ghost at +32 in neighbor
        if (lx == CHUNK_SIZE-1) { dx[xCount] = +1; gx[xCount++] = -1; }             // ghost at -1 in neighbor

        if (ly == 0)            { dy[yCount] = -1; gy[yCount++] = CHUNK_SIZE; }
        if (ly == CHUNK_SIZE-1) { dy[yCount] = +1; gy[yCount++] = -1; }

        if (lz == 0)            { dz[zCount] = -1; gz[zCount++] = CHUNK_SIZE; }
        if (lz == CHUNK_SIZE-1) { dz[zCount] = +1; gz[zCount++] = -1; }

        // Iterate all combinations of border axes that are active
        for (int xi = 0; xi < xCount; xi++)
            WriteNeighborGhost(cx + dx[xi], cy, cz, gx[xi], ly, lz, typeId);

        for (int yi = 0; yi < yCount; yi++)
            WriteNeighborGhost(cx, cy + dy[yi], cz, lx, gy[yi], lz, typeId);

        for (int zi = 0; zi < zCount; zi++)
            WriteNeighborGhost(cx, cy, cz + dz[zi], lx, ly, gz[zi], typeId);

        // Edge neighbors (2 axes)
        for (int xi = 0; xi < xCount; xi++)
        for (int yi = 0; yi < yCount; yi++)
            WriteNeighborGhost(cx + dx[xi], cy + dy[yi], cz, gx[xi], gy[yi], lz, typeId);

        for (int xi = 0; xi < xCount; xi++)
        for (int zi = 0; zi < zCount; zi++)
            WriteNeighborGhost(cx + dx[xi], cy, cz + dz[zi], gx[xi], ly, gz[zi], typeId);

        for (int yi = 0; yi < yCount; yi++)
        for (int zi = 0; zi < zCount; zi++)
            WriteNeighborGhost(cx, cy + dy[yi], cz + dz[zi], lx, gy[yi], gz[zi], typeId);

        // Corner neighbors (3 axes)
        for (int xi = 0; xi < xCount; xi++)
        for (int yi = 0; yi < yCount; yi++)
        for (int zi = 0; zi < zCount; zi++)
            WriteNeighborGhost(cx + dx[xi], cy + dy[yi], cz + dz[zi], gx[xi], gy[yi], gz[zi], typeId);
    }

    // Writes into a neighbor chunk's padded array using ghost coords (may be -1 or CHUNK_SIZE).
    // Ghost coord -1 maps to padded index 0; CHUNK_SIZE maps to padded index CHUNK_SIZE+1 = 33.
    private void WriteNeighborGhost(int ncx, int ncy, int ncz, int glx, int gly, int glz, byte typeId)
    {
        if ((uint)ncx >= CHUNKS_X || (uint)ncy >= CHUNKS_Y || (uint)ncz >= CHUNKS_Z)
            return;

        int ni = ChunkIndex(ncx, ncy, ncz);
        // Ghost coord: -1 → padded index 0, CHUNK_SIZE → padded index CHUNK_SIZE+1
        // PaddedIndex adds +1, so pass (glx) directly — it already encodes the ghost offset.
        _chunkVoxels[ni][(glx + 1) + (gly + 1) * PADDED + (glz + 1) * PADDED * PADDED] = typeId;
        _chunkDirty[ni] = true;
    }

    public void SetBlock(int x, int y, int z, int sizeX, int sizeY, int sizeZ, byte typeId)
    {
        // Collect touched chunks without per-voxel dirty writes; flush once at end.
        var touched = new HashSet<int>();

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
            int lx = vx % CHUNK_SIZE, ly = vy % CHUNK_SIZE, lz = vz % CHUNK_SIZE;
            int ci = ChunkIndex(cx, cy, cz);
            _chunkVoxels[ci][PaddedIndex(lx, ly, lz)] = typeId;
            touched.Add(ci);
            // Ghost borders for block edges
            WriteGhostBordersTracked(cx, cy, cz, lx, ly, lz, typeId, touched);
        }

        foreach (int ci in touched)
            _chunkDirty[ci] = true;
    }

    // Same as WriteGhostBorders but adds neighbor indices to the touched set instead of marking dirty directly.
    private void WriteGhostBordersTracked(int cx, int cy, int cz, int lx, int ly, int lz, byte typeId, HashSet<int> touched)
    {
        Span<int> dx = stackalloc int[2];
        Span<int> dy = stackalloc int[2];
        Span<int> dz = stackalloc int[2];
        Span<int> gx = stackalloc int[2];
        Span<int> gy = stackalloc int[2];
        Span<int> gz = stackalloc int[2];

        int xCount = 0, yCount = 0, zCount = 0;

        if (lx == 0)            { dx[xCount] = -1; gx[xCount++] = CHUNK_SIZE; }
        if (lx == CHUNK_SIZE-1) { dx[xCount] = +1; gx[xCount++] = -1; }
        if (ly == 0)            { dy[yCount] = -1; gy[yCount++] = CHUNK_SIZE; }
        if (ly == CHUNK_SIZE-1) { dy[yCount] = +1; gy[yCount++] = -1; }
        if (lz == 0)            { dz[zCount] = -1; gz[zCount++] = CHUNK_SIZE; }
        if (lz == CHUNK_SIZE-1) { dz[zCount] = +1; gz[zCount++] = -1; }

        for (int xi = 0; xi < xCount; xi++)
            WriteNeighborGhostTracked(cx + dx[xi], cy, cz, gx[xi], ly, lz, typeId, touched);
        for (int yi = 0; yi < yCount; yi++)
            WriteNeighborGhostTracked(cx, cy + dy[yi], cz, lx, gy[yi], lz, typeId, touched);
        for (int zi = 0; zi < zCount; zi++)
            WriteNeighborGhostTracked(cx, cy, cz + dz[zi], lx, ly, gz[zi], typeId, touched);

        for (int xi = 0; xi < xCount; xi++)
        for (int yi = 0; yi < yCount; yi++)
            WriteNeighborGhostTracked(cx + dx[xi], cy + dy[yi], cz, gx[xi], gy[yi], lz, typeId, touched);
        for (int xi = 0; xi < xCount; xi++)
        for (int zi = 0; zi < zCount; zi++)
            WriteNeighborGhostTracked(cx + dx[xi], cy, cz + dz[zi], gx[xi], ly, gz[zi], typeId, touched);
        for (int yi = 0; yi < yCount; yi++)
        for (int zi = 0; zi < zCount; zi++)
            WriteNeighborGhostTracked(cx, cy + dy[yi], cz + dz[zi], lx, gy[yi], gz[zi], typeId, touched);

        for (int xi = 0; xi < xCount; xi++)
        for (int yi = 0; yi < yCount; yi++)
        for (int zi = 0; zi < zCount; zi++)
            WriteNeighborGhostTracked(cx + dx[xi], cy + dy[yi], cz + dz[zi], gx[xi], gy[yi], gz[zi], typeId, touched);
    }

    private void WriteNeighborGhostTracked(int ncx, int ncy, int ncz, int glx, int gly, int glz, byte typeId, HashSet<int> touched)
    {
        if ((uint)ncx >= CHUNKS_X || (uint)ncy >= CHUNKS_Y || (uint)ncz >= CHUNKS_Z)
            return;

        int ni = ChunkIndex(ncx, ncy, ncz);
        _chunkVoxels[ni][(glx + 1) + (gly + 1) * PADDED + (glz + 1) * PADDED * PADDED] = typeId;
        touched.Add(ni);
    }

    public ReadOnlySpan<int> DrainDirtyChunks()
    {
        _dirtyList.Clear();
        for (int i = 0; i < MAX_CHUNKS; i++)
        {
            if (_chunkDirty[i])
                _dirtyList.Add(i);
        }
        for (int i = 0; i < MAX_CHUNKS; i++)
            _chunkDirty[i] = false;

        return CollectionsMarshal.AsSpan(_dirtyList);
    }

    internal void SetChunkVoxels(int chunkIndex, ReadOnlySpan<byte> padded)
    {
        Debug.Assert(padded.Length == PADDED * PADDED * PADDED);
        padded.CopyTo(_chunkVoxels[chunkIndex]);
        _chunkDirty[chunkIndex] = true;
    }

    public ReadOnlySpan<byte> GetPaddedChunkVoxels(int chunkIndex)
        => _chunkVoxels[chunkIndex];
}
