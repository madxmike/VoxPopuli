namespace VoxPopuli.Game;

using System.Numerics;
using VoxPopuli.Renderer;

/// <summary>
/// Owns game state and drives the render loop. Depends only on <see cref="IRenderer"/> —
/// no SDL types or GPU handles appear here.
/// </summary>
internal sealed class VoxGame : IDisposable
{
    private readonly IRenderer _renderer;
    private readonly MeshHandle _cubeMesh;
    private readonly MeshInstance[] _instances;
    private readonly GodCamera _camera = new();

    // 36 explicit vertices (6 faces × 2 triangles × 3 vertices), no index buffer.
    // Each vertex: world-space position + RGB color in [0,1].
    private static readonly Vertex[] CubeVertices =
    [
        /* Front */
        new(new(-0.5f,  0.5f, -0.5f), new(1f, 0f, 0f)),
        new(new( 0.5f, -0.5f, -0.5f), new(0f, 0f, 1f)),
        new(new(-0.5f, -0.5f, -0.5f), new(0f, 1f, 0f)),
        new(new(-0.5f,  0.5f, -0.5f), new(1f, 0f, 0f)),
        new(new( 0.5f,  0.5f, -0.5f), new(1f, 1f, 0f)),
        new(new( 0.5f, -0.5f, -0.5f), new(0f, 0f, 1f)),
        /* Left */
        new(new(-0.5f,  0.5f,  0.5f), new(1f, 1f, 1f)),
        new(new(-0.5f, -0.5f, -0.5f), new(0f, 1f, 0f)),
        new(new(-0.5f, -0.5f,  0.5f), new(0f, 1f, 1f)),
        new(new(-0.5f,  0.5f,  0.5f), new(1f, 1f, 1f)),
        new(new(-0.5f,  0.5f, -0.5f), new(1f, 0f, 0f)),
        new(new(-0.5f, -0.5f, -0.5f), new(0f, 1f, 0f)),
        /* Top */
        new(new(-0.5f,  0.5f,  0.5f), new(1f, 1f, 1f)),
        new(new( 0.5f,  0.5f, -0.5f), new(1f, 1f, 0f)),
        new(new(-0.5f,  0.5f, -0.5f), new(1f, 0f, 0f)),
        new(new(-0.5f,  0.5f,  0.5f), new(1f, 1f, 1f)),
        new(new( 0.5f,  0.5f,  0.5f), new(0f, 0f, 0f)),
        new(new( 0.5f,  0.5f, -0.5f), new(1f, 1f, 0f)),
        /* Right */
        new(new( 0.5f,  0.5f, -0.5f), new(1f, 1f, 0f)),
        new(new( 0.5f, -0.5f,  0.5f), new(1f, 0f, 1f)),
        new(new( 0.5f, -0.5f, -0.5f), new(0f, 0f, 1f)),
        new(new( 0.5f,  0.5f, -0.5f), new(1f, 1f, 0f)),
        new(new( 0.5f,  0.5f,  0.5f), new(0f, 0f, 0f)),
        new(new( 0.5f, -0.5f,  0.5f), new(1f, 0f, 1f)),
        /* Back */
        new(new( 0.5f,  0.5f,  0.5f), new(0f, 0f, 0f)),
        new(new(-0.5f, -0.5f,  0.5f), new(0f, 1f, 1f)),
        new(new( 0.5f, -0.5f,  0.5f), new(1f, 0f, 1f)),
        new(new( 0.5f,  0.5f,  0.5f), new(0f, 0f, 0f)),
        new(new(-0.5f,  0.5f,  0.5f), new(1f, 1f, 1f)),
        new(new(-0.5f, -0.5f,  0.5f), new(0f, 1f, 1f)),
        /* Bottom */
        new(new(-0.5f, -0.5f, -0.5f), new(0f, 1f, 0f)),
        new(new( 0.5f, -0.5f,  0.5f), new(1f, 0f, 1f)),
        new(new(-0.5f, -0.5f,  0.5f), new(0f, 1f, 1f)),
        new(new(-0.5f, -0.5f, -0.5f), new(0f, 1f, 0f)),
        new(new( 0.5f, -0.5f, -0.5f), new(0f, 0f, 1f)),
        new(new( 0.5f, -0.5f,  0.5f), new(1f, 0f, 1f)),
    ];

    internal VoxGame(IRenderer renderer)
    {
        _renderer = renderer;
        _cubeMesh = renderer.UploadMesh(CubeVertices);

        _instances = [new MeshInstance(_cubeMesh, Matrix4x4.Identity)];
    }

    internal void Tick(CameraInput input)
    {
        var view = _camera.Update(input);
        _renderer.DrawFrame(_instances, view);
    }

    public void Dispose() => _renderer.ReleaseMesh(_cubeMesh);
}
