namespace VoxPopuli.Renderer;

using System;
using System.Threading;
using VoxPopuli.Game;

// Builds a triangle-list mesh for a single chunk.
// Returns the number of vertices written into 'output', or 0 if the chunk is empty.
internal interface IChunkMeshBuilder
{
    int Build(VoxelWorld world, int chunkIndex, Span<VoxelVertex> output, CancellationToken ct);
}
