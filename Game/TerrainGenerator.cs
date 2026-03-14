namespace VoxPopuli.Game;

internal static class TerrainGenerator
{
    private const int PADDED = 34;
    private const int STRIDE_Y = PADDED;
    private const int STRIDE_Z = PADDED * PADDED; // 1156

    private static byte BiomeType(int surfaceY) => surfaceY switch
    {
        <= 8  => 5, // cyan   — stone/deep
        <= 18 => 3, // blue   — dirt
        <= 32 => 2, // green  — grass
        <= 42 => 7, // orange — rock
        _     => 8, // grey   — snow
    };

    internal static void GenerateChunk(int cx, int cy, int cz, int seed, Span<byte> padded)
    {
        int wyBase = cy * 32;

        for (int lz = 0; lz < 32; lz++)
        for (int lx = 0; lx < 32; lx++)
        {
            int wx = cx * 32 + lx;
            int wz = cz * 32 + lz;

            float n = SimplexNoise.Sample(wx * 0.004f + seed, wz * 0.004f + seed)
                    + SimplexNoise.Sample(wx * 0.016f + seed, wz * 0.016f + seed) * 0.3f;

            int surfaceY = (int)((n + 1.3f) / 2.6f * 48f) + 4;
            surfaceY = Math.Clamp(surfaceY, 1, 63);

            byte surface = BiomeType(surfaceY);

            for (int ly = 0; ly < 32; ly++)
            {
                int wy = wyBase + ly;
                byte type;
                if (wy < surfaceY)       type = 5;       // stone
                else if (wy == surfaceY) type = surface;
                else                     continue;        // air — leave 0

                padded[(lx + 1) + (ly + 1) * STRIDE_Y + (lz + 1) * STRIDE_Z] = type;
            }
        }
    }

    internal static void GenerateWorld(VoxelWorld world, int seed)
    {
        var buf = new byte[PADDED * PADDED * PADDED];

        for (int i = 0; i < 512; i++)
        {
            int cx = i % 16;
            int cy = (i / 16) % 2;
            int cz = i / 32;

            Array.Clear(buf);
            GenerateChunk(cx, cy, cz, seed, buf);
            world.SetChunkVoxels(i, buf);
        }
    }
}
