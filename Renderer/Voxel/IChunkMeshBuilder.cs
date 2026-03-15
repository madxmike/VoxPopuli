namespace VoxPopuli.Renderer;

using System;
using System.Threading;
using VoxPopuli.Game;

/// <summary>Interface for building triangle meshes from voxel chunks.</summary>
internal interface IChunkMeshBuilder
{
    /// <summary>Builds a triangle-list mesh for a chunk and writes vertices to <paramref name="output"/>.</summary>
    /// <param name="world">The voxel world containing the chunk.</param>
    /// <param name="chunkIndex">Index of the chunk to mesh.</param>
    /// <param name="output">Span to write vertices into. Must be at least <see cref="VoxelVertex.MaxVerticesPerChunk"/> elements.</param>
    /// <param name="ct">Cancellation token to abort meshing if needed.</param>
    /// <returns>The number of vertices written, or 0 if the chunk is empty.</returns>
    int Build(VoxelWorld world, int chunkIndex, Span<VoxelVertex> output, CancellationToken ct);
}
