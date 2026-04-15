using System.Collections.Generic;
using System;

public sealed class SquareStreamingPolicy : IChunkStreamingPolicy
{
    // 현재 단계에서는 Y 방향 스트리밍은 단순한 고정 레이어 범위로 둡니다.
    // 나중에 동굴/높은 산/비행 이동을 다루면 플레이어 Y 기준 반경으로 바꿀 수 있습니다.
    private readonly int horizontalRadius;
    private readonly int minLayerY;
    private readonly int maxLayerY;

    public SquareStreamingPolicy(int horizontalRadius, int minLayerY, int maxLayerY)
    {
        this.horizontalRadius = Math.Max(0, horizontalRadius);
        this.minLayerY = Math.Min(minLayerY, maxLayerY);
        this.maxLayerY = Math.Max(minLayerY, maxLayerY);
    }

    public IReadOnlyCollection<ChunkPos> GetRequiredChunks(ChunkPos centerChunk)
    {
        var requiredChunks = new List<ChunkPos>();

        for (int y = minLayerY; y <= maxLayerY; y++)
        {
            for (int z = -horizontalRadius; z <= horizontalRadius; z++)
            {
                for (int x = -horizontalRadius; x <= horizontalRadius; x++)
                {
                    // 반경 안의 X/Z 청크를 사각형으로 모두 로드합니다.
                    // 반경 1이면 중앙 포함 3x3이 되므로 대각선 이동 때도 빈 공간이 덜 보입니다.
                    requiredChunks.Add(new ChunkPos(centerChunk.X + x, y, centerChunk.Z + z));
                }
            }
        }

        return requiredChunks;
    }
}
