namespace VoxPopuli.Renderer;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using VoxPopuli.Game;

/// <summary>
/// SDL3-backed <see cref="IRenderer"/>. Translates safe game-side calls into
/// SDL GPU command buffers, transfer uploads, and render passes.
/// All unsafe code is confined to this class and <see cref="SdlGpuDevice"/>.
/// </summary>
internal sealed unsafe class SdlRenderer : IRenderer
{
    private readonly SdlGpuDevice _gpu;
    private readonly Dictionary<int, MeshBuffer> _meshes = new();
    private int _nextId;
    private Matrix4x4 _proj;
    private uint _projW, _projH;

    internal SdlRenderer(string title, int width, int height)
    {
        _gpu = new SdlGpuDevice(title, width, height);
    }

    public MeshHandle UploadMesh(ReadOnlySpan<Vertex> vertices)
    {
        uint size = (uint)(vertices.Length * Unsafe.SizeOf<Vertex>());

        var bufInfo = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX,
            size = size
        };
        var gpuBuffer = SDL3.SDL_CreateGPUBuffer(_gpu.Device, &bufInfo);

        var xferInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size = size
        };
        var xfer = SDL3.SDL_CreateGPUTransferBuffer(_gpu.Device, &xferInfo);

        var map = SDL3.SDL_MapGPUTransferBuffer(_gpu.Device, xfer, false);
        fixed (byte* src = MemoryMarshal.AsBytes(vertices))
            Buffer.MemoryCopy(src, (void*)map, size, size);
        SDL3.SDL_UnmapGPUTransferBuffer(_gpu.Device, xfer);

        var cmd = _gpu.AcquireCommandBuffer();
        var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
        var xferSrc = new SDL_GPUTransferBufferLocation { transfer_buffer = xfer, offset = 0 };
        var dst = new SDL_GPUBufferRegion { buffer = gpuBuffer, offset = 0, size = size };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &xferSrc, &dst, false);
        SDL3.SDL_EndGPUCopyPass(copyPass);
        SDL3.SDL_SubmitGPUCommandBuffer(cmd);
        SDL3.SDL_ReleaseGPUTransferBuffer(_gpu.Device, xfer);

        var handle = new MeshHandle(++_nextId);
        _meshes[handle.Id] = new MeshBuffer(gpuBuffer, vertices.Length);
        return handle;
    }

    public void ReleaseMesh(MeshHandle handle)
    {
        if (_meshes.Remove(handle.Id, out var buf))
            buf.Dispose(_gpu.Device);
    }

    public void DrawFrame(ReadOnlySpan<MeshInstance> instances, CameraView view)
    {
        var viewMatrix = Matrix4x4.CreateLookAt(view.Eye, view.Target, view.Up);
        var cmd = _gpu.AcquireCommandBuffer();
        SDL_GPUTexture* swapchain; uint sw, sh;
        SDL3.SDL_WaitAndAcquireGPUSwapchainTexture(cmd, _gpu.Window, &swapchain, &sw, &sh);
        // Swapchain texture can be null when the window is minimised or occluded.
        if (swapchain == null) { SDL3.SDL_CancelGPUCommandBuffer(cmd); return; }

        _gpu.ResizeDepthTextureIfNeeded(sw, sh);

        if (sw != _projW || sh != _projH)
        {
            _proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, (float)sw / sh, 0.01f, 100f);
            _projW = sw; _projH = sh;
        }

        var colorTarget = new SDL_GPUColorTargetInfo
        {
            texture = swapchain,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            clear_color = new SDL_FColor { r = 0f, g = 0f, b = 0f, a = 1f }
        };
        var depthTarget = new SDL_GPUDepthStencilTargetInfo
        {
            texture = _gpu.DepthTexture,
            clear_depth = 1.0f,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            // Depth values don't need to be read back after the pass.
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            cycle = true
        };

        var pass = SDL3.SDL_BeginGPURenderPass(cmd, &colorTarget, 1, &depthTarget);
        SDL3.SDL_BindGPUGraphicsPipeline(pass, _gpu.Pipeline);

        foreach (var instance in instances)
        {
            if (!_meshes.TryGetValue(instance.Mesh.Id, out var buf)) continue;
            var trs = Matrix4x4.CreateScale(instance.Transform.Scale)
                    * Matrix4x4.CreateFromQuaternion(instance.Transform.Rotation)
                    * Matrix4x4.CreateTranslation(instance.Transform.Position);
            var mvp = trs * viewMatrix * _proj;
            SDL3.SDL_PushGPUVertexUniformData(cmd, 0, (nint)Unsafe.AsPointer(ref mvp), 64);
            var vbBinding = new SDL_GPUBufferBinding { buffer = buf.GpuBuffer, offset = 0 };
            SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vbBinding, 1);
            SDL3.SDL_DrawGPUPrimitives(pass, (uint)buf.VertexCount, 1, 0, 0);
        }

        SDL3.SDL_EndGPURenderPass(pass);
        SDL3.SDL_SubmitGPUCommandBuffer(cmd);
    }

    public void Dispose()
    {
        foreach (var buf in _meshes.Values)
            buf.Dispose(_gpu.Device);
        _meshes.Clear();
        _gpu.Dispose();
    }

    // ── Nested private type ───────────────────────────────────────────────────

    /// <summary>
    /// Wraps a single GPU vertex buffer. Lifetime is managed by <see cref="SdlRenderer"/>;
    /// disposal requires the device pointer because SDL buffers aren't self-releasing.
    /// </summary>
    private struct MeshBuffer
    {
        internal SDL_GPUBuffer* GpuBuffer { get; }
        internal int VertexCount { get; }

        internal MeshBuffer(SDL_GPUBuffer* gpuBuffer, int vertexCount)
        {
            GpuBuffer = gpuBuffer;
            VertexCount = vertexCount;
        }

        internal void Dispose(SDL_GPUDevice* device) =>
            SDL3.SDL_ReleaseGPUBuffer(device, GpuBuffer);
    }
}
