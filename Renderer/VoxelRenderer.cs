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
    private SDL_GPUBuffer* _chunkOffsetBuffer;

    internal VoxelRenderer(SdlGpuDevice gpu, ChunkMeshPool pool)
    {
        _gpu = gpu;
        _pool = pool;

        var bufInfo = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_GRAPHICS_STORAGE_READ,
            size  = (uint)(MaxChunks * 16)
        };
        _chunkOffsetBuffer = SDL3.SDL_CreateGPUBuffer(gpu.Device, &bufInfo);

        _pipeline = CreateVoxelPipeline();
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
            slot = 0,
            pitch = 8,
            input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX,
            instance_step_rate = 0
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
                vertex_shader   = vert,
                fragment_shader = frag,
                vertex_input_state = new SDL_GPUVertexInputState
                {
                    num_vertex_buffers        = 1,
                    vertex_buffer_descriptions = &vertexBufferDesc,
                    num_vertex_attributes     = 2,
                    vertex_attributes         = attribsPtr
                },
                depth_stencil_state = new SDL_GPUDepthStencilState
                {
                    enable_depth_test  = true,
                    enable_depth_write = true,
                    compare_op         = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL
                },
                target_info = new SDL_GPUGraphicsPipelineTargetInfo
                {
                    num_color_targets          = 1,
                    color_target_descriptions  = &colorTargetDesc,
                    depth_stencil_format       = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D16_UNORM,
                    has_depth_stencil_target   = true
                },
                primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST
            };
            pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(_gpu.Device, &createInfo);
        }

        SDL3.SDL_ReleaseGPUShader(_gpu.Device, vert);
        SDL3.SDL_ReleaseGPUShader(_gpu.Device, frag);
        return pipeline;
    }

    internal void Render(
        SDL_GPUCommandBuffer* cmd,
        SDL_GPUTexture*       swapchain,
        uint w, uint h,
        int chunkSlot, Vector3 chunkOrigin, CameraView camera)
    {
        // 1. Upload chunk origin
        var entry = new ChunkOffsetEntry { Origin = chunkOrigin, _pad = 0f };
        uint entrySize = (uint)Unsafe.SizeOf<ChunkOffsetEntry>();
        var xferInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size  = entrySize
        };
        var xfer = SDL3.SDL_CreateGPUTransferBuffer(_gpu.Device, &xferInfo);
        var map  = SDL3.SDL_MapGPUTransferBuffer(_gpu.Device, xfer, false);
        Buffer.MemoryCopy(Unsafe.AsPointer(ref entry), (void*)map, entrySize, entrySize);
        SDL3.SDL_UnmapGPUTransferBuffer(_gpu.Device, xfer);

        var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
        var xferSrc  = new SDL_GPUTransferBufferLocation { transfer_buffer = xfer, offset = 0 };
        var dst      = new SDL_GPUBufferRegion { buffer = _chunkOffsetBuffer, offset = 0, size = entrySize };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &xferSrc, &dst, false);
        SDL3.SDL_EndGPUCopyPass(copyPass);
        SDL3.SDL_ReleaseGPUTransferBuffer(_gpu.Device, xfer);

        // 2. Resize depth if needed
        _gpu.ResizeDepthTextureIfNeeded(w, h);

        // 3. ViewProj
        var view = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, camera.Up);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, (float)w / h, 0.1f, 2000f);
        var viewProj = view * proj;

        // 4. Render pass
        var colorTarget = new SDL_GPUColorTargetInfo
        {
            texture     = swapchain,
            load_op     = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op    = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            clear_color = new SDL_FColor { r = 0.1f, g = 0.1f, b = 0.1f, a = 1f }
        };
        var depthTarget = new SDL_GPUDepthStencilTargetInfo
        {
            texture             = _gpu.DepthTexture,
            clear_depth         = 1.0f,
            load_op             = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op            = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            stencil_load_op     = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            stencil_store_op    = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            cycle               = true
        };

        var pass = SDL3.SDL_BeginGPURenderPass(cmd, &colorTarget, 1, &depthTarget);
        SDL3.SDL_BindGPUGraphicsPipeline(pass, _pipeline);

        var vbBinding = new SDL_GPUBufferBinding { buffer = _pool.VertexBuffer, offset = 0 };
        SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vbBinding, 1);

        var ibBinding = new SDL_GPUBufferBinding { buffer = _pool.IndexBuffer, offset = 0 };
        SDL3.SDL_BindGPUIndexBuffer(pass, &ibBinding, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);

        var buf = _chunkOffsetBuffer;
        SDL3.SDL_BindGPUVertexStorageBuffers(pass, 0, &buf, 1);

        SDL3.SDL_PushGPUVertexUniformData(cmd, 0, (nint)Unsafe.AsPointer(ref viewProj), 64);

        SDL3.SDL_DrawGPUIndexedPrimitives(
            pass,
            (uint)_pool.GetIndexCount(chunkSlot),
            1,
            (uint)_pool.GetFirstIndex(chunkSlot),
            _pool.GetVertexOffset(chunkSlot),
            0);

        SDL3.SDL_EndGPURenderPass(pass);
    }

    public void Dispose()
    {
        SDL3.SDL_ReleaseGPUGraphicsPipeline(_gpu.Device, _pipeline);
        SDL3.SDL_ReleaseGPUBuffer(_gpu.Device, _chunkOffsetBuffer);
    }
}
