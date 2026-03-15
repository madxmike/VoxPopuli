namespace VoxPopuli.Game;

/// <summary>
/// A zero-allocation view over a chunk's slice of VoxelWorld's flat voxel array.
/// Holds no data — all reads and writes go directly to the backing array.
/// </summary>
public ref struct Chunk
{
    public const int SIZE   = 32;
    public const int VOLUME = SIZE * SIZE * SIZE; // 32768

    private readonly Span<byte> _slice;

    internal Chunk(Span<byte> slice) => _slice = slice;

    public static implicit operator Chunk(Span<byte> slice) => new(slice);

    public static int Index(int lx, int ly, int lz) => lx + ly * SIZE + lz * SIZE * SIZE;

    public byte Get(int lx, int ly, int lz) => _slice[Index(lx, ly, lz)];
    public void Set(int lx, int ly, int lz, byte type) => _slice[Index(lx, ly, lz)] = type;

    public ReadOnlySpan<byte> Data  => _slice;
    internal Span<byte> MutableData => _slice;
}
