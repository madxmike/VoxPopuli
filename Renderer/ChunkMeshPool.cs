namespace VoxPopuli.Renderer;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;

/// <summary>
/// Manages a fixed pool of 1024 GPU vertex/index buffer slots for chunk meshes.
/// Both buffers are pre-allocated once; individual slots are carved out by byte offset.
/// </summary>
internal sealed unsafe class ChunkMeshPool : IDisposable
{
    internal const int MaxSlots        = 1024;
    internal const int VerticesPerSlot = 24576;
    internal const int IndicesPerSlot  = 36864;

    private readonly SDL_GPUDevice* _device;
    private readonly SDL_GPUBuffer* _vertexBuffer;
    private readonly SDL_GPUBuffer* _indexBuffer;
    private readonly Stack<int> _freeSlots;
    private readonly int[] _vertCounts = new int[MaxSlots];
    private readonly int[] _idxCounts  = new int[MaxSlots];

    internal ChunkMeshPool(SDL_GPUDevice* device)
    {
        _device = device;

        var vbInfo = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX,
            size  = (uint)(MaxSlots * VerticesPerSlot * 8)
        };
        _vertexBuffer = SDL3.SDL_CreateGPUBuffer(device, &vbInfo);

        var ibInfo = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX,
            size  = (uint)(MaxSlots * IndicesPerSlot * 4)
        };
        _indexBuffer = SDL3.SDL_CreateGPUBuffer(device, &ibInfo);

        _freeSlots = new Stack<int>(MaxSlots);
        for (int i = MaxSlots - 1; i >= 0; i--)
            _freeSlots.Push(i);
    }

    internal SDL_GPUBuffer* VertexBuffer => _vertexBuffer;
    internal SDL_GPUBuffer* IndexBuffer  => _indexBuffer;

    internal int AllocSlot() => _freeSlots.TryPop(out int slot) ? slot : -1;

    internal void FreeSlot(int slot) => _freeSlots.Push(slot);

    /// <summary>
    /// Uploads vertex and index data into the pool slot via a transfer buffer.
    /// The caller is responsible for submitting <paramref name="cmd"/>.
    /// </summary>
    internal void UploadMesh(SDL_GPUCommandBuffer* cmd, int slot,
        ReadOnlySpan<VoxelVertex> verts, ReadOnlySpan<uint> indices,
        int vertCount, int idxCount)
    {
        uint vertBytes = (uint)(vertCount * Unsafe.SizeOf<VoxelVertex>());
        uint idxBytes  = (uint)(idxCount  * sizeof(uint));
        uint totalSize = vertBytes + idxBytes;

        var xferInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size  = totalSize
        };
        var xfer = SDL3.SDL_CreateGPUTransferBuffer(_device, &xferInfo);

        var map = (byte*)SDL3.SDL_MapGPUTransferBuffer(_device, xfer, false);
        fixed (byte* vSrc = MemoryMarshal.AsBytes(verts))
            Buffer.MemoryCopy(vSrc, map, vertBytes, vertBytes);
        fixed (byte* iSrc = MemoryMarshal.AsBytes(indices))
            Buffer.MemoryCopy(iSrc, map + vertBytes, idxBytes, idxBytes);
        SDL3.SDL_UnmapGPUTransferBuffer(_device, xfer);

        uint vertSlotOffset = (uint)(slot * VerticesPerSlot * 8);
        uint idxSlotOffset  = (uint)(slot * IndicesPerSlot  * 4);

        var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);

        var vSrcLoc = new SDL_GPUTransferBufferLocation { transfer_buffer = xfer, offset = 0 };
        var vDst    = new SDL_GPUBufferRegion { buffer = _vertexBuffer, offset = vertSlotOffset, size = vertBytes };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &vSrcLoc, &vDst, false);

        var iSrcLoc = new SDL_GPUTransferBufferLocation { transfer_buffer = xfer, offset = vertBytes };
        var iDst    = new SDL_GPUBufferRegion { buffer = _indexBuffer, offset = idxSlotOffset, size = idxBytes };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &iSrcLoc, &iDst, false);

        SDL3.SDL_EndGPUCopyPass(copyPass);
        SDL3.SDL_ReleaseGPUTransferBuffer(_device, xfer);

        _vertCounts[slot] = vertCount;
        _idxCounts[slot]  = idxCount;
    }

    internal int GetVertexCount(int slot)  => _vertCounts[slot];
    internal int GetIndexCount(int slot)   => _idxCounts[slot];
    internal int GetFirstIndex(int slot)   => slot * IndicesPerSlot;
    internal int GetVertexOffset(int slot) => slot * VerticesPerSlot;

    public void Dispose()
    {
        SDL3.SDL_ReleaseGPUBuffer(_device, _vertexBuffer);
        SDL3.SDL_ReleaseGPUBuffer(_device, _indexBuffer);
    }
}
