namespace VoxPopuli.Game;

public sealed class Chunk
{
    public const int SIZE = 32;
    public const int VOLUME = SIZE * SIZE * SIZE; // 32768

    private readonly byte[] _voxels = new byte[VOLUME];

    public static int Index(int lx, int ly, int lz) => lx + ly * SIZE + lz * SIZE * SIZE;

    public byte Get(int lx, int ly, int lz) => _voxels[Index(lx, ly, lz)];
    public void Set(int lx, int ly, int lz, byte type) => _voxels[Index(lx, ly, lz)] = type;

    public ReadOnlySpan<byte> Data => _voxels;
    internal Span<byte> MutableData => _voxels;
}
