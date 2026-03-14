# VoxPopuli Step 1 — CPU Data Structures Design

## Module Boundaries

```
VoxPopuli.Game
└── VoxelWorld          owns all voxel data; no GPU, no SDL

VoxPopuli.Renderer
├── VoxelVertex         8-byte vertex struct; pure data
└── MeshBuilder         static greedy-face emitter; reads padded chunk, writes spans
```

These two modules are independent. `MeshBuilder` knows nothing about `VoxelWorld`; it
operates only on a `ReadOnlySpan<byte>` of padded voxels. `VoxelWorld` knows nothing
about vertices. The coupling point is the caller (future `VoxGame`), which pulls a
padded span from `VoxelWorld` and passes it to `MeshBuilder`.

---

## VoxelWorld (Game/VoxelWorld.cs)

### Responsibilities
- Owns the canonical voxel state for the entire 512×128×512 world.
- Maintains padded (34³) per-chunk arrays so `MeshBuilder` never needs to cross-chunk
  lookups at runtime.
- Tracks which chunks changed so the renderer can rebuild only dirty meshes.
- Exposes hot AABB arrays as plain public fields for zero-overhead frustum culling.

### Constants and layout

```
Chunks: 16 (X) × 4 (Y) × 16 (Z) = 1024
ChunkIndex(cx,cy,cz) = cx + cy*16 + cz*64

Padded array size: 34*34*34 = 39304 bytes per chunk
PaddedIndex(lx,ly,lz) = (lx+1) + (ly+1)*34 + (lz+1)*34*34
  where lx,ly,lz ∈ [-1, 32]  (ghost border = -1 or 32)
```

### Internal data (separation of hot/cold)

| Field | Access pattern | Rationale |
|---|---|---|
| `byte[][] _chunkVoxels` | cold — only on mesh rebuild | jagged array; each slot is 39304 bytes allocated once |
| `bool[] _chunkDirty` | warm — set on every write | flat array, 1024 bools |
| `Vector3[] ChunkAabbMin/Max` | hot — read every frame | public fields, flat arrays |

`_chunkDirty` is separate from `_chunkVoxels` so the dirty scan never touches voxel
memory. `ChunkAabbMin/Max` are public fields (not properties) to avoid any overhead in
the frustum-cull loop.

### Public interface

```csharp
namespace VoxPopuli.Game;

public sealed class VoxelWorld
{
    public const int WorldSizeX = 512, WorldSizeY = 128, WorldSizeZ = 512;
    public const int ChunksX = 16, ChunksY = 4, ChunksZ = 16;
    public const int MAX_CHUNKS = ChunksX * ChunksY * ChunksZ; // 1024

    // Hot AABB data — read directly by frustum culler, no accessor overhead.
    public readonly Vector3[] ChunkAabbMin = new Vector3[MAX_CHUNKS];
    public readonly Vector3[] ChunkAabbMax = new Vector3[MAX_CHUNKS];

    public VoxelWorld();

    // Single-voxel write. No-op if out of bounds.
    // Marks owning chunk dirty and refreshes ghost borders of up to 6 neighbors.
    public void SetVoxel(int x, int y, int z, byte typeId);

    // AABB write. Marks all touched chunks dirty once (not per-voxel).
    // No-op for any coordinate outside world bounds.
    public void SetBlock(int x, int y, int z, int sizeX, int sizeY, int sizeZ, byte typeId);

    // Returns 0 for out-of-bounds reads.
    public byte GetVoxel(int x, int y, int z);

    // Returns indices of all dirty chunks and clears the dirty set.
    // Span is valid until the next call to DrainDirtyChunks or any write.
    public ReadOnlySpan<int> DrainDirtyChunks();

    // Returns the 34³ padded voxel array for the given chunk index.
    public ReadOnlySpan<byte> GetPaddedChunkVoxels(int chunkIndex);

    // World-space origin of chunk (cx*32, cy*32, cz*32).
    public static Vector3 ChunkOrigin(int chunkIndex);

    // cx + cy*16 + cz*64
    public static int ChunkIndex(int cx, int cy, int cz);
}
```

### Ghost border refresh algorithm

When `SetVoxel(x, y, z, typeId)` is called:

1. Compute owning chunk `(cx, cy, cz)` and local coords `(lx, ly, lz)`.
2. Write `typeId` into `_chunkVoxels[chunkIdx]` at `PaddedIndex(lx, ly, lz)`.
3. Mark `_chunkDirty[chunkIdx] = true`.
4. For each of the 6 face directions, check if `lx/ly/lz` is on that edge:

```
Face +X: lx == 31  → neighbor (cx+1, cy, cz), write into its ghost at PaddedIndex(-1, ly, lz)
Face -X: lx == 0   → neighbor (cx-1, cy, cz), write into its ghost at PaddedIndex(32, ly, lz)
Face +Y: ly == 31  → neighbor (cx, cy+1, cz), write into its ghost at PaddedIndex(lx, -1, lz)
Face -Y: ly == 0   → neighbor (cx, cy-1, cz), write into its ghost at PaddedIndex(lx, 32, lz)
Face +Z: lz == 31  → neighbor (cx, cy, cz+1), write into its ghost at PaddedIndex(lx, ly, -1)
Face -Z: lz == 0   → neighbor (cx, cy, cz-1), write into its ghost at PaddedIndex(lx, ly, 32)
```

5. For each valid neighbor found, mark it dirty too.

Corner/edge voxels may trigger up to 3 neighbor updates; this is correct and intentional.

`SetBlock` iterates the AABB, writes voxels and ghost borders using the same per-voxel
logic internally, but collects dirty chunk indices into a temporary `HashSet<int>` and
flushes them once at the end to avoid redundant dirty marking.

### DrainDirtyChunks implementation note

Internally uses a `List<int> _dirtyList` that is rebuilt on each drain by scanning
`_chunkDirty[0..1023]`. The returned `ReadOnlySpan<int>` wraps `_dirtyList`'s backing
array. After returning, all `_chunkDirty` entries are cleared. This avoids a separate
`HashSet` for the common case and keeps the drain O(MAX_CHUNKS) = O(1024).

---

## VoxelVertex (Renderer/VoxelVertex.cs)

### Responsibilities
Pure data carrier. No logic. 8 bytes, sequential layout, GPU-uploadable as-is.

```csharp
namespace VoxPopuli.Renderer;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct VoxelVertex
{
    public readonly byte X, Y, Z;   // local voxel position 0–31
    public readonly byte Face;       // 0=+X 1=-X 2=+Y 3=-Y 4=+Z 5=-Z
    public readonly uint Color;      // 0xRRGGBB00
}
```

Face encoding is an index, not a bitmask, so the GPU shader can use it directly as an
array index into a normal/light table.

---

## MeshBuilder (Renderer/MeshBuilder.cs)

### Responsibilities
Converts a 34³ padded voxel span into a vertex + index buffer. Pure static, no state,
no allocation. Caller owns the output spans.

### Public interface

```csharp
namespace VoxPopuli.Renderer;

public static class MeshBuilder
{
    // Colors for typeId 0–8. Index 0 = air (unused). 1–8 = distinct solid colors.
    public static readonly uint[] TypeColors; // length 9, format 0xRRGGBB00

    // Emits quads for all visible faces in the padded chunk.
    // Truncates gracefully if output spans are too small — no exception thrown.
    // A fully solid 32³ chunk produces exactly 24576 verts and 36864 indices.
    public static void Build(
        ReadOnlySpan<byte> padded,       // must be 34*34*34 = 39304 bytes
        Span<VoxelVertex>  verts,
        Span<uint>         indices,
        out int            vertCount,
        out int            idxCount);
}
```

### Face quad vertex corners

All corners are expressed as offsets added to the voxel's local position `(x, y, z)`.
Winding is CCW when viewed from outside (from the direction the face normal points).

```
Face 0 (+X, normal = +X):  x+1 is the fixed axis
  v0 = (x+1, y,   z  )
  v1 = (x+1, y+1, z  )
  v2 = (x+1, y+1, z+1)
  v3 = (x+1, y,   z+1)

Face 1 (-X, normal = -X):  x is the fixed axis
  v0 = (x,   y,   z+1)
  v1 = (x,   y+1, z+1)
  v2 = (x,   y+1, z  )
  v3 = (x,   y,   z  )

Face 2 (+Y, normal = +Y):  y+1 is the fixed axis
  v0 = (x,   y+1, z  )
  v1 = (x,   y+1, z+1)
  v2 = (x+1, y+1, z+1)
  v3 = (x+1, y+1, z  )

Face 3 (-Y, normal = -Y):  y is the fixed axis
  v0 = (x+1, y,   z  )
  v1 = (x+1, y,   z+1)
  v2 = (x,   y,   z+1)
  v3 = (x,   y,   z  )

Face 4 (+Z, normal = +Z):  z+1 is the fixed axis
  v0 = (x+1, y,   z+1)
  v1 = (x+1, y+1, z+1)
  v2 = (x,   y+1, z+1)
  v3 = (x,   y,   z+1)

Face 5 (-Z, normal = -Z):  z is the fixed axis
  v0 = (x,   y,   z  )
  v1 = (x,   y+1, z  )
  v2 = (x+1, y+1, z  )
  v3 = (x+1, y,   z  )
```

Index pattern per quad (base = current vertCount before this quad):
```
base+0, base+1, base+2,   base+0, base+2, base+3
```

### Neighbor lookup in padded array

For owned voxel at local `(lx, ly, lz)` ∈ [0,31], the 6 neighbor padded indices are:

```
+X neighbor: PaddedIndex(lx+1, ly,   lz  )
-X neighbor: PaddedIndex(lx-1, ly,   lz  )
+Y neighbor: PaddedIndex(lx,   ly+1, lz  )
-Y neighbor: PaddedIndex(lx,   ly-1, lz  )
+Z neighbor: PaddedIndex(lx,   ly,   lz+1)
-Z neighbor: PaddedIndex(lx,   ly,   lz-1)
```

Because `lx ∈ [0,31]`, `lx+1` reaches at most 32 and `lx-1` reaches at most -1, both
within the padded range [-1,32]. No bounds check needed inside the loop.

### Solid chunk verification

A fully solid 32³ chunk has only its 6 outer faces visible:
- 2 faces × (32×32) per axis × 3 axes = 6144 quads
- 6144 × 4 = 24576 verts
- 6144 × 6 = 36864 indices

---

## Non-obvious decisions

**Ghost border is write-through, not lazy.** Refreshing neighbor ghost cells on every
`SetVoxel` means `GetPaddedChunkVoxels` always returns a consistent snapshot. A lazy
approach (refresh on read) would require tracking which borders are stale, adding
complexity to the hot read path.

**`DrainDirtyChunks` returns a span, not a list copy.** The caller (future `VoxGame`)
iterates it immediately to kick off mesh rebuilds. No allocation on the drain path.

**`MeshBuilder` is stateless and allocation-free.** The caller pre-allocates output
spans sized to worst-case (32³ solid = 24576 verts / 36864 indices). This matches the
GPU upload pattern where a fixed staging buffer is reused each frame.

**`VoxelVertex.Face` is a byte index, not a bitmask.** The shader will index a
`constant float3 normals[6]` array. A bitmask would require a `countbits` or branch in
the shader; an index is a direct load.

**`ChunkAabbMin/Max` are public fields, not properties.** The frustum culler will loop
over all 1024 entries every frame. Property accessors add a method call per element;
direct field access on a known-layout array is a single load.
