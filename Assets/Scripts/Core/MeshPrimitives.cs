using System.Collections.Generic;

public readonly struct Vec2
{
    public readonly float X;
    public readonly float Y;

    public Vec2(float x, float y)
    {
        X = x;
        Y = y;
    }
}

public readonly struct Vec3
{
    public readonly float X;
    public readonly float Y;
    public readonly float Z;

    public Vec3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vec3 operator +(Vec3 left, Vec3 right)
    {
        return new Vec3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }
}

public sealed class ChunkMeshData
{
    public readonly List<Vec3> Vertices = new();
    public readonly List<int> Triangles = new();
    public readonly List<Vec3> Normals = new();
    public readonly List<Vec2> Uvs = new();
}
