# Flat Voxel Array Refactor — Design

## Overview

Replace per-chunk `byte[]` heap allocations with a single flat `byte[]` owned by `VoxelWorld`.
`Chunk` becomes a thin view (offset + reference to the flat array) with no owned storage.
All existing call sites change minimally. Streaming is a first-class concern.

---

## Data Layout

```
VoxelWorld._voxels : byte[MAX_CHUNKS * Chunk.VOLUME]
                     = byte[512 * 32768]
                     = byte[16,777,216]  (~16 MB)

Slot for chunk index i starts at: i * Chunk.VOLUME
Slot size:                        Chunk.VOLUME = 32 * 32 * 32 = 32,768 bytes
```

Chunk coordinates map to a flat index exactly as today:

```
ChunkIndex(cx, cy, cz) = cx + cy * CHUNKS_X + cz * CHUNKS_X * CHUNKS_Y
```

Voxel offset within the flat array:

```
FlatOffset(chunkIndex, lx, ly, lz)
    = chunkIndex * Chunk.VOLUME + Chunk.LocalIndex(lx, ly, lz)
    = chunkIndex * Chunk.VOLUME + lx + ly * SIZE + lz * SIZE * SIZE
```

---

## Module Boundaries

| Module | Responsibility |
|---|---|
| `VoxelWorld` | Owns `_voxels`, exposes `GetVoxel`/`SetVoxel`, dirty tracking, chunk streaming slots |
| `Chunk` | Thin view: holds `(byte[] array, int baseOffset)`. No owned storage. |
| `TerrainGenerator` | Writes into a `Chunk` view exactly as today |
| `CpuChunkMeshBuilder` | Reads voxels via direct flat-index arithmetic; no `Chunk` object access in inner loop |

---

## Type Signatures

### `Chunk.cs`

```csharp
public sealed class Chunk
{
    public const int SIZE   = 32;
    public const int VOLUME = SIZE * SIZE * SIZE; // 32768

    // View fields — no owned storage
    private readonly byte[] _array;
    private readonly int    _base;   // = chunkIndex * VOLUME

    internal Chunk(byte[] array, int baseOffset);

    public static int LocalIndex(int lx, int ly, int lz)
        => lx + ly * SIZE + lz * SIZE * SIZE;

    public byte Get(int lx, int ly, int lz);
    public void Set(int lx, int ly, int lz, byte type);

    // Zero-copy span over this chunk's slot in the flat array
    public ReadOnlySpan<byte> Data         { get; }
    internal Span<byte>       MutableData  { get; }
}
```

`Chunk` remains a `sealed class` (not a `ref struct`) so `VoxelWorld.Chunks[]` and
`TerrainGenerator` can hold references without lifetime restrictions.

---

### `VoxelWorld.cs`

```csharp
public sealed class VoxelWorld
{
    public const int WorldSizeX = 512, WorldSizeY = 64, WorldSizeZ = 512;

    private const int CHUNK_SIZE = Chunk.SIZE;
    private const int CHUNKS_X = 16, CHUNKS_Y = 2, CHUNKS_Z = 16;
    public  const int MAX_CHUNKS = 512;

    // ── Single flat allocation ──────────────────────────────────────────────
    internal readonly byte[]  _voxels;          // [MAX_CHUNKS * Chunk.VOLUME]
    public   readonly Chunk[] Chunks;           // thin views; index == chunkIndex

    // ── Spatial metadata (unchanged) ───────────────────────────────────────
    public Vector3[] ChunkAabbMin = new Vector3[MAX_CHUNKS];
    public Vector3[] ChunkAabbMax = new Vector3[MAX_CHUNKS];

    // ── Dirty tracking (unchanged) ─────────────────────────────────────────
    private readonly bool[]    _dirty     = new bool[MAX_CHUNKS];
    private readonly List<int> _dirtyList = new(MAX_CHUNKS);

    // ── Streaming state ────────────────────────────────────────────────────
    private readonly bool[] _loaded = new bool[MAX_CHUNKS]; // false = slot is zeroed / unloaded

    public VoxelWorld();

    // ── Coordinate helpers (unchanged signatures) ──────────────────────────
    public static int     ChunkIndex(int cx, int cy, int cz);
    public static Vector3 ChunkOrigin(int chunkIndex);

    // ── Voxel access (unchanged signatures) ───────────────────────────────
    public byte GetVoxel(int x, int y, int z);
    public void SetVoxel(int x, int y, int z, byte typeId);
    public void SetBlock(int x, int y, int z, int sizeX, int sizeY, int sizeZ, byte typeId);

    // ── Dirty drain (unchanged signature) ─────────────────────────────────
    public ReadOnlySpan<int> DrainDirtyChunks();

    // ── Streaming API (new) ────────────────────────────────────────────────

    // Copy exactly Chunk.VOLUME bytes from `data` into slot `chunkIndex`.
    // Marks the chunk dirty. `data.Length` must equal Chunk.VOLUME.
    public void LoadChunk(int chunkIndex, ReadOnlySpan<byte> data);

    // Zero the slot and mark not-loaded. Does NOT mark dirty (renderer
    // must be told separately via the return value of DrainDirtyChunks or
    // a dedicated unload notification — see notes below).
    public void UnloadChunk(int chunkIndex);

    public bool IsLoaded(int chunkIndex);

    // Direct flat-array access for mesh builder (avoids Chunk object lookup)
    // Returns a read-only span over the chunk's 32 KB slot.
    internal ReadOnlySpan<byte> ChunkSlot(int chunkIndex);
}
```

`LoadChunk` / `UnloadChunk` are the entire streaming interface. Loading a chunk from disk
is: deserialize 32 KB → call `LoadChunk`. Unloading is: call `UnloadChunk` (optionally
serialize first). No other plumbing is required.

---

### `CpuChunkMeshBuilder.cs`

```csharp
internal sealed class CpuChunkMeshBuilder : IChunkMeshBuilder
{
    public int Build(VoxelWorld world, int chunkIndex, Span<VoxelVertex> output);
}
```

Signature is unchanged. Internal implementation changes:

- Replace `world.Chunks[chunkIndex].Get(x, y, z)` with a direct read from
  `world._voxels[chunkIndex * Chunk.VOLUME + Chunk.LocalIndex(x, y, z)]`.
- Replace `world.GetVoxel(wx, wy, wz)` neighbor lookups with an inline flat-index
  calculation (see "Neighbor Lookup" section below).

---

### `TerrainGenerator.cs`

```csharp
internal static class TerrainGenerator
{
    internal static void GenerateChunk(int cx, int cy, int cz, int seed, Chunk chunk);
    internal static void GenerateWorld(VoxelWorld world, int seed);
}
```

Signatures are unchanged. `GenerateChunk` calls `chunk.Set(...)` exactly as today —
`Chunk.Set` now writes into the flat array through the view. No logic changes required.

`GenerateWorld` replaces `world.Chunks[i].MutableData.Clear()` with
`world.ChunkSlot(i)` — but since `VoxelWorld` constructor zero-initialises `_voxels`
with `new byte[...]`, the explicit clear can be removed entirely.

---

## Neighbor Lookup in CpuChunkMeshBuilder

Current code calls `world.GetVoxel(wx, wy, wz)` for each of the 6 face neighbors.
`GetVoxel` does a bounds check, a division, and a `Chunk` object dereference.

With the flat array, the same result is a single index expression:

```
// Given world position (wx, wy, wz):
if out-of-bounds → 0
cx = wx / SIZE,  cy = wy / SIZE,  cz = wz / SIZE
ci = ChunkIndex(cx, cy, cz)
flat = ci * VOLUME + LocalIndex(wx % SIZE, wy % SIZE, wz % SIZE)
neighbor = world._voxels[flat]
```

This can be expressed as a private static helper inside `CpuChunkMeshBuilder`:

```csharp
private static byte SampleFlat(byte[] voxels, int wx, int wy, int wz);
```

No `Chunk` object is touched. The inner loop becomes a pure array read.

---

## Constructor Initialisation

```csharp
public VoxelWorld()
{
    _voxels = new byte[MAX_CHUNKS * Chunk.VOLUME]; // zero-initialised by CLR
    Chunks  = new Chunk[MAX_CHUNKS];
    for (int i = 0; i < MAX_CHUNKS; i++)
    {
        Chunks[i] = new Chunk(_voxels, i * Chunk.VOLUME);
        var origin = ChunkOrigin(i);
        ChunkAabbMin[i] = origin;
        ChunkAabbMax[i] = origin + new Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE);
    }
}
```

---

## Per-File Migration Notes

### `Chunk.cs`

- Remove `private readonly byte[] _voxels = new byte[VOLUME];`
- Add `private readonly byte[] _array` and `private readonly int _base`
- Add `internal Chunk(byte[] array, int baseOffset)` constructor
- `Get` → `return _array[_base + LocalIndex(lx, ly, lz)];`
- `Set` → `_array[_base + LocalIndex(lx, ly, lz)] = type;`
- `Data` → `return new ReadOnlySpan<byte>(_array, _base, VOLUME);`
- `MutableData` → `return new Span<byte>(_array, _base, VOLUME);`
- Rename `Index` → `LocalIndex` (clarifies it is chunk-local, not flat)

### `VoxelWorld.cs`

- Add `internal readonly byte[] _voxels`
- Constructor: allocate `_voxels`, pass `(_voxels, i * Chunk.VOLUME)` to each `Chunk`
- Add `LoadChunk`, `UnloadChunk`, `IsLoaded`, `ChunkSlot`
- Everything else (`GetVoxel`, `SetVoxel`, `SetBlock`, `DrainDirtyChunks`, `ChunkOrigin`,
  `ChunkIndex`) is unchanged — they delegate to `Chunk.Get/Set` which now write into
  the flat array transparently

### `CpuChunkMeshBuilder.cs`

- Replace `world.Chunks[chunkIndex].Get(x, y, z)` with direct flat read via
  `world._voxels[chunkIndex * Chunk.VOLUME + Chunk.LocalIndex(x, y, z)]`
- Replace `world.GetVoxel(...)` neighbor calls with `SampleFlat(world._voxels, wx, wy, wz)`
- Add private static `SampleFlat` helper

### `TerrainGenerator.cs`

- No signature changes
- `GenerateWorld`: remove `world.Chunks[i].MutableData.Clear()` (flat array is
  zero-initialised at construction; for streaming re-use, `UnloadChunk` handles zeroing)
- `GenerateChunk`: no changes — `chunk.Set(...)` works through the view

---

## Streaming Contract

| Operation | Mechanism |
|---|---|
| Load chunk from disk | Deserialize 32 KB → `world.LoadChunk(ci, span)` |
| Unload chunk | `world.UnloadChunk(ci)` — zeroes slot, clears `_loaded[ci]` |
| Check if loaded | `world.IsLoaded(ci)` |
| `GetVoxel` on unloaded chunk | Returns 0 (slot is zeroed) — safe, no special case needed |
| Renderer skip | Caller checks `IsLoaded` before queuing a chunk for mesh build |

The `_loaded` flag is the only streaming state. No additional indirection or nullable
chunk references are needed because an unloaded slot is indistinguishable from an
all-air chunk at the voxel level.

---

## What Does Not Change

- `IChunkMeshBuilder` interface
- `VoxelWorld.ChunkIndex`, `ChunkOrigin` — identical
- `VoxelWorld.GetVoxel`, `SetVoxel`, `SetBlock`, `DrainDirtyChunks` — identical signatures and semantics
- `TerrainGenerator.GenerateChunk` — identical signature
- `ChunkAabbMin` / `ChunkAabbMax` arrays — unchanged
- All call sites outside these four files — zero changes required
