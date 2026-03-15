namespace VoxPopuli.Input;

public abstract record InputEvent;

public sealed record KeyEvent(string Key, bool IsPressed) : InputEvent;

public sealed record MouseButtonEvent(int Button, bool IsPressed) : InputEvent;

public sealed record MouseMotionEvent(float DeltaX, float DeltaY) : InputEvent;

public sealed record MouseWheelEvent(float Delta) : InputEvent;

public sealed record QuitEvent : InputEvent;
