namespace VoxPopuli.Renderer;

using System;
using VoxPopuli.Game;
using VoxPopuli.Renderer.UI;

/// <summary>Main renderer interface for drawing frames.</summary>
internal interface IRenderer : IDisposable
{
    /// <summary>Draws a frame with the given camera view and world.</summary>
    void DrawFrame(CameraView camera, VoxelWorld world, bool wireframe = false);

    /// <summary>Gets a frame-scoped UIDrawContext for UI rendering.</summary>
    /// <returns>A UIDrawContext that writes into the UI renderer's vertex buffer.</returns>
    UIDrawContext GetUIDrawContext();
}
