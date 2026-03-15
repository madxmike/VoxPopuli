namespace VoxPopuli.Renderer;

using System;
using VoxPopuli.Game;

/// <summary>Main renderer interface for drawing frames.</summary>
internal interface IRenderer : IDisposable
{
    /// <summary>Draws a frame with the given camera view and world.</summary>
    void DrawFrame(CameraView camera, VoxelWorld world, bool wireframe = false);
}
