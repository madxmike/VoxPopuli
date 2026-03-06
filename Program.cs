namespace VoxPopuli;

using System;
using SDL;

public static class RenderConstants
{
    public const string ShaderPath = "Shaders/";
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO);



        SDL3.SDL_Quit();
    }
}
