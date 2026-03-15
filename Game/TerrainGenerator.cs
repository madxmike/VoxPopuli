namespace VoxPopuli.Game;

internal static class TerrainGenerator
{
    private static byte BiomeType(int surfaceY) => surfaceY switch
    {
        <= 8  => 5,
        <= 18 => 3,
        <= 32 => 2,
        <= 42 => 7,
        _     => 8,
    };

    internal static void GenerateChunk(int cx, int cy, int cz, int seed, Chunk chunk)
    {
        int wyBase = cy * Chunk.SIZE;

        for (int lz = 0; lz < Chunk.SIZE; lz++)
        for (int lx = 0; lx < Chunk.SIZE; lx++)
        {
            int wx = cx * Chunk.SIZE + lx;
            int wz = cz * Chunk.SIZE + lz;

            float n = SimplexNoise.Sample(wx * 0.004f + seed, wz * 0.004f + seed)
                    + SimplexNoise.Sample(wx * 0.016f + seed, wz * 0.016f + seed) * 0.3f;

            int surfaceY = (int)((n + 1.3f) / 2.6f * 48f) + 4;
            surfaceY = Math.Clamp(surfaceY, 1, 63);

            byte surface = BiomeType(surfaceY);

            for (int ly = 0; ly < Chunk.SIZE; ly++)
            {
                int wy = wyBase + ly;
                byte type;
                if (wy < surfaceY)       type = 5;
                else if (wy == surfaceY) type = surface;
                else                     continue;

                chunk.Set(lx, ly, lz, type);
            }
        }
    }

    internal static void GenerateWorld(VoxelWorld world, int seed)
    {
        for (int i = 0; i < VoxelWorld.MAX_CHUNKS; i++)
        {
            int cx = i % 16;
            int cy = (i / 16) % 2;
            int cz = i / 32;

            world.Chunks[i].MutableData.Clear();
            GenerateChunk(cx, cy, cz, seed, world.Chunks[i]);
        }
    }
}
