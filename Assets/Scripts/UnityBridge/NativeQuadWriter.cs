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
        WriteQuad(
            voxelType,
            FaceTopology.GetNormal(direction),
            voxelLocalPosition + FaceTopology.GetUnitQuadCorner(direction, 0),
            voxelLocalPosition + FaceTopology.GetUnitQuadCorner(direction, 1),
            voxelLocalPosition + FaceTopology.GetUnitQuadCorner(direction, 2),
            voxelLocalPosition + FaceTopology.GetUnitQuadCorner(direction, 3));
    }

    // Greedy처럼 이미 병합된 큰 쿼드를 만들 때는,
    // 단위 face 대신 4개 꼭짓점을 직접 넘겨 같은 NativeList 형식으로 기록합니다.
    public void WriteMerged(FaceDirection direction, byte voxelType, Vec3 p0, Vec3 pU, Vec3 pV, Vec3 pUV)
    {
        WriteQuad(
            voxelType,
            FaceTopology.GetNormal(direction),
            GetCorner(FaceTopology.GetWindingCornerIndex(direction, 0), p0, pU, pV, pUV),
            GetCorner(FaceTopology.GetWindingCornerIndex(direction, 1), p0, pU, pV, pUV),
            GetCorner(FaceTopology.GetWindingCornerIndex(direction, 2), p0, pU, pV, pUV),
            GetCorner(FaceTopology.GetWindingCornerIndex(direction, 3), p0, pU, pV, pUV));
    }

    private void WriteQuad(byte voxelType, Vec3 normal, Vec3 a, Vec3 b, Vec3 c, Vec3 d)
    {
        int startIndex = Vertices.Length;

        AddVertex(voxelType, normal, a, 0);
        AddVertex(voxelType, normal, b, 1);
        AddVertex(voxelType, normal, c, 2);
        AddVertex(voxelType, normal, d, 3);

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

    // FaceTopology는 winding 순서를 "0,1,2,3 중 어느 꼭짓점을 쓸지"라는 숫자로 돌려줍니다.
    // WriteMerged는 이미 병합된 쿼드의 네 점(p0/pU/pV/pUV)을 직접 들고 있으므로,
    // 여기서 그 숫자를 실제 꼭짓점 좌표로 바꿔 winding 순서대로 기록합니다.
    private Vec3 GetCorner(int index, Vec3 p0, Vec3 pU, Vec3 pV, Vec3 pUV)
    {
        return index switch
        {
            0 => p0,
            1 => pU,
            2 => pV,
            _ => pUV
        };
    }
}
