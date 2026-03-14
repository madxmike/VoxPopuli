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
            while (run)
            {
                SDL_Event @event;
                while (SDL3.SDL_PollEvent(&@event))
                {
                    if (@event.type == (uint)SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED)
                        run = false;
                }
                game.Tick();
            }
        }
    }
}
