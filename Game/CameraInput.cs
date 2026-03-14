namespace VoxPopuli;

public readonly struct CameraInput
{
    public readonly float PanX;
    public readonly float PanZ;
    public readonly float ZoomDelta;
    public readonly float RotateDelta;
    public readonly float MouseDX;
    public readonly float MouseDY;
    public readonly float DeltaTime;

    public CameraInput(float panX, float panZ, float zoomDelta, float rotateDelta, float mouseDX, float mouseDY, float deltaTime)
    {
        PanX = panX;
        PanZ = panZ;
        ZoomDelta = zoomDelta;
        RotateDelta = rotateDelta;
        MouseDX = mouseDX;
        MouseDY = mouseDY;
        DeltaTime = deltaTime;
    }
}
