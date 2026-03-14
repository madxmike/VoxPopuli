namespace VoxPopuli.Renderer;
using System.Numerics;

internal static class Frustum
{
    internal static void Extract(in Matrix4x4 vp, Span<Vector4> planes)
    {
        planes[0] = Normalize(new Vector4(vp.M41+vp.M11, vp.M42+vp.M12, vp.M43+vp.M13, vp.M44+vp.M14)); // Left
        planes[1] = Normalize(new Vector4(vp.M41-vp.M11, vp.M42-vp.M12, vp.M43-vp.M13, vp.M44-vp.M14)); // Right
        planes[2] = Normalize(new Vector4(vp.M41+vp.M21, vp.M42+vp.M22, vp.M43+vp.M23, vp.M44+vp.M24)); // Bottom
        planes[3] = Normalize(new Vector4(vp.M41-vp.M21, vp.M42-vp.M22, vp.M43-vp.M23, vp.M44-vp.M24)); // Top
        planes[4] = Normalize(new Vector4(vp.M31,        vp.M32,        vp.M33,        vp.M34));          // Near
        planes[5] = Normalize(new Vector4(vp.M41-vp.M31, vp.M42-vp.M32, vp.M43-vp.M33, vp.M44-vp.M34)); // Far
    }

    // Returns true if the AABB is outside the frustum (should be culled).
    internal static bool Cull(ReadOnlySpan<Vector4> planes, Vector3 aabbMin, Vector3 aabbMax)
    {
        foreach (var p in planes)
        {
            float px = p.X >= 0 ? aabbMax.X : aabbMin.X;
            float py = p.Y >= 0 ? aabbMax.Y : aabbMin.Y;
            float pz = p.Z >= 0 ? aabbMax.Z : aabbMin.Z;
            if (p.X * px + p.Y * py + p.Z * pz + p.W < 0f)
                return true;
        }
        return false;
    }

    private static Vector4 Normalize(Vector4 p)
    {
        float len = MathF.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
        return len > 0f ? p / len : p;
    }
}
