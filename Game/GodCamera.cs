namespace VoxPopuli.Game;

using System.Numerics;

internal sealed class GodCamera
{
    public float PanSpeed = 5f;
    public float ZoomSpeed = 3f;
    public float RotateSpeed = 1.5f;
    public float MouseRotateSpeed = 0.005f;
    public float SmoothFactor = 10f;
    public float MinDistance = 2f;
    public float MaxDistance = 50f;
    public float MinPitch = 0.26f;
    public float MaxPitch = 1.40f;

    private Vector3 _targetGoal = Vector3.Zero;
    private float _yawGoal = 0f;
    private float _pitchGoal = MathF.PI / 4f;
    private float _distanceGoal = 10f;

    private Vector3 _targetSmoothed = Vector3.Zero;
    private float _yawSmoothed = 0f;
    private float _pitchSmoothed = MathF.PI / 4f;
    private float _distanceSmoothed = 10f;

    internal Matrix4x4 Update(CameraInput input)
    {
        var forward = new Vector3(-MathF.Sin(_yawGoal), 0, -MathF.Cos(_yawGoal));
        var right = new Vector3(MathF.Cos(_yawGoal), 0, -MathF.Sin(_yawGoal));
        var pan = right * input.PanX + forward * input.PanZ;
        if (pan.LengthSquared() > 0f) pan = Vector3.Normalize(pan);
        _targetGoal += pan * PanSpeed * input.DeltaTime;

        _distanceGoal = Math.Clamp(_distanceGoal - input.ZoomDelta * ZoomSpeed, MinDistance, MaxDistance);
        _yawGoal += (input.RotateDelta * RotateSpeed * input.DeltaTime) + (input.MouseDX * MouseRotateSpeed);
        _pitchGoal = Math.Clamp(_pitchGoal - input.MouseDY * MouseRotateSpeed, MinPitch, MaxPitch);

        float alpha = 1f - MathF.Exp(-SmoothFactor * input.DeltaTime);
        _targetSmoothed = Vector3.Lerp(_targetSmoothed, _targetGoal, alpha);
        _yawSmoothed += (_yawGoal - _yawSmoothed) * alpha;
        _pitchSmoothed += (_pitchGoal - _pitchSmoothed) * alpha;
        _distanceSmoothed += (_distanceGoal - _distanceSmoothed) * alpha;

        var eye = _targetSmoothed + new Vector3(
            _distanceSmoothed * MathF.Cos(_pitchSmoothed) * MathF.Sin(_yawSmoothed),
            _distanceSmoothed * MathF.Sin(_pitchSmoothed),
            _distanceSmoothed * MathF.Cos(_pitchSmoothed) * MathF.Cos(_yawSmoothed)
        );
        return Matrix4x4.CreateLookAt(eye, _targetSmoothed, Vector3.UnitY);
    }
}
