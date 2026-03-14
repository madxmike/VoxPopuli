namespace VoxPopuli;
using SDL;
using VoxPopuli.Game;
using VoxPopuli.Renderer;
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var renderer = new SdlRenderer("VoxPopuli", 800, 600);
        using var game = new VoxGame(renderer);
        unsafe
        {
            var run = true;
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
                        run = false;
                    else if (@event.type == (uint)SDL_EventType.SDL_EVENT_MOUSE_WHEEL)
                        zoomDelta += @event.wheel.y;
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
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_W]) panZ = 1f;
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_S]) panZ = -1f;
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_A]) panX = -1f;
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_D]) panX = 1f;
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_Q]) rotateDelta = -1f;
                if (keys[(int)SDL_Scancode.SDL_SCANCODE_E]) rotateDelta = 1f;


                var input = new CameraInput(panX, panZ, zoomDelta, rotateDelta, mouseDX, mouseDY, deltaTime);
                game.Tick(input);
            }
        }
    }
}
