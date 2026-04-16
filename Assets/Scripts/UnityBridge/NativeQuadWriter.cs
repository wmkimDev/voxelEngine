using Unity.Collections;

// Job 안에서 "면 하나를 메시 데이터로 기록"하는 도우미입니다.
// 메셔가 어떤 면을 만들지 결정하면, 이 writer가 정점/노말/UV/삼각형을 NativeList에 추가합니다.
public struct NativeQuadWriter
{
    public NativeList<Vec3> Vertices;
    public NativeList<int> Triangles;
    public NativeList<Vec3> Normals;
    public NativeList<Vec2> Uvs;

    // Job 쪽의 QuadMeshWriter 역할입니다.
    // 어떤 면을 만들지는 메셔가 결정하고, 실제 NativeList 기록 형식은 이 writer가 처리합니다.
    public void Write(FaceDirection direction, byte voxelType, Vec3 voxelLocalPosition)
    {
        int startIndex = Vertices.Length;
        Vec3 normal = FaceTopology.GetNormal(direction);

        AddVertex(voxelType, normal, voxelLocalPosition + FaceTopology.GetUnitQuadCorner(direction, 0), 0);
        AddVertex(voxelType, normal, voxelLocalPosition + FaceTopology.GetUnitQuadCorner(direction, 1), 1);
        AddVertex(voxelType, normal, voxelLocalPosition + FaceTopology.GetUnitQuadCorner(direction, 2), 2);
        AddVertex(voxelType, normal, voxelLocalPosition + FaceTopology.GetUnitQuadCorner(direction, 3), 3);

        Triangles.Add(startIndex + 0);
        Triangles.Add(startIndex + 1);
        Triangles.Add(startIndex + 2);
        Triangles.Add(startIndex + 0);
        Triangles.Add(startIndex + 2);
        Triangles.Add(startIndex + 3);
    }

    private void AddVertex(byte voxelType, Vec3 normal, Vec3 position, int uvIndex)
    {
        Vertices.Add(position);
        Normals.Add(normal);
        Uvs.Add(MeshBuilderUv.GetAtlasUv(voxelType, GetFaceUv(uvIndex)));
    }

    private Vec2 GetFaceUv(int uvIndex)
    {
        return uvIndex switch
        {
            0 => new Vec2(0, 0),
            1 => new Vec2(0, 1),
            2 => new Vec2(1, 1),
            _ => new Vec2(1, 0),
        };
    }
}
