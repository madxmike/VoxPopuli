namespace VoxPopuli.Renderer;

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using VoxPopuli.Game;

// ── Public API ────────────────────────────────────────────────────────────────

/// <summary>
/// A single vertex in world space. Sequential layout matches the GPU pipeline:
/// float3 position (offset 0) followed by float3 color (offset 12), 24 bytes total.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex
{
    public readonly Vector3 Position;
    public readonly Vector3 Color;
    public Vertex(Vector3 position, Vector3 color) { Position = position; Color = color; }
}

/// <summary>
/// Opaque reference to geometry uploaded to the GPU. The renderer owns the
/// underlying buffer; callers may only pass this handle back to the renderer.
/// </summary>
public readonly record struct MeshHandle(int Id);

/// <summary>
/// A single draw request: which mesh to draw and where to place it in world space.
/// </summary>
public readonly struct MeshInstance
{
    public readonly MeshHandle Mesh;
    public readonly Transform Transform;
    public MeshInstance(MeshHandle mesh, Transform transform) { Mesh = mesh; Transform = transform; }
}

/// <summary>
/// Renders geometry submitted by the game. The game never sees SDL types,
/// command buffers, or GPU resource handles — only these three methods.
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>
    /// Uploads vertex data to the GPU and returns an opaque handle.
    /// The caller owns the handle and must call <see cref="ReleaseMesh"/> when done.
    /// </summary>
    MeshHandle UploadMesh(ReadOnlySpan<Vertex> vertices);

    /// <summary>Releases the GPU buffer associated with <paramref name="handle"/>.</summary>
    void ReleaseMesh(MeshHandle handle);

    /// <summary>
    /// Renders all <paramref name="instances"/> and presents the frame.
    /// Each instance is drawn with its own MVP derived from <see cref="MeshInstance.Transform"/>.
    /// </summary>
    void DrawFrame(ReadOnlySpan<MeshInstance> instances, CameraView view);
}
