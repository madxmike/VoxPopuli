namespace VoxPopuli.Game;

using System.Numerics;

public struct Transform
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public static Transform Identity => new()
    {
        Position = Vector3.Zero,
        Rotation = Quaternion.Identity,
        Scale    = Vector3.One
    };

    public readonly Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);
    public readonly Vector3 Right   => Vector3.Transform( Vector3.UnitX, Rotation);
    public readonly Vector3 Up      => Vector3.Transform( Vector3.UnitY, Rotation);
}
