namespace VoxPopuli.Game;

using System.Numerics;
using VoxPopuli.Renderer;

internal sealed class VoxGame : IDisposable
{
    private readonly IRenderer _renderer;
    private readonly MeshHandle _cubeMesh;
    private readonly MeshInstance[] _instances;

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

        float angleY = MathF.PI / 4f;
        float angleX = MathF.PI / 6f;
        var rotY = Matrix4x4.CreateRotationY(angleY);
        var rotX = Matrix4x4.CreateRotationX(angleX);
        _instances = [new MeshInstance(_cubeMesh, rotY * rotX)];
    }

    internal void Tick() => _renderer.DrawFrame(_instances);

    public void Dispose() => _renderer.ReleaseMesh(_cubeMesh);
}
