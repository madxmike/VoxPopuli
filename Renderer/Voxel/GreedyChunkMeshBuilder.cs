namespace VoxPopuli.Renderer;

using System;
using System.Numerics;
using VoxPopuli.Game;

/// <summary>
/// Builds a triangle-list mesh for a chunk using greedy meshing — merging adjacent
/// coplanar faces of the same voxel type into single large quads rather than emitting
/// one quad per face. Produces significantly fewer vertices than a naive per-face builder
/// while rendering identically.
///
/// The builder is stateful (reuses an internal mask buffer) and is not thread-safe.
/// </summary>
internal sealed class GreedyChunkMeshBuilder : IChunkMeshBuilder
{
    // Reused across all Build calls to avoid per-frame allocation.
    // Stores typeId of visible faces for one depth-slice; 0 means no face.
    private readonly int[] _mask = new int[Chunk.SIZE * Chunk.SIZE];

    // Reused coordinate buffer for axis-indexed local voxel lookups.
    private readonly int[] _localCoords = new int[3];

    // Encodes the geometry of each of the 6 face orientations.
    // normalAxis/normalSign: which axis is the face normal and which direction (+1 or -1).
    // planeOffset: offset along normalAxis where the face plane sits (0 = near side, 1 = far side of voxel).
    // uAxis/uSign, vAxis/vSign: the two tangent axes and their winding directions.
    // Winding: vertices emitted as (P, P+du, P+du+dv, P+dv) → triangles (q0,q1,q2),(q0,q2,q3) — CCW front faces.
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

    /// <inheritdoc/>
    public int Build(VoxelWorld world, int chunkIndex, Span<VoxelVertex> output)
    {
        var origin = VoxelWorld.ChunkOrigin(chunkIndex);
        int ox = (int)origin.X, oy = (int)origin.Y, oz = (int)origin.Z;
        int vertexCount = 0;
        var chunk = world.GetChunk(chunkIndex);

        foreach (var face in Faces)
        {
            for (int depth = 0; depth < Chunk.SIZE; depth++)
            {
                BuildMask(world, chunk, ox, oy, oz, face, depth);
                vertexCount += EmitQuads(output, vertexCount, ox, oy, oz, face, depth);
            }
        }

        return vertexCount;
    }

    /// <summary>
    /// Populates <see cref="_mask"/> for a single depth-slice along the face's normal axis.
    /// Each cell stores the voxel's typeId if that face is visible (i.e. the neighbor
    /// in the normal direction is air), or 0 if the face is hidden or the voxel is air.
    /// Uses <see cref="VoxelWorld.GetVoxel"/> for neighbors that cross chunk boundaries.
    /// </summary>
    private void BuildMask(VoxelWorld world, Chunk chunk, int ox, int oy, int oz,
        (int normalAxis, int normalSign, int planeOffset, int uAxis, int uSign, int vAxis, int vSign) face, int depth)
    {
        int size = Chunk.SIZE;
        var (normalAxis, normalSign, _, uAxis, _, vAxis, _) = face;

        Array.Clear(_mask, 0, size * size);

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
                    _mask[u + v * size] = typeId;
                }
            }
        }
    }

    /// <summary>
    /// Scans <see cref="_mask"/> and greedily merges visible cells into the largest possible
    /// rectangles of uniform typeId, emitting one quad per rectangle. Consumed cells are zeroed
    /// so they are not processed again. Returns the number of vertices written.
    /// </summary>
    private int EmitQuads(Span<VoxelVertex> output, int offset, int ox, int oy, int oz,
        (int normalAxis, int normalSign, int planeOffset, int uAxis, int uSign, int vAxis, int vSign) face, int depth)
    {
        int size = Chunk.SIZE;
        var (normalAxis, _, planeOffset, uAxis, uSign, vAxis, vSign) = face;
        int vertexCount = 0;

        for (int v = 0; v < size; v++)
        {
            for (int u = 0; u < size; u++)
            {
                int typeId = _mask[u + v * size];
                if (typeId == 0)
                {
                    continue;
                }

                int width  = ExpandWidth(u, v, typeId);
                int height = ExpandHeight(u, v, typeId, width);

                // For negative-sign axes the quad corner starts at the far edge of the rectangle
                // so that du/dv point in the correct winding direction.
                _localCoords[normalAxis] = depth + planeOffset;
                _localCoords[uAxis] = uSign > 0 ? u : u + width;
                _localCoords[vAxis] = vSign > 0 ? v : v + height;
                var quadOrigin = new Vector3(ox + _localCoords[0], oy + _localCoords[1], oz + _localCoords[2]);
                var du = SetAxis(Vector3.Zero, uAxis, uSign * width);
                var dv = SetAxis(Vector3.Zero, vAxis, vSign * height);

                vertexCount += EmitQuad(output, offset + vertexCount, quadOrigin, du, dv, (uint)typeId);
                ClearMask(u, v, width, height);
            }
        }

        return vertexCount;
    }

    // Expands rightward along u while cells match typeId. Returns width ≥ 1.
    private int ExpandWidth(int u, int v, int typeId)
    {
        int size = Chunk.SIZE;
        int width = 1;
        while (u + width < size && _mask[(u + width) + v * size] == typeId)
        {
            width++;
        }
        return width;
    }

    // Expands downward along v while every cell in the row [u, u+width) matches typeId. Returns height ≥ 1.
    private int ExpandHeight(int u, int v, int typeId, int width)
    {
        int size = Chunk.SIZE;
        int height = 1;
        while (v + height < size)
        {
            for (int k = 0; k < width; k++)
            {
                if (_mask[(u + k) + (v + height) * size] != typeId)
                {
                    return height;
                }
            }
            height++;
        }
        return height;
    }

    // Emits a quad as two CCW triangles: (q0,q1,q2),(q0,q2,q3).
    // quadOrigin is the origin corner; du and dv are the scaled edge vectors.
    private static int EmitQuad(Span<VoxelVertex> output, int offset, Vector3 quadOrigin, Vector3 du, Vector3 dv, uint typeId)
    {
        var q0 = quadOrigin;
        var q1 = quadOrigin + du;
        var q2 = quadOrigin + du + dv;
        var q3 = quadOrigin + dv;
        output[offset + 0] = new VoxelVertex(q0, typeId);
        output[offset + 1] = new VoxelVertex(q1, typeId);
        output[offset + 2] = new VoxelVertex(q2, typeId);
        output[offset + 3] = new VoxelVertex(q0, typeId);
        output[offset + 4] = new VoxelVertex(q2, typeId);
        output[offset + 5] = new VoxelVertex(q3, typeId);
        return 6;
    }

    // Zeroes the width×height rectangle at (u,v) in _mask so those cells are not re-processed.
    private void ClearMask(int u, int v, int width, int height)
    {
        int size = Chunk.SIZE;
        for (int dv = 0; dv < height; dv++)
        {
            for (int du = 0; du < width; du++)
            {
                _mask[(u + du) + (v + dv) * size] = 0;
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
