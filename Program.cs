namespace VoxPopuli;
using System.Numerics;
using SDL;
using VoxPopuli.Game;
using VoxPopuli.Renderer;
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Index 0 = air (transparent), indices 1-255 = placeholder solid colors
        var colorTable = new Vector4[256];
        colorTable[0] = Vector4.Zero;
        for (int i = 1; i < 256; i++)
        {
            colorTable[i] = new Vector4((i & 1) * 0.8f + 0.1f, ((i >> 1) & 1) * 0.8f + 0.1f, ((i >> 2) & 1) * 0.8f + 0.1f, 1f);
        }
        // Terrain types: 2=snow, 3=sand, 5=stone, 7=grass, 8=rock
        colorTable[2] = new Vector4(0.9f, 0.9f, 0.95f, 1f); // snow
        colorTable[3] = new Vector4(0.76f, 0.70f, 0.50f, 1f); // sand
        colorTable[5] = new Vector4(0.45f, 0.40f, 0.35f, 1f); // stone/dirt
        colorTable[7] = new Vector4(0.30f, 0.65f, 0.25f, 1f); // grass
        colorTable[8] = new Vector4(0.50f, 0.45f, 0.40f, 1f); // rock

        using var device = new SdlGpuDevice("VoxPopuli", 800, 600);
        using var renderer = new SdlRenderer(device, colorTable);
        using var game = new VoxGame(renderer);
        unsafe
        {
            var run = true;
            bool prevFDown = false;
            ulong prevCounter = SDL3.SDL_GetPerformanceCounter();
            ulong freq = SDL3.SDL_GetPerformanceFrequency();

            while (run)
            {
                ulong now = SDL3.SDL_GetPerformanceCounter();
                float deltaTime = (float)(now - prevCounter) / (float)freq;
                prevCounter = now;

                float zoomDelta = 0f, mouseDX = 0f, mouseDY = 0f;
                float rotateDelta = 0f, panX = 0f, panZ = 0f;

                SDL_Event @event;
                while (SDL3.SDL_PollEvent(&@event))
                {
                    if (@event.type == (uint)SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED)
                    {
                        run = false;
                    }
                    else if (@event.type == (uint)SDL_EventType.SDL_EVENT_KEY_DOWN &&
                             @event.key.scancode == SDL_Scancode.SDL_SCANCODE_P)
                    {
                        game.ToggleWireframe();
                    }
                    else if (@event.type == (uint)SDL_EventType.SDL_EVENT_KEY_DOWN &&
                             @event.key.scancode == SDL_Scancode.SDL_SCANCODE_R)
                    {
                        game.MarkAllChunksDirty();
                    }
                    else if (@event.type == (uint)SDL_EventType.SDL_EVENT_MOUSE_WHEEL)
                    {
                        zoomDelta += @event.wheel.y;
                    }
                    else if (@event.type == (uint)SDL_EventType.SDL_EVENT_MOUSE_MOTION)
                    {
                        var buttons = SDL3.SDL_GetMouseState(null, null);
                        if ((buttons & (SDL_MouseButtonFlags)SDL3.SDL_BUTTON_RMASK) != 0)
                        {
                            mouseDX += @event.motion.xrel;
                            mouseDY += @event.motion.yrel;
                        }
                    }
                }

                SDLBool* keys = SDL3.SDL_GetKeyboardState(null);
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_W]) { panZ = 1f; }
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_S]) { panZ = -1f; }
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_A]) { panX = -1f; }
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_D]) { panX = 1f; }
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_Q]) { rotateDelta = -1f; }
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_E]) { rotateDelta = 1f; }
                bool fDown = keys[(int)SDL_Scancode.SDL_SCANCODE_F];
                bool triggerEdit = fDown && !prevFDown;
                prevFDown = fDown;

                var input = new CameraInput(panX, panZ, zoomDelta, rotateDelta, mouseDX, mouseDY, deltaTime);
                var view = game.Tick(input);
                if (triggerEdit)
                {
                    game.DeleteClosestChunk(view.Eye, view.Target);
                }
            }
        }
    }
}
