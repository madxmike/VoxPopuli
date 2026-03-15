namespace VoxPopuli.Renderer;

using System.Numerics;

/// <summary>
/// View frustum defined by six clipping planes for culling operations.
/// Plane extraction uses the Gribb/Hartmann method: <a href="https://en.wikipedia.org/wiki/Hidden-surface_determination#Gribb_and_Hartmann">Wikipedia</a>.
/// </summary>
internal readonly struct Frustum
{
    private readonly Vector4 _left, _right, _bottom, _top, _near, _far;

    /// <summary>
    /// Creates a frustum with the specified clipping planes.
    /// Each plane is stored as (NormalXYZ, DistanceW) in the format: dot(P, Normal) + W = 0.
    /// </summary>
    private Frustum(Vector4 left, Vector4 right, Vector4 bottom, Vector4 top, Vector4 near, Vector4 far)
    {
        _left = left;
        _right = right;
        _bottom = bottom;
        _top = top;
        _near = near;
        _far = far;
    }

    /// <summary>
    /// Extracts frustum clipping planes from a view-projection matrix using the Gribb/Hartmann method.
    /// See <a href="https://en.wikipedia.org/wiki/Hidden-surface_determination#Gribb_and_Hartmann">Wikipedia</a>.
    /// Handles System.Numerics' row-vector convention where clip = worldPos * M.
    /// </summary>
    public static Frustum FromViewProj(in Matrix4x4 m)
    {
        // Extract columns (not rows) due to row-vector convention
        var c0 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c1 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c2 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c3 = new Vector4(m.M14, m.M24, m.M34, m.M44);

        return new Frustum(
            Vector4.Normalize(c3 + c0), // Left plane
            Vector4.Normalize(c3 - c0), // Right plane
            Vector4.Normalize(c3 + c1), // Bottom plane
            Vector4.Normalize(c3 - c1), // Top plane
            Vector4.Normalize(c3 + c2), // Near plane
            Vector4.Normalize(c3 - c2)  // Far plane
        );
    }

    /// <summary>
    /// Tests if an axis-aligned bounding box is completely outside the frustum.
    /// Returns true if the box is outside (should be culled), false if it intersects or is inside.
    /// </summary>
    public bool IsAabbOutside(Vector3 min, Vector3 max)
    {
        return IsOutside(_left, min, max)
            || IsOutside(_right, min, max)
            || IsOutside(_bottom, min, max)
            || IsOutside(_top, min, max)
            || IsOutside(_near, min, max)
            || IsOutside(_far, min, max);
    }

    /// <summary>
    /// Tests if an AABB is completely on the negative side of a clipping plane.
    /// Uses the positive vertex (corner furthest along the plane normal) for the test.
    /// </summary>
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
