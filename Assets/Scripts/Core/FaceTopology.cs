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
    public static Vec3 GetNormal(FaceDirection direction)
    {
        // 이 면 방향이 바라보는 법선 벡터를 돌려줍니다.
        // 이 값은 이웃 voxel 검사와 조명용 normal 기록에 함께 사용합니다.
        return direction switch
        {
            FaceDirection.PositiveX => new Vec3(1, 0, 0),
            FaceDirection.NegativeX => new Vec3(-1, 0, 0),
            FaceDirection.PositiveY => new Vec3(0, 1, 0),
            FaceDirection.NegativeY => new Vec3(0, -1, 0),
            FaceDirection.PositiveZ => new Vec3(0, 0, 1),
            _ => new Vec3(0, 0, -1),
        };
    }

    public static Vec3 GetUnitQuadCorner(FaceDirection direction, int cornerIndex)
    {
        // voxel 하나를 기준으로, 해당 면의 cornerIndex번째 꼭짓점 상대 좌표를 돌려줍니다.
        // NaiveMeshBuilder는 이 값을 voxel 위치에 더해서 실제 정점을 만듭니다.
        return direction switch
        {
            FaceDirection.PositiveX => cornerIndex switch
            {
                0 => new Vec3(1, 0, 0),
                1 => new Vec3(1, 1, 0),
                2 => new Vec3(1, 1, 1),
                _ => new Vec3(1, 0, 1),
            },
            FaceDirection.NegativeX => cornerIndex switch
            {
                0 => new Vec3(0, 0, 1),
                1 => new Vec3(0, 1, 1),
                2 => new Vec3(0, 1, 0),
                _ => new Vec3(0, 0, 0),
            },
            FaceDirection.PositiveY => cornerIndex switch
            {
                0 => new Vec3(0, 1, 1),
                1 => new Vec3(1, 1, 1),
                2 => new Vec3(1, 1, 0),
                _ => new Vec3(0, 1, 0),
            },
            FaceDirection.NegativeY => cornerIndex switch
            {
                0 => new Vec3(0, 0, 0),
                1 => new Vec3(1, 0, 0),
                2 => new Vec3(1, 0, 1),
                _ => new Vec3(0, 0, 1),
            },
            FaceDirection.PositiveZ => cornerIndex switch
            {
                0 => new Vec3(1, 0, 1),
                1 => new Vec3(1, 1, 1),
                2 => new Vec3(0, 1, 1),
                _ => new Vec3(0, 0, 1),
            },
            _ => cornerIndex switch
            {
                0 => new Vec3(0, 0, 0),
                1 => new Vec3(0, 1, 0),
                2 => new Vec3(1, 1, 0),
                _ => new Vec3(1, 0, 0),
            },
        };
    }

    public static int GetWindingCornerIndex(FaceDirection direction, int cornerIndex)
    {
        // Greedy처럼 p0 / pU / pV / pUV 네 점을 이미 알고 있을 때,
        // 그중 몇 번째 점을 어떤 순서로 써야 올바른 winding이 되는지 알려줍니다.
        return direction switch
        {
            FaceDirection.PositiveX => cornerIndex switch
            {
                0 => 0,
                1 => 1,
                2 => 3,
                _ => 2,
            },
            FaceDirection.NegativeX => cornerIndex switch
            {
                0 => 2,
                1 => 3,
                2 => 1,
                _ => 0,
            },
            FaceDirection.PositiveY => cornerIndex switch
            {
                0 => 2,
                1 => 3,
                2 => 1,
                _ => 0,
            },
            FaceDirection.NegativeY => cornerIndex switch
            {
                0 => 0,
                1 => 1,
                2 => 3,
                _ => 2,
            },
            FaceDirection.PositiveZ => cornerIndex switch
            {
                0 => 1,
                1 => 3,
                2 => 2,
                _ => 0,
            },
            _ => cornerIndex switch
            {
                0 => 0,
                1 => 2,
                2 => 3,
                _ => 1,
            },
        };
    }
}
