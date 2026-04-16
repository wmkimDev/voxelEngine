public enum FaceDirection
{
    PositiveX = 0,
    NegativeX = 1,
    PositiveY = 2,
    NegativeY = 3,
    PositiveZ = 4,
    NegativeZ = 5,
}

// voxel 면 방향별 공용 topology 규칙입니다.
// 각 면의 normal, 단위 쿼드 꼭짓점, winding 순서를 한곳에 모아
// Naive/Greedy/Job 메셔가 같은 face 정의를 공유하게 합니다.
public static class FaceTopology
{
    // 엔진 전체에서 쓰는 "면 방향 -> 법선" 공식 정의입니다.
    // Naive, Greedy, 이후 다른 메셔도 같은 방향 규칙을 공유합니다.
    private static readonly Vec3[] Normals =
    {
        new Vec3(1, 0, 0),
        new Vec3(-1, 0, 0),
        new Vec3(0, 1, 0),
        new Vec3(0, -1, 0),
        new Vec3(0, 0, 1),
        new Vec3(0, 0, -1),
    };

    // voxel 하나 기준으로 각 면을 이루는 네 꼭짓점입니다.
    // 이 정의 덕분에 NaiveMeshBuilder는 각 면의 상대 좌표를 공용 규칙에서 가져옵니다.
    private static readonly Vec3[,] UnitQuadCorners =
    {
        { new Vec3(1, 0, 0), new Vec3(1, 1, 0), new Vec3(1, 1, 1), new Vec3(1, 0, 1) }, // +X
        { new Vec3(0, 0, 1), new Vec3(0, 1, 1), new Vec3(0, 1, 0), new Vec3(0, 0, 0) }, // -X
        { new Vec3(0, 1, 1), new Vec3(1, 1, 1), new Vec3(1, 1, 0), new Vec3(0, 1, 0) }, // +Y
        { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 0, 1), new Vec3(0, 0, 1) }, // -Y
        { new Vec3(1, 0, 1), new Vec3(1, 1, 1), new Vec3(0, 1, 1), new Vec3(0, 0, 1) }, // +Z
        { new Vec3(0, 0, 0), new Vec3(0, 1, 0), new Vec3(1, 1, 0), new Vec3(1, 0, 0) }, // -Z
    };

    // Greedy처럼 큰 쿼드를 만들 때는 p0 / pU / pV / pUV 네 점을
    // 어떤 순서로 써야 방향별 winding이 올바른지 알아야 합니다.
    private static readonly int[,] QuadCornerOrder =
    {
        { 0, 1, 3, 2 }, // +X
        { 2, 3, 1, 0 }, // -X
        { 2, 3, 1, 0 }, // +Y
        { 0, 1, 3, 2 }, // -Y
        { 1, 3, 2, 0 }, // +Z
        { 0, 2, 3, 1 }, // -Z
    };

    public static Vec3 GetNormal(FaceDirection direction)
    {
        // 이 면 방향이 바라보는 법선 벡터를 돌려줍니다.
        // 이 값은 이웃 voxel 검사와 조명용 normal 기록에 함께 사용합니다.
        return Normals[(int)direction];
    }

    public static Vec3 GetUnitQuadCorner(FaceDirection direction, int cornerIndex)
    {
        // voxel 하나를 기준으로, 해당 면의 cornerIndex번째 꼭짓점 상대 좌표를 돌려줍니다.
        // NaiveMeshBuilder는 이 값을 voxel 위치에 더해서 실제 정점을 만듭니다.
        return UnitQuadCorners[(int)direction, cornerIndex];
    }

    public static int GetWindingCornerIndex(FaceDirection direction, int cornerIndex)
    {
        // Greedy처럼 p0 / pU / pV / pUV 네 점을 이미 알고 있을 때,
        // 그중 몇 번째 점을 어떤 순서로 써야 올바른 winding이 되는지 알려줍니다.
        return QuadCornerOrder[(int)direction, cornerIndex];
    }
}
