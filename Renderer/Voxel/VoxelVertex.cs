namespace VoxPopuli.Renderer;

using System.Numerics;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct VoxelVertex
{
    public readonly Vector3 Position; // world-space, offset 0, 12 bytes
    public readonly uint TypeId;      // voxel type, offset 12, 4 bytes
    // total: 16 bytes

    public VoxelVertex(Vector3 position, uint typeId) { Position = position; TypeId = typeId; }

    // Packs AO (0–3) into bits [31:30] and typeId into bits [29:0].
    public VoxelVertex(Vector3 position, uint typeId, int ao)
    {
        Position = position;
        TypeId = (typeId & 0x3FFFFFFFu) | ((uint)(ao & 3) << 30);
    }

    // Worst case: 32³ voxels × 6 faces × 6 vertices per face
    public const int MaxVerticesPerChunk = 32 * 32 * 32 * 6 * 6;
}
