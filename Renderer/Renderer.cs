namespace VoxPopuli.Renderer;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;

// ── Public API ────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex
{
    public readonly Vector3 Position;
    public readonly Vector3 Color;
    public Vertex(Vector3 position, Vector3 color) { Position = position; Color = color; }
}

public readonly record struct MeshHandle(int Id);

public readonly struct MeshInstance
{
    public readonly MeshHandle Mesh;
    public readonly Matrix4x4 Transform;
    public MeshInstance(MeshHandle mesh, Matrix4x4 transform) { Mesh = mesh; Transform = transform; }
}

public interface IRenderer : IDisposable
{
    MeshHandle UploadMesh(ReadOnlySpan<Vertex> vertices);
    void ReleaseMesh(MeshHandle handle);
    void DrawFrame(ReadOnlySpan<MeshInstance> instances);
}

// ── Internal implementation ───────────────────────────────────────────────────

internal sealed unsafe class SdlRenderer : IRenderer
{
    private readonly GpuDevice _gpu;
    private readonly Dictionary<int, MeshBuffer> _meshes = new();
    private int _nextId;

    internal SdlRenderer(string title, int width, int height)
    {
        _gpu = new GpuDevice(title, width, height);
    }

    public MeshHandle UploadMesh(ReadOnlySpan<Vertex> vertices)
    {
        uint size = (uint)(vertices.Length * Marshal.SizeOf<Vertex>());

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
        Marshal.Copy(MemoryMarshal.AsBytes(vertices).ToArray(), 0, (nint)map, (int)size);
        SDL3.SDL_UnmapGPUTransferBuffer(_gpu.Device, xfer);

        var cmd = _gpu.AcquireCommandBuffer();
        var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = xfer, offset = 0 };
        var dst = new SDL_GPUBufferRegion { buffer = gpuBuffer, offset = 0, size = size };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &src, &dst, false);
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

    public void DrawFrame(ReadOnlySpan<MeshInstance> instances)
    {
        var cmd = _gpu.AcquireCommandBuffer();
        SDL_GPUTexture* swapchain; uint sw, sh;
        SDL3.SDL_WaitAndAcquireGPUSwapchainTexture(cmd, _gpu.Window, &swapchain, &sw, &sh);
        if (swapchain == null) { SDL3.SDL_CancelGPUCommandBuffer(cmd); return; }

        _gpu.ResizeDepthTextureIfNeeded(sw, sh);

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
            var mvp = BuildMVP(instance.Transform, sw, sh);
            fixed (float* mvpPtr = mvp)
                SDL3.SDL_PushGPUVertexUniformData(cmd, 0, (nint)mvpPtr, 64);
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

    private static float[] BuildMVP(Matrix4x4 model, uint w, uint h)
    {
        float f = 1.0f / MathF.Tan(MathF.PI / 8f);
        float aspect = (float)w / h;
        float near = 0.01f, far = 100f;

        float[] proj = new float[16];
        proj[0]  = f / aspect;
        proj[5]  = f;
        proj[10] = (near + far) / (near - far);
        proj[11] = -1f;
        proj[14] = (2f * near * far) / (near - far);

        // view: identity translated -2.5 on Z
        float[] view = new float[16];
        view[0] = view[5] = view[10] = view[15] = 1f;
        view[14] = -2.5f;

        // model matrix from Matrix4x4 (row-major) → column-major float[16]
        float[] m =
        [
            model.M11, model.M21, model.M31, model.M41,
            model.M12, model.M22, model.M32, model.M42,
            model.M13, model.M23, model.M33, model.M43,
            model.M14, model.M24, model.M34, model.M44,
        ];

        return Mul(Mul(proj, view), m);
    }

    private static float[] Mul(float[] a, float[] b)
    {
        var r = new float[16];
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
                for (int k = 0; k < 4; k++)
                    r[j * 4 + i] += a[k * 4 + i] * b[j * 4 + k];
        return r;
    }

    // ── Nested private type ───────────────────────────────────────────────────

    private sealed class MeshBuffer
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
