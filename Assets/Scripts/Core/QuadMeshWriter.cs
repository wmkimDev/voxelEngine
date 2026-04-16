public static class QuadMeshWriter
{
    // 쿼드 한 장의 네 정점이 텍스처 안에서 차지하는 기본 UV 모양입니다.
    // 실제 atlas 좌표는 MeshBuilderUv가 voxel 타입에 맞게 변환합니다.
    private static readonly Vec2[] FaceUvs =
    {
        new Vec2(0, 0),
        new Vec2(0, 1),
        new Vec2(1, 1),
        new Vec2(1, 0),
    };

    public static void Write(
        ChunkMeshData meshData,
        byte voxelType,
        Vec3 normal,
        Vec3 a,
        Vec3 b,
        Vec3 c,
        Vec3 d)
    {
        // 메셔는 "어떤 쿼드를 만들지"만 결정하고,
        // 실제 버퍼 기록 형식은 이 헬퍼가 공통으로 처리합니다.
        int startIndex = meshData.Vertices.Count;

        AddVertex(meshData, voxelType, normal, a, 0);
        AddVertex(meshData, voxelType, normal, b, 1);
        AddVertex(meshData, voxelType, normal, c, 2);
        AddVertex(meshData, voxelType, normal, d, 3);

        meshData.Triangles.Add(startIndex + 0);
        meshData.Triangles.Add(startIndex + 1);
        meshData.Triangles.Add(startIndex + 2);
        meshData.Triangles.Add(startIndex + 0);
        meshData.Triangles.Add(startIndex + 2);
        meshData.Triangles.Add(startIndex + 3);
    }

    private static void AddVertex(
        ChunkMeshData meshData,
        byte voxelType,
        Vec3 normal,
        Vec3 position,
        int uvIndex)
    {
        meshData.Vertices.Add(position);
        meshData.Normals.Add(normal);
        meshData.Uvs.Add(MeshBuilderUv.GetAtlasUv(voxelType, FaceUvs[uvIndex]));
    }
}
