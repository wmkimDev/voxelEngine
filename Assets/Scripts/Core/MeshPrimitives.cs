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

    public static Vec3 operator -(Vec3 left, Vec3 right)
    {
        return new Vec3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    public static Vec3 Cross(Vec3 left, Vec3 right)
    {
        return new Vec3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    public static float Dot(Vec3 left, Vec3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }
}

public sealed class ChunkMeshData
{
    public readonly List<Vec3> Vertices = new();
    public readonly List<int> Triangles = new();
    public readonly List<Vec3> Normals = new();
    public readonly List<Vec2> Uvs = new();

    // 매번 새 List를 만들지 않고, 기존 ChunkMeshData 인스턴스를 다시 쓰기 위한 초기화입니다.
    // 이전 프레임 내용은 비우고, 이번 복사/빌드에 필요한 크기만큼만 capacity를 미리 맞춥니다.
    public void ResetAndEnsureCapacity(int vertexCount, int triangleCount, int normalCount, int uvCount)
    {
        Vertices.Clear();
        Triangles.Clear();
        Normals.Clear();
        Uvs.Clear();

        if (Vertices.Capacity < vertexCount)
        {
            Vertices.Capacity = vertexCount;
        }

        if (Triangles.Capacity < triangleCount)
        {
            Triangles.Capacity = triangleCount;
        }

        if (Normals.Capacity < normalCount)
        {
            Normals.Capacity = normalCount;
        }

        if (Uvs.Capacity < uvCount)
        {
            Uvs.Capacity = uvCount;
        }
    }

    // 다른 ChunkMeshData의 내용을 이 인스턴스로 복사합니다.
    // 완료된 메시를 재사용 버퍼로 옮길 때 중간 List를 새로 만들지 않기 위해 씁니다.
    public void CopyFrom(ChunkMeshData source)
    {
        ResetAndEnsureCapacity(
            source.Vertices.Count,
            source.Triangles.Count,
            source.Normals.Count,
            source.Uvs.Count);

        Vertices.AddRange(source.Vertices);
        Triangles.AddRange(source.Triangles);
        Normals.AddRange(source.Normals);
        Uvs.AddRange(source.Uvs);
    }
}
