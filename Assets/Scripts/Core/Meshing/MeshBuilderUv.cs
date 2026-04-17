public static class MeshBuilderUv
{
    public static Vec2 GetAtlasUv(byte voxelType, Vec2 faceUv)
    {
        // voxel 타입마다 아틀라스의 어느 칸을 쓸지 정합니다.
        // 현재 아틀라스는 가로 4칸이고, 각 칸이 Dirt/Grass/Stone/Sand 텍스처입니다.
        int tileIndex = voxelType switch
        {
            VoxelType.Dirt => 0,
            VoxelType.Grass => 1,
            VoxelType.Stone => 2,
            VoxelType.Sand => 3,
            _ => 0
        };

        const float tileCount = 4f;
        float tileWidth = 1f / tileCount;
        float padding = 0.01f;

        // faceUv는 쿼드 하나 안에서의 좌표입니다. 보통 (0,0)~(1,1) 범위입니다.
        // 이 값을 전체 아틀라스 기준으로 그대로 쓰면 모든 타입이 같은 텍스처를 보게 됩니다.
        // 그래서 tileIndex가 가리키는 작은 칸 안으로 u 좌표를 옮깁니다.
        float u = (tileIndex * tileWidth) + Lerp(padding, tileWidth - padding, faceUv.X);

        // 아틀라스가 가로로만 나뉘어 있으므로 v는 전체 높이 0~1을 그대로 씁니다.
        // padding은 타일 경계의 색이 번지는 bleeding 현상을 줄이기 위한 여백입니다.
        float v = Lerp(padding, 1f - padding, faceUv.Y);

        return new Vec2(u, v);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }
}
