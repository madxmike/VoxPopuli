namespace VoxPopuli.Game;
using System.Numerics;

public readonly struct CameraView
{
    public readonly Vector3 Eye;
    public readonly Vector3 Target;
    public readonly Vector3 Up;
    public CameraView(Vector3 eye, Vector3 target, Vector3 up) { Eye = eye; Target = target; Up = up; }
}
