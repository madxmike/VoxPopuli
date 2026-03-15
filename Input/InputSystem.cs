namespace VoxPopuli.Input;

using System.Collections.Generic;
using SDL;

public sealed unsafe class InputSystem : IDisposable
{
    private readonly Dictionary<string, List<ActionBinding>> _bindings = new();
    private bool _quitRequested;
    private bool _rightMouseDown;

    public bool RightMouseDown => _rightMouseDown;

    public void Bind(ActionBinding binding)
    {
        if (!_bindings.TryGetValue(binding.ActionName, out var list))
        {
            list = new List<ActionBinding>();
            _bindings[binding.ActionName] = list;
        }
        list.Add(binding);
    }

    public void Unbind(string actionName)
    {
        _bindings.Remove(actionName);
    }

    public bool Poll()
    {
        _quitRequested = false;

        SDL_Event @event;
        while (SDL3.SDL_PollEvent(&@event))
        {
            ProcessEvent(@event);
        }

        FireKeyHeldBindings();

        return !_quitRequested;
    }

    private void ProcessEvent(SDL_Event @event)
    {
        InputEvent? inputEvent = @event.type switch
        {
            (uint)SDL_EventType.SDL_EVENT_KEY_DOWN => new KeyEvent(GetScancodeName(@event.key.scancode), true),
            (uint)SDL_EventType.SDL_EVENT_KEY_UP => new KeyEvent(GetScancodeName(@event.key.scancode), false),
            (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN => new MouseButtonEvent(@event.button.button, true),
            (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP => new MouseButtonEvent(@event.button.button, false),
            (uint)SDL_EventType.SDL_EVENT_MOUSE_MOTION => new MouseMotionEvent(@event.motion.xrel, @event.motion.yrel),
            (uint)SDL_EventType.SDL_EVENT_MOUSE_WHEEL => new MouseWheelEvent(@event.wheel.y),
            (uint)SDL_EventType.SDL_EVENT_QUIT => new QuitEvent(),
            _ => null
        };

        if (inputEvent is null)
        {
            return;
        }

        if (@event.type == (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN)
        {
            if (@event.button.button == 3)
            {
                _rightMouseDown = true;
            }
        }
        else if (@event.type == (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP)
        {
            if (@event.button.button == 3)
            {
                _rightMouseDown = false;
            }
        }

        FireBindings(inputEvent);
    }

    private void FireBindings(InputEvent inputEvent)
    {
        foreach (var pair in _bindings)
        {
            foreach (var binding in pair.Value)
            {
                if (ShouldFireBinding(binding, inputEvent))
                {
                    binding.OnFired(inputEvent);
                }
            }
        }
    }

    private bool ShouldFireBinding(ActionBinding binding, InputEvent inputEvent)
    {
        foreach (var trigger in binding.Triggers)
        {
            switch (inputEvent)
            {
                case KeyEvent keyEvent:
                    if (trigger.Kind == TriggerKind.KeyPressed && keyEvent.IsPressed && trigger.Key == keyEvent.Key)
                        return true;
                    if (trigger.Kind == TriggerKind.KeyReleased && !keyEvent.IsPressed && trigger.Key == keyEvent.Key)
                        return true;
                    if (trigger.Kind == TriggerKind.KeyHeld && keyEvent.IsPressed && trigger.Key == keyEvent.Key)
                        return true;
                    break;

                case MouseButtonEvent mouseButtonEvent:
                    if (trigger.Kind == TriggerKind.MouseButtonPressed && mouseButtonEvent.IsPressed && trigger.Button == mouseButtonEvent.Button)
                        return true;
                    if (trigger.Kind == TriggerKind.MouseButtonReleased && !mouseButtonEvent.IsPressed && trigger.Button == mouseButtonEvent.Button)
                        return true;
                    break;

                case MouseMotionEvent:
                    if (trigger.Kind == TriggerKind.MouseMotion)
                        return true;
                    break;

                case MouseWheelEvent:
                    if (trigger.Kind == TriggerKind.MouseWheel)
                        return true;
                    break;

                case QuitEvent:
                    if (trigger.Kind == TriggerKind.MouseMotion || trigger.Kind == TriggerKind.MouseWheel)
                        return true;
                    break;
            }
        }

        return false;
    }

    private void FireKeyHeldBindings()
    {
        SDLBool* keys = SDL3.SDL_GetKeyboardState(null);

        foreach (var pair in _bindings)
        {
            foreach (var binding in pair.Value)
            {
                foreach (var trigger in binding.Triggers)
                {
                    if (trigger.Kind == TriggerKind.KeyHeld && trigger.Key is not null)
                    {
                        var scancode = SDL3.SDL_GetScancodeFromName(trigger.Key);
                        if (keys[(int)scancode])
                        {
                            binding.OnFired(new KeyEvent(trigger.Key, true));
                        }
                    }
                }
            }
        }
    }

    private static string GetScancodeName(SDL_Scancode scancode)
    {
        return SDL3.SDL_GetScancodeName(scancode);
    }

    public void Dispose()
    {
        _bindings.Clear();
    }
}
