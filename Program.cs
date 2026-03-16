namespace VoxPopuli;

using System.Numerics;
using SDL;
using VoxPopuli.Game;
using VoxPopuli.Input;
using VoxPopuli.Renderer;
using VoxPopuli.Renderer.UI;
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
        using var inputSystem = new InputSystem();

        float zoomDelta = 0f, mouseDX = 0f, mouseDY = 0f;
        float rotateDelta = 0f, panX = 0f, panZ = 0f;
        bool fDown = false;

        inputSystem.Bind(new ActionBinding("panZ", [TriggerCondition.WhileKeyHeld("W")], _ => panZ = 1f));
        inputSystem.Bind(new ActionBinding("panZ", [TriggerCondition.WhileKeyHeld("S")], _ => panZ = -1f));
        inputSystem.Bind(new ActionBinding("panX", [TriggerCondition.WhileKeyHeld("A")], _ => panX = -1f));
        inputSystem.Bind(new ActionBinding("panX", [TriggerCondition.WhileKeyHeld("D")], _ => panX = 1f));
        inputSystem.Bind(new ActionBinding("rotate", [TriggerCondition.WhileKeyHeld("Q")], _ => rotateDelta = -1f));
        inputSystem.Bind(new ActionBinding("rotate", [TriggerCondition.WhileKeyHeld("E")], _ => rotateDelta = 1f));
        inputSystem.Bind(new ActionBinding("zoom", [TriggerCondition.OnMouseWheel()], e => zoomDelta += ((MouseWheelEvent)e).Delta));
        inputSystem.Bind(new ActionBinding("fKey", [TriggerCondition.WhileKeyHeld("F")], e => fDown = ((KeyEvent)e).IsPressed));
        inputSystem.Bind(new ActionBinding("quit", [TriggerCondition.OnKeyPressed("P")], _ => game.ToggleWireframe()));
        inputSystem.Bind(new ActionBinding("quit", [TriggerCondition.OnKeyPressed("R")], _ => game.MarkAllChunksDirty()));
        inputSystem.Bind(new ActionBinding("mouseMotion", [TriggerCondition.OnMouseMotion()], e =>
        {
            if (inputSystem.RightMouseDown)
            {
                var motion = (MouseMotionEvent)e;
                mouseDX += motion.DeltaX;
                mouseDY += motion.DeltaY;
            }
        }));

        unsafe
        {
            ulong prevCounter = SDL3.SDL_GetPerformanceCounter();
            ulong freq = SDL3.SDL_GetPerformanceFrequency();
            bool prevFDown = false;

            while (inputSystem.Poll())
            {
                ulong now = SDL3.SDL_GetPerformanceCounter();
                float deltaTime = (float)(now - prevCounter) / (float)freq;
                prevCounter = now;

                var input = new CameraInput(panX, panZ, zoomDelta, rotateDelta, mouseDX, mouseDY, deltaTime);
                var view = game.Tick(input);

                bool triggerEdit = fDown && !prevFDown;
                prevFDown = fDown;

                if (triggerEdit)
                {
                    game.DeleteClosestChunk(view.Eye, view.Target);
                }

                zoomDelta = 0f; mouseDX = 0f; mouseDY = 0f;
                rotateDelta = 0f; panX = 0f; panZ = 0f;

                var ui = renderer.GetUIDrawContext();
                ui.DrawRect(UIAnchor.TopLeft, new Vector2(10, 10), new Vector2(200, 40), new Color4(1f, 0f, 0f, 0.8f));
                ui.DrawRect(UIAnchor.BottomRight, new Vector2(-10, -10), new Vector2(100, 100), new Color4(0f, 1f, 0f, 0.5f));

                renderer.DrawFrame(view, game.World);
            }
        }
    }
}
