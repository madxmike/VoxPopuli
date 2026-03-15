namespace VoxPopuli.Renderer;

using System;
using System.Numerics;
using System.Threading;
using VoxPopuli.Game;

/// <summary>Builds a triangle-list mesh for a chunk using greedy meshing.</summary>
internal sealed class GreedyChunkMeshBuilder : IChunkMeshBuilder
{
    // Stores typeId and 4 corner AO values for each visible face in a depth-slice.
    // TypeId=0 means no face. CanMerge ensures greedy expansion only merges cells
    // with identical AO at all corners, preventing AO stretching across large quads.
    private readonly struct MaskCell
    {
        public readonly int TypeId;
        public readonly int AO00, AO10, AO01, AO11;
        public MaskCell(int typeId, int ao00, int ao10, int ao01, int ao11)
        { TypeId = typeId; AO00 = ao00; AO10 = ao10; AO01 = ao01; AO11 = ao11; }
        public bool CanMerge(MaskCell other) =>
            TypeId == other.TypeId && AO00 == other.AO00 && AO10 == other.AO10 &&
            AO01 == other.AO01 && AO11 == other.AO11;
    }
    private readonly MaskCell[] _mask = new MaskCell[Chunk.SIZE * Chunk.SIZE];

    // Reused coordinate buffer for axis-indexed local voxel lookups.
    private readonly int[] _localCoords = new int[3];

    /// <summary>Encodes the geometry of each of the 6 face orientations.</summary>
    private static readonly (int normalAxis, int normalSign, int planeOffset,
                              int uAxis, int uSign, int vAxis, int vSign)[] Faces =
    [
        (2, +1, 1,  0, +1, 1, +1), // +Z
        (2, -1, 0,  0, -1, 1, +1), // -Z
        (0, +1, 1,  2, -1, 1, +1), // +X
        (0, -1, 0,  2, +1, 1, +1), // -X
        (1, +1, 1,  0, +1, 2, -1), // +Y
        (1, -1, 0,  0, +1, 2, +1), // -Y
    ];

    /// <summary>Builds a mesh for a chunk and writes vertices to <paramref name="output"/>.</summary>
    public int Build(VoxelWorld world, int chunkIndex, Span<VoxelVertex> output, CancellationToken ct)
    {
        var origin = VoxelWorld.ChunkOrigin(chunkIndex);
        int ox = (int)origin.X, oy = (int)origin.Y, oz = (int)origin.Z;
        int vertexCount = 0;
        var chunk = world.GetChunk(chunkIndex);

        foreach (var face in Faces)
        {
            for (int depth = 0; depth < Chunk.SIZE; depth++)
            {
                ct.ThrowIfCancellationRequested();
                BuildMask(world, chunk, ox, oy, oz, face, depth);
                vertexCount += EmitQuads(output, vertexCount, ox, oy, oz, face, depth);
            }
        }

        return vertexCount;
    }

    /// <summary>Populates the mask for a single depth-slice along the face's normal axis.</summary>
    private void BuildMask(VoxelWorld world, Chunk chunk, int ox, int oy, int oz,
        (int normalAxis, int normalSign, int planeOffset, int uAxis, int uSign, int vAxis, int vSign) face, int depth)
    {
        int size = Chunk.SIZE;
        var (normalAxis, normalSign, planeOffset, uAxis, _, vAxis, _) = face;

        Array.Fill(_mask, default);

        for (int v = 0; v < size; v++)
        {
            for (int u = 0; u < size; u++)
            {
                _localCoords[normalAxis] = depth; _localCoords[uAxis] = u; _localCoords[vAxis] = v;
                byte typeId = chunk.Get(_localCoords[0], _localCoords[1], _localCoords[2]);
                if (typeId == 0)
                {
                    continue;
                }

                _localCoords[normalAxis] = depth + normalSign;
                bool neighborAir = _localCoords[normalAxis] < 0 || _localCoords[normalAxis] >= size
                    ? world.GetVoxel(ox + _localCoords[0], oy + _localCoords[1], oz + _localCoords[2]) == 0
                    : chunk.Get(_localCoords[0], _localCoords[1], _localCoords[2]) == 0;

                if (neighborAir)
                {
                    // Corner positions are at the 4 grid vertices of this voxel face.
                    // Always sample in the -1 direction from each corner so that shared
                    // corners between adjacent cells produce identical AO values (required for CanMerge).
                    int ao00 = ComputeCornerAO(world, chunk, ox, oy, oz, normalAxis, normalSign, uAxis, vAxis, depth, u,     v    );
                    int ao10 = ComputeCornerAO(world, chunk, ox, oy, oz, normalAxis, normalSign, uAxis, vAxis, depth, u + 1, v    );
                    int ao01 = ComputeCornerAO(world, chunk, ox, oy, oz, normalAxis, normalSign, uAxis, vAxis, depth, u,     v + 1);
                    int ao11 = ComputeCornerAO(world, chunk, ox, oy, oz, normalAxis, normalSign, uAxis, vAxis, depth, u + 1, v + 1);
                    _mask[u + v * size] = new MaskCell(typeId, ao00, ao10, ao01, ao11);
                }
            }
        }
    }

    /// <summary>Scans the mask and greedily merges visible cells into rectangles, emitting one quad per rectangle.</summary>
    private int EmitQuads(Span<VoxelVertex> output, int offset, int ox, int oy, int oz,
        (int normalAxis, int normalSign, int planeOffset, int uAxis, int uSign, int vAxis, int vSign) face, int depth)
    {
        int size = Chunk.SIZE;
        var (normalAxis, normalSign, planeOffset, uAxis, uSign, vAxis, vSign) = face;
        int vertexCount = 0;

        for (int v = 0; v < size; v++)
        {
            for (int u = 0; u < size; u++)
            {
                var cell = _mask[u + v * size];
                if (cell.TypeId == 0)
                {
                    continue;
                }

                int width  = ExpandWidth(u, v, cell);
                int height = ExpandHeight(u, v, cell, width);

                _localCoords[normalAxis] = depth + planeOffset;
                _localCoords[uAxis] = uSign > 0 ? u : u + width;
                _localCoords[vAxis] = vSign > 0 ? v : v + height;
                var quadOrigin = new Vector3(ox + _localCoords[0], oy + _localCoords[1], oz + _localCoords[2]);
                var du = SetAxis(Vector3.Zero, uAxis, uSign * width);
                var dv = SetAxis(Vector3.Zero, vAxis, vSign * height);

                // Map mask-space AO corners to quad vertices.
                // Mask: AO00=(u,v), AO10=(u+w,v), AO01=(u,v+h), AO11=(u+w,v+h)
                // q0=quadOrigin, q1=q0+du, q2=q0+dv, q3=q0+du+dv
                // When uSign<0, quadOrigin is at u+width in mask-space (du points left).
                // When vSign<0, quadOrigin is at v+height in mask-space (dv points down).
                int[] aoArr = [cell.AO00, cell.AO10, cell.AO01, cell.AO11];
                // index into aoArr: bit0=uEdge(0=u,1=u+w), bit1=vEdge(0=v,1=v+h)
                int q0ao = (uSign > 0 ? 0 : 1) | (vSign > 0 ? 0 : 2);
                int q1ao = (uSign > 0 ? 1 : 0) | (vSign > 0 ? 0 : 2);
                int q2ao = (uSign > 0 ? 0 : 1) | (vSign > 0 ? 2 : 0);
                int q3ao = (uSign > 0 ? 1 : 0) | (vSign > 0 ? 2 : 0);
                vertexCount += EmitQuad(output, offset + vertexCount, quadOrigin, du, dv, (uint)cell.TypeId,
                    aoArr[q0ao], aoArr[q1ao], aoArr[q2ao], aoArr[q3ao]);
                ClearMask(u, v, width, height);
            }
        }

        return vertexCount;
    }

    /// <summary>Expands rightward along u while cells can merge with the seed cell. Returns width ≥ 1.</summary>
    private int ExpandWidth(int u, int v, MaskCell seed)
    {
        int size = Chunk.SIZE;
        int width = 1;
        while (u + width < size && seed.CanMerge(_mask[(u + width) + v * size]))
        {
            width++;
        }
        return width;
    }

    /// <summary>Expands downward along v while every cell in the row can merge with the seed cell. Returns height ≥ 1.</summary>
    private int ExpandHeight(int u, int v, MaskCell seed, int width)
    {
        int size = Chunk.SIZE;
        int height = 1;
        while (v + height < size)
        {
            for (int k = 0; k < width; k++)
            {
                if (!seed.CanMerge(_mask[(u + k) + (v + height) * size]))
                {
                    return height;
                }
            }
            height++;
        }
        return height;
    }

    /// <summary>Emits a quad as two CCW triangles with per-vertex AO.</summary>
    private static int EmitQuad(Span<VoxelVertex> output, int offset, Vector3 quadOrigin, Vector3 du, Vector3 dv, uint typeId,
        int ao00, int ao10, int ao01, int ao11)
    {
        var q0 = quadOrigin;          // (u,   v)
        var q1 = quadOrigin + du;     // (u+w, v)
        var q2 = quadOrigin + dv;     // (u,   v+h)
        var q3 = quadOrigin + du + dv;// (u+w, v+h)

        bool flip = (ao00 + ao11) < (ao10 + ao01);
        if (!flip)
        {
            // Not flipped: (q0,q1,q3),(q0,q3,q2)
            output[offset + 0] = new VoxelVertex(q0, typeId, ao00);
            output[offset + 1] = new VoxelVertex(q1, typeId, ao10);
            output[offset + 2] = new VoxelVertex(q3, typeId, ao11);
            output[offset + 3] = new VoxelVertex(q0, typeId, ao00);
            output[offset + 4] = new VoxelVertex(q3, typeId, ao11);
            output[offset + 5] = new VoxelVertex(q2, typeId, ao01);
        }
        else
        {
            // Flipped: (q0,q1,q2),(q1,q3,q2)
            output[offset + 0] = new VoxelVertex(q0, typeId, ao00);
            output[offset + 1] = new VoxelVertex(q1, typeId, ao10);
            output[offset + 2] = new VoxelVertex(q2, typeId, ao01);
            output[offset + 3] = new VoxelVertex(q1, typeId, ao10);
            output[offset + 4] = new VoxelVertex(q3, typeId, ao11);
            output[offset + 5] = new VoxelVertex(q2, typeId, ao01);
        }
        return 6;
    }

    /// <summary>Samples 3 neighbors in the face plane to compute AO (0=fully occluded, 3=fully open).</summary>
    private static int ComputeCornerAO(VoxelWorld world, Chunk chunk, int ox, int oy, int oz,
        int normalAxis, int normalSign, int uAxis, int vAxis,
        int depth, int cornerU, int cornerV)
    {
        int bn = depth + normalSign;
        bool side1 = SampleSolid(world, chunk, ox, oy, oz, Chunk.SIZE, normalAxis, bn, uAxis, cornerU - 1, vAxis, cornerV    );
        bool side2 = SampleSolid(world, chunk, ox, oy, oz, Chunk.SIZE, normalAxis, bn, uAxis, cornerU,     vAxis, cornerV - 1);
        bool diag  = SampleSolid(world, chunk, ox, oy, oz, Chunk.SIZE, normalAxis, bn, uAxis, cornerU - 1, vAxis, cornerV - 1);
        if (side1 && side2) return 0;
        return 3 - (side1 ? 1 : 0) - (side2 ? 1 : 0) - (diag ? 1 : 0);
    }

    /// <summary>Samples a voxel at the given world coordinates, handling chunk boundaries.</summary>
    private static bool SampleSolid(VoxelWorld world, Chunk chunk, int ox, int oy, int oz, int size,
        int normalAxis, int bn, int uAxis, int lu, int vAxis, int lv)
    {
        int[] p = new int[3];
        p[normalAxis] = bn;
        p[uAxis] = lu;
        p[vAxis] = lv;
        int lx = p[0], ly = p[1], lz = p[2];
        if (lx >= 0 && lx < size && ly >= 0 && ly < size && lz >= 0 && lz < size)
            return chunk.Get(lx, ly, lz) != 0;
        return world.GetVoxel(ox + lx, oy + ly, oz + lz) != 0;
    }

    /// <summary>Clears the width×height rectangle at (u,v) in the mask so those cells are not re-processed.</summary>
    private void ClearMask(int u, int v, int width, int height)
    {
        int size = Chunk.SIZE;
        for (int dv = 0; dv < height; dv++)
        {
            for (int du = 0; du < width; du++)
            {
                _mask[(u + du) + (v + dv) * size] = default;
            }
        }
    }

    private static Vector3 SetAxis(Vector3 vec, int axis, float value) => axis switch
    {
        0 => vec with { X = value },
        1 => vec with { Y = value },
        _ => vec with { Z = value },
    };
}
