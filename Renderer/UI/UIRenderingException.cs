namespace VoxPopuli.Renderer.UI;

using System;
using SDL;

/// <summary>Exception type for UI rendering errors, wrapping SDL3 errors.</summary>
internal sealed class UIRenderingException : Exception
{
    /// <summary>Creates a new UIRenderingException with the specified message and appends SDL_GetError().</summary>
    /// <param name="message">The error message.</param>
    internal UIRenderingException(string message)
        : base($"{message}: {SDL3.SDL_GetError()}")
    {
    }
}
