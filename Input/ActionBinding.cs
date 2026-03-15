namespace VoxPopuli.Input;

public enum TriggerKind
{
    KeyPressed,
    KeyReleased,
    KeyHeld,
    MouseButtonPressed,
    MouseButtonReleased,
    MouseMotion,
    MouseWheel
}

public readonly struct TriggerCondition
{
    public readonly TriggerKind Kind;
    public readonly string? Key;
    public readonly int? Button;

    private TriggerCondition(TriggerKind kind, string? key, int? button)
    {
        Kind = kind;
        Key = key;
        Button = button;
    }

    public static TriggerCondition OnKeyPressed(string key) => new(TriggerKind.KeyPressed, key, null);
    public static TriggerCondition OnKeyReleased(string key) => new(TriggerKind.KeyReleased, key, null);
    public static TriggerCondition WhileKeyHeld(string key) => new(TriggerKind.KeyHeld, key, null);
    public static TriggerCondition OnMouseButtonPressed(int button) => new(TriggerKind.MouseButtonPressed, null, button);
    public static TriggerCondition OnMouseButtonReleased(int button) => new(TriggerKind.MouseButtonReleased, null, button);
    public static TriggerCondition OnMouseMotion() => new(TriggerKind.MouseMotion, null, null);
    public static TriggerCondition OnMouseWheel() => new(TriggerKind.MouseWheel, null, null);
}

public sealed class ActionBinding
{
    public readonly string ActionName;
    public readonly IReadOnlyList<TriggerCondition> Triggers;
    public readonly Action<InputEvent> OnFired;

    public ActionBinding(string actionName, IEnumerable<TriggerCondition> triggers, Action<InputEvent> onFired)
    {
        ActionName = actionName;
        Triggers = triggers.ToList();
        OnFired = onFired;
    }
}
