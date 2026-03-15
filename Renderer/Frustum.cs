namespace VoxPopuli.Renderer;

using System.Numerics;

internal readonly struct Frustum
{
    private readonly Vector4 _left, _right, _bottom, _top, _near, _far;

    private Frustum(Vector4 left, Vector4 right, Vector4 bottom, Vector4 top, Vector4 near, Vector4 far)
    {
        _left   = left;
        _right  = right;
        _bottom = bottom;
        _top    = top;
        _near   = near;
        _far    = far;
    }

    public static Frustum FromViewProj(in Matrix4x4 m)
    {
        // System.Numerics uses row-vector convention: clip = worldPos * M.
        // Gribb/Hartmann plane extraction therefore operates on columns, not rows.
        var c0 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c1 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c2 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c3 = new Vector4(m.M14, m.M24, m.M34, m.M44);

        return new Frustum(
            Normalize(c3 + c0),
            Normalize(c3 - c0),
            Normalize(c3 + c1),
            Normalize(c3 - c1),
            Normalize(c3 + c2),
            Normalize(c3 - c2)
        );
    }

    private static Vector4 Normalize(Vector4 p)
    {
        float len = MathF.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
        return p / len;
    }

    public bool IsAabbOutside(Vector3 min, Vector3 max)
    {
        return IsOutside(_left,   min, max)
            || IsOutside(_right,  min, max)
            || IsOutside(_bottom, min, max)
            || IsOutside(_top,    min, max)
            || IsOutside(_near,   min, max)
            || IsOutside(_far,    min, max);
    }

    private static bool IsOutside(Vector4 plane, Vector3 min, Vector3 max)
    {
        // Positive vertex: corner furthest along plane normal
        var pos = new Vector3(
            plane.X >= 0f ? max.X : min.X,
            plane.Y >= 0f ? max.Y : min.Y,
            plane.Z >= 0f ? max.Z : min.Z
        );
        return plane.X * pos.X + plane.Y * pos.Y + plane.Z * pos.Z + plane.W < 0f;
    }
}
