namespace VoxPopuli.Game;

using System.Numerics;

/// <summary>Debug and utility operations on a <see cref="VoxelWorld"/> that are not part of its core interface.</summary>
internal static class VoxelWorldDebugTools
{
    /// <summary>
    /// Returns the index of the first non-empty chunk hit by the ray, or -1 if none.
    /// Uses slab-method AABB intersection, then sorts hits by distance and checks for non-air voxels.
    /// </summary>
    internal static int RaycastChunk(this VoxelWorld world, Vector3 origin, Vector3 dir)
    {
        Span<(float t, int i)> hits = stackalloc (float, int)[VoxelWorld.MAX_CHUNKS];
        int hitCount = 0;

        for (int i = 0; i < VoxelWorld.MAX_CHUNKS; i++)
        {
            // Precompute per-component reciprocal to turn divisions into multiplications
            Vector3 inverseDirection = new Vector3(1f / dir.X, 1f / dir.Y, 1f / dir.Z);

            // Slab intersection: compute entry/exit t values for each axis pair
            Vector3 tNear = (world.ChunkAabbMin[i] - origin) * inverseDirection;
            Vector3 tFar = (world.ChunkAabbMax[i] - origin) * inverseDirection;

            // tEntry is the latest entry across all axes; tExit is the earliest exit
            float tEntry = MathF.Max(MathF.Max(MathF.Min(tNear.X, tFar.X), MathF.Min(tNear.Y, tFar.Y)), MathF.Min(tNear.Z, tFar.Z));
            float tExit = MathF.Min(MathF.Min(MathF.Max(tNear.X, tFar.X), MathF.Max(tNear.Y, tFar.Y)), MathF.Max(tNear.Z, tFar.Z));

            // A valid hit requires the ray to enter before it exits, and the exit must be in front of the ray
            if (tExit >= 0 && tEntry <= tExit)
                hits[hitCount++] = (tEntry, i);
        }

        // Sort hits by distance (insertion sort — hitCount is small in practice)
        for (int i = 1; i < hitCount; i++)
        {
            var currentHit = hits[i];
            int j = i - 1;
            while (j >= 0 && hits[j].t > currentHit.t) { hits[j + 1] = hits[j]; j--; }
            hits[j + 1] = currentHit;
        }

        // Walk hits nearest-first and return the first chunk that contains any non-air voxel
        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            int chunkIndex = hits[hitIndex].i;
            var chunkVoxels = world._voxels.AsSpan(chunkIndex * Chunk.VOLUME, Chunk.VOLUME);
            foreach (byte voxelValue in chunkVoxels) if (voxelValue != 0) return chunkIndex;
        }
        return -1;
    }

    /// <summary>Clears the chunk whose centre is nearest to <paramref name="position"/> and marks it dirty.</summary>
    internal static void DeleteClosestChunk(this VoxelWorld world, Vector3 position)
    {
        int closestChunkIndex = 0;
        float minDistanceSquared = float.MaxValue;
        for (int i = 0; i < VoxelWorld.MAX_CHUNKS; i++)
        {
            float distanceSquared = Vector3.DistanceSquared(position, (world.ChunkAabbMin[i] + world.ChunkAabbMax[i]) * 0.5f);
            if (distanceSquared < minDistanceSquared) { minDistanceSquared = distanceSquared; closestChunkIndex = i; }
        }
        world.DeleteChunk(closestChunkIndex);
    }
}
