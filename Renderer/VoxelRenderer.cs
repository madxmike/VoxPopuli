namespace VoxPopuli.Renderer;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using VoxPopuli.Game;

internal sealed unsafe class VoxelRenderer : IDisposable
{
    private const int MaxChunks = 1024;

    [StructLayout(LayoutKind.Sequential)]
    private struct ChunkOffsetEntry { public Vector3 Origin; public float _pad; }

    private readonly SdlGpuDevice _gpu;
    private readonly ChunkMeshPool _pool;
    private SDL_GPUGraphicsPipeline* _pipeline;
    private SDL_GPUBuffer* _chunkOffsetBuffer;   // GRAPHICS_STORAGE_READ, MaxChunks * 16
    private SDL_GPUBuffer* _indirectDrawBuffer;  // INDIRECT, MaxChunks * 20
    private readonly int[] _chunkSlot = new int[MaxChunks];
    private readonly VoxelDrawCommand[] _drawCommands = new VoxelDrawCommand[MaxChunks];
    private readonly Vector4[] _frustumPlanes = new Vector4[6];

    internal VoxelRenderer(SdlGpuDevice gpu, ChunkMeshPool pool)
    {
        _gpu  = gpu;
        _pool = pool;
        Array.Fill(_chunkSlot, -1);

        var offsetBufInfo = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_GRAPHICS_STORAGE_READ,
            size  = (uint)(MaxChunks * 16)
        };
        _chunkOffsetBuffer = SDL3.SDL_CreateGPUBuffer(gpu.Device, &offsetBufInfo);

        var indirectBufInfo = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDIRECT,
            size  = (uint)(MaxChunks * 20)
        };
        _indirectDrawBuffer = SDL3.SDL_CreateGPUBuffer(gpu.Device, &indirectBufInfo);

        _pipeline = CreateVoxelPipeline();
    }

    internal void AssignSlot(int chunkIndex, int slot) => _chunkSlot[chunkIndex] = slot;

    // Upload all 1024 chunk world origins to the storage buffer in one transfer.
    internal void UploadAllChunkOffsets(SDL_GPUCommandBuffer* cmd, VoxelWorld world)
    {
        uint size = (uint)(MaxChunks * 16);
        var xferInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size  = size
        };
        var xfer = SDL3.SDL_CreateGPUTransferBuffer(_gpu.Device, &xferInfo);
        var map  = (ChunkOffsetEntry*)SDL3.SDL_MapGPUTransferBuffer(_gpu.Device, xfer, false);
        for (int i = 0; i < MaxChunks; i++)
            map[i] = new ChunkOffsetEntry { Origin = VoxelWorld.ChunkOrigin(i), _pad = 0f };
        SDL3.SDL_UnmapGPUTransferBuffer(_gpu.Device, xfer);

        var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = xfer, offset = 0 };
        var dst = new SDL_GPUBufferRegion { buffer = _chunkOffsetBuffer, offset = 0, size = size };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &src, &dst, false);
        SDL3.SDL_EndGPUCopyPass(copyPass);
        SDL3.SDL_ReleaseGPUTransferBuffer(_gpu.Device, xfer);
    }

    internal void Render(
        SDL_GPUCommandBuffer* cmd,
        SDL_GPUTexture*       swapchain,
        uint w, uint h,
        VoxelWorld world,
        CameraView camera)
    {
        // 1. ViewProj
        var view     = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up);
        var proj     = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, (float)w / h, 0.1f, 2000f);
        var viewProj = view * proj;

        // 2. Frustum cull
        Frustum.Extract(viewProj, _frustumPlanes);
        int visibleCount = 0;
        for (int i = 0; i < MaxChunks; i++)
        {
            int slot = _chunkSlot[i];
            if (slot == -1) continue;
            if (Frustum.Cull(_frustumPlanes, world.ChunkAabbMin[i], world.ChunkAabbMax[i])) continue;
            _drawCommands[visibleCount++] = new VoxelDrawCommand
            {
                NumIndices    = (uint)_pool.GetIndexCount(slot),
                NumInstances  = 1,
                FirstIndex    = (uint)_pool.GetFirstIndex(slot),
                VertexOffset  = _pool.GetVertexOffset(slot),
                FirstInstance = (uint)i
            };
        }

        // 3. Upload indirect buffer
        if (visibleCount > 0)
        {
            uint indirectSize = (uint)(visibleCount * 20);
            var xferInfo = new SDL_GPUTransferBufferCreateInfo
            {
                usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                size  = indirectSize
            };
            var xfer = SDL3.SDL_CreateGPUTransferBuffer(_gpu.Device, &xferInfo);
            var map  = SDL3.SDL_MapGPUTransferBuffer(_gpu.Device, xfer, false);
            fixed (VoxelDrawCommand* cmdsPtr = _drawCommands)
                Buffer.MemoryCopy(cmdsPtr, (void*)map, indirectSize, indirectSize);
            SDL3.SDL_UnmapGPUTransferBuffer(_gpu.Device, xfer);

            var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
            var src = new SDL_GPUTransferBufferLocation { transfer_buffer = xfer, offset = 0 };
            var dst = new SDL_GPUBufferRegion { buffer = _indirectDrawBuffer, offset = 0, size = indirectSize };
            SDL3.SDL_UploadToGPUBuffer(copyPass, &src, &dst, false);
            SDL3.SDL_EndGPUCopyPass(copyPass);
            SDL3.SDL_ReleaseGPUTransferBuffer(_gpu.Device, xfer);
        }

        // 4. Render pass
        _gpu.ResizeDepthTextureIfNeeded(w, h);

        var colorTarget = new SDL_GPUColorTargetInfo
        {
            texture     = swapchain,
            load_op     = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op    = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            clear_color = new SDL_FColor { r = 0.1f, g = 0.1f, b = 0.1f, a = 1f }
        };
        var depthTarget = new SDL_GPUDepthStencilTargetInfo
        {
            texture          = _gpu.DepthTexture,
            clear_depth      = 1.0f,
            load_op          = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op         = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            stencil_load_op  = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            cycle            = true
        };

        var pass = SDL3.SDL_BeginGPURenderPass(cmd, &colorTarget, 1, &depthTarget);
        SDL3.SDL_BindGPUGraphicsPipeline(pass, _pipeline);

        var vbBinding = new SDL_GPUBufferBinding { buffer = _pool.VertexBuffer, offset = 0 };
        SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vbBinding, 1);

        var ibBinding = new SDL_GPUBufferBinding { buffer = _pool.IndexBuffer, offset = 0 };
        SDL3.SDL_BindGPUIndexBuffer(pass, &ibBinding, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);

        var offsetBuf = _chunkOffsetBuffer;
        SDL3.SDL_BindGPUVertexStorageBuffers(pass, 0, &offsetBuf, 1);

        SDL3.SDL_PushGPUVertexUniformData(cmd, 0, (nint)Unsafe.AsPointer(ref viewProj), 64);

        if (visibleCount > 0)
            SDL3.SDL_DrawGPUIndexedPrimitivesIndirect(pass, _indirectDrawBuffer, 0, (uint)visibleCount);

        SDL3.SDL_EndGPURenderPass(pass);

        Console.WriteLine($"[Voxel] Visible chunks: {visibleCount}");
    }

    private SDL_GPUGraphicsPipeline* CreateVoxelPipeline()
    {
        var vert = _gpu.LoadShaderInternal("Voxel.vert", numUniformBuffers: 1);
        var frag = _gpu.LoadShaderInternal("Voxel.frag");

        var colorTargetDesc = new SDL_GPUColorTargetDescription
        {
            format = SDL3.SDL_GetGPUSwapchainTextureFormat(_gpu.Device, _gpu.Window)
        };
        var vertexBufferDesc = new SDL_GPUVertexBufferDescription
        {
            slot = 0, pitch = 8,
            input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX
        };
        var vertexAttributes = new SDL_GPUVertexAttribute[]
        {
            new() { location = 0, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4, offset = 0 },
            new() { location = 1, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT,   offset = 4 }
        };

        SDL_GPUGraphicsPipeline* pipeline;
        fixed (SDL_GPUVertexAttribute* attribsPtr = vertexAttributes)
        {
            var createInfo = new SDL_GPUGraphicsPipelineCreateInfo
            {
                vertex_shader = vert, fragment_shader = frag,
                vertex_input_state = new SDL_GPUVertexInputState
                {
                    num_vertex_buffers = 1, vertex_buffer_descriptions = &vertexBufferDesc,
                    num_vertex_attributes = 2, vertex_attributes = attribsPtr
                },
                depth_stencil_state = new SDL_GPUDepthStencilState
                {
                    enable_depth_test = true, enable_depth_write = true,
                    compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL
                },
                target_info = new SDL_GPUGraphicsPipelineTargetInfo
                {
                    num_color_targets = 1, color_target_descriptions = &colorTargetDesc,
                    depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D16_UNORM,
                    has_depth_stencil_target = true
                },
                primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST
            };
            pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(_gpu.Device, &createInfo);
        }
        SDL3.SDL_ReleaseGPUShader(_gpu.Device, vert);
        SDL3.SDL_ReleaseGPUShader(_gpu.Device, frag);
        return pipeline;
    }

    public void Dispose()
    {
        SDL3.SDL_ReleaseGPUGraphicsPipeline(_gpu.Device, _pipeline);
        SDL3.SDL_ReleaseGPUBuffer(_gpu.Device, _chunkOffsetBuffer);
        SDL3.SDL_ReleaseGPUBuffer(_gpu.Device, _indirectDrawBuffer);
    }
}
