namespace VoxPopuli.Renderer;

using System;
using System.Numerics;
using SDL;
using VoxPopuli.Game;

internal sealed unsafe class VoxelChunkRenderer : ISubRenderer
{
    private readonly SdlGpuDevice _gpu;
    private readonly IChunkMeshBuilder _meshBuilder;

    private readonly SDL_GPUBuffer*[] _vertexBuffers = new SDL_GPUBuffer*[VoxelWorld.MAX_CHUNKS];
    private readonly uint[] _vertexCounts = new uint[VoxelWorld.MAX_CHUNKS];
    private readonly VoxelVertex[] _meshScratch = new VoxelVertex[VoxelVertex.MaxVerticesPerChunk];

    private SDL_GPUBuffer* _colorTableBuffer;
    private SDL_GPUTransferBuffer* _transferBuffer;
    private SDL_GPUGraphicsPipeline* _pipeline;
    private SDL_GPUGraphicsPipeline* _wireframePipeline;
    private bool _initialized;

    internal VoxelChunkRenderer(SdlGpuDevice gpu, ReadOnlySpan<Vector4> colorTable, IChunkMeshBuilder meshBuilder)
    {
        _gpu = gpu;
        _meshBuilder = meshBuilder;

        // Upload color table (always 256 entries = 4096 bytes) to a GRAPHICS_STORAGE_READ buffer
        const uint colorTableSize = 256 * 4 * sizeof(float); // 256 × Vector4 = 4096 bytes

        var colorBufInfo = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_GRAPHICS_STORAGE_READ,
            size  = colorTableSize
        };
        _colorTableBuffer = SDL3.SDL_CreateGPUBuffer(gpu.Device, &colorBufInfo);
        if (_colorTableBuffer == null)
            throw new Exception(SDL3.SDL_GetError());

        // Upload color table via one-shot copy pass with a temp transfer buffer
        var tempTransferInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size  = colorTableSize
        };
        var tempTransfer = SDL3.SDL_CreateGPUTransferBuffer(gpu.Device, &tempTransferInfo);
        if (tempTransfer == null)
            throw new Exception(SDL3.SDL_GetError());

        var mapped = (Vector4*)SDL3.SDL_MapGPUTransferBuffer(gpu.Device, tempTransfer, false);
        // Copy input span (may be shorter than 256); zero-pad the rest
        int copyCount = Math.Min(colorTable.Length, 256);
        for (int i = 0; i < copyCount; i++)
            mapped[i] = colorTable[i];
        for (int i = copyCount; i < 256; i++)
            mapped[i] = Vector4.Zero;
        SDL3.SDL_UnmapGPUTransferBuffer(gpu.Device, tempTransfer);

        var uploadCmd = gpu.AcquireCommandBuffer();
        var copyPass = SDL3.SDL_BeginGPUCopyPass(uploadCmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = tempTransfer, offset = 0 };
        var dst = new SDL_GPUBufferRegion { buffer = _colorTableBuffer, offset = 0, size = colorTableSize };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &src, &dst, false);
        SDL3.SDL_EndGPUCopyPass(copyPass);
        SDL3.SDL_SubmitGPUCommandBuffer(uploadCmd);
        SDL3.SDL_ReleaseGPUTransferBuffer(gpu.Device, tempTransfer);

        // Create reusable transfer buffer for per-chunk uploads
        var transferInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size  = (uint)(VoxelVertex.MaxVerticesPerChunk * 16)
        };
        _transferBuffer = SDL3.SDL_CreateGPUTransferBuffer(gpu.Device, &transferInfo);
        if (_transferBuffer == null)
            throw new Exception(SDL3.SDL_GetError());

        // Load shaders: vert needs 1 uniform buffer (ViewProj) + 1 storage buffer (colorTable)
        var vert = gpu.LoadShaderInternal("Voxel.vert", numUniformBuffers: 1, numReadOnlyStorageBuffers: 1);
        if (vert == null) throw new Exception(SDL3.SDL_GetError());
        var frag = gpu.LoadShaderInternal("Voxel.frag", numUniformBuffers: 0, numReadOnlyStorageBuffers: 0);
        if (frag == null) throw new Exception(SDL3.SDL_GetError());

        // Create graphics pipeline
        var bufDesc = new SDL_GPUVertexBufferDescription
        {
            slot               = 0,
            pitch              = 16,
            input_rate         = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX,
            instance_step_rate = 0
        };
        var attrs = stackalloc SDL_GPUVertexAttribute[2];
        attrs[0] = new SDL_GPUVertexAttribute
        {
            location    = 0,
            buffer_slot = 0,
            format      = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3,
            offset      = 0
        };
        attrs[1] = new SDL_GPUVertexAttribute
        {
            location    = 1,
            buffer_slot = 0,
            format      = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT,
            offset      = 12
        };

        var swapchainFormat = SDL3.SDL_GetGPUSwapchainTextureFormat(gpu.Device, gpu.Window);
        var colorTargetDesc = new SDL_GPUColorTargetDescription { format = swapchainFormat };

        var pipelineInfo = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader   = vert,
            fragment_shader = frag,
            vertex_input_state = new SDL_GPUVertexInputState
            {
                vertex_buffer_descriptions = &bufDesc,
                num_vertex_buffers         = 1,
                vertex_attributes          = attrs,
                num_vertex_attributes      = 2
            },
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState
            {
                cull_mode  = SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK,
                front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
                fill_mode  = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL
            },
            depth_stencil_state = new SDL_GPUDepthStencilState
            {
                enable_depth_test  = true,
                enable_depth_write = true,
                compare_op         = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS
            },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo
            {
                color_target_descriptions = &colorTargetDesc,
                num_color_targets         = 1,
                depth_stencil_format      = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT,
                has_depth_stencil_target  = true
            }
        };

        _pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(gpu.Device, &pipelineInfo);
        if (_pipeline == null)
            throw new Exception(SDL3.SDL_GetError());

        pipelineInfo.rasterizer_state.fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_LINE;
        _wireframePipeline = SDL3.SDL_CreateGPUGraphicsPipeline(gpu.Device, &pipelineInfo);
        if (_wireframePipeline == null)
            throw new Exception(SDL3.SDL_GetError());

        SDL3.SDL_ReleaseGPUShader(gpu.Device, vert);
        SDL3.SDL_ReleaseGPUShader(gpu.Device, frag);
    }

    public unsafe void PrepareFrame(SDL_GPUCommandBuffer* cmd, in RenderFrame frame)
    {
        if (frame.World == null) return;

        if (!_initialized)
        {
            for (int i = 0; i < VoxelWorld.MAX_CHUNKS; i++)
                BuildAndUploadChunk(cmd, frame.World, i);
            _initialized = true;
            LogFaceCount();
        }

        bool dirty = false;
        foreach (int i in frame.World.DrainDirtyChunks())
        {
            BuildAndUploadChunk(cmd, frame.World, i);
            dirty = true;
        }
        if (dirty) LogFaceCount();
    }

    private void LogFaceCount()
    {
        uint totalVerts = 0;
        foreach (uint v in _vertexCounts) totalVerts += v;
        Console.WriteLine($"[VoxelChunkRenderer] {totalVerts / 3} triangles ({totalVerts / 6} quads)");
    }

    private void BuildAndUploadChunk(SDL_GPUCommandBuffer* cmd, VoxelWorld world, int i)
    {
        int count = _meshBuilder.Build(world, i, _meshScratch);

        if (count == 0)
        {
            if (_vertexBuffers[i] != null)
            {
                SDL3.SDL_ReleaseGPUBuffer(_gpu.Device, _vertexBuffers[i]);
                _vertexBuffers[i] = null;
            }
            _vertexCounts[i] = 0;
            return;
        }

        // Release old buffer and create new one sized to this chunk's vertex count
        if (_vertexBuffers[i] != null)
            SDL3.SDL_ReleaseGPUBuffer(_gpu.Device, _vertexBuffers[i]);

        var bufInfo = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX,
            size  = (uint)(count * 16)
        };
        _vertexBuffers[i] = SDL3.SDL_CreateGPUBuffer(_gpu.Device, &bufInfo);
        if (_vertexBuffers[i] == null)
            throw new Exception(SDL3.SDL_GetError());

        // Upload via reusable transfer buffer with cycle=true
        uint byteSize = (uint)(count * 16);
        var mapped = (VoxelVertex*)SDL3.SDL_MapGPUTransferBuffer(_gpu.Device, _transferBuffer, true);
        _meshScratch.AsSpan(0, count).CopyTo(new Span<VoxelVertex>(mapped, count));
        SDL3.SDL_UnmapGPUTransferBuffer(_gpu.Device, _transferBuffer);

        var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = _transferBuffer, offset = 0 };
        var dst = new SDL_GPUBufferRegion { buffer = _vertexBuffers[i], offset = 0, size = byteSize };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &src, &dst, true);
        SDL3.SDL_EndGPUCopyPass(copyPass);

        _vertexCounts[i] = (uint)count;
    }

    public void Draw(in RenderFrame frame)
    {
        var pass = frame.RenderPass;
        var cmd  = frame.CommandBuffer;

        // Push ViewProj once — persists for the entire command buffer
        var viewProj = frame.ViewProj;
        SDL3.SDL_PushGPUVertexUniformData(cmd, 0, (IntPtr)(&viewProj), (uint)sizeof(Matrix4x4));

        SDL3.SDL_BindGPUGraphicsPipeline(pass, frame.Wireframe ? _wireframePipeline : _pipeline);

        var colorBuf = _colorTableBuffer;
        SDL3.SDL_BindGPUVertexStorageBuffers(pass, 0, &colorBuf, 1);

        for (int i = 0; i < VoxelWorld.MAX_CHUNKS; i++)
        {
            if (_vertexCounts[i] == 0) continue;

            var binding = new SDL_GPUBufferBinding { buffer = _vertexBuffers[i], offset = 0 };
            SDL3.SDL_BindGPUVertexBuffers(pass, 0, &binding, 1);
            SDL3.SDL_DrawGPUPrimitives(pass, _vertexCounts[i], 1, 0, 0);
        }
    }

    public void Dispose()
    {
        for (int i = 0; i < VoxelWorld.MAX_CHUNKS; i++)
        {
            if (_vertexBuffers[i] != null)
                SDL3.SDL_ReleaseGPUBuffer(_gpu.Device, _vertexBuffers[i]);
        }
        if (_colorTableBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_gpu.Device, _colorTableBuffer);
        if (_transferBuffer   != null) SDL3.SDL_ReleaseGPUTransferBuffer(_gpu.Device, _transferBuffer);
        if (_pipeline         != null) SDL3.SDL_ReleaseGPUGraphicsPipeline(_gpu.Device, _pipeline);
        if (_wireframePipeline != null) SDL3.SDL_ReleaseGPUGraphicsPipeline(_gpu.Device, _wireframePipeline);
    }
}
