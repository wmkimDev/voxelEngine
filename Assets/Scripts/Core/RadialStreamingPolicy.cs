using System;
using System.Collections.Generic;

public sealed class RadialStreamingPolicy : IChunkStreamingPolicy
{
    // Y 방향은 아직 고정 레이어 범위만 유지하고,
    // X/Z만 플레이어 기준 원형 반경으로 계산합니다.
    private readonly int horizontalRadius;
    private readonly int minLayerY;
    private readonly int maxLayerY;

    public RadialStreamingPolicy(int horizontalRadius, int minLayerY, int maxLayerY)
    {
        this.horizontalRadius = Math.Max(0, horizontalRadius);
        this.minLayerY = Math.Min(minLayerY, maxLayerY);
        this.maxLayerY = Math.Max(minLayerY, maxLayerY);
    }

    public IReadOnlyCollection<ChunkPos> GetRequiredChunks(ChunkPos centerChunk)
    {
        var requiredChunks = new List<ChunkPos>();
        int radiusSquared = horizontalRadius * horizontalRadius;

        for (int y = minLayerY; y <= maxLayerY; y++)
        {
            for (int z = -horizontalRadius; z <= horizontalRadius; z++)
            {
                for (int x = -horizontalRadius; x <= horizontalRadius; x++)
                {
                    // 대각선 끝까지 모두 채우는 사각형 대신,
                    // 중심으로부터 실제 거리가 반경 안인 청크만 남깁니다.
                    if ((x * x) + (z * z) > radiusSquared)
                    {
                        continue;
                    }

                    requiredChunks.Add(new ChunkPos(centerChunk.X + x, y, centerChunk.Z + z));
                }
            }
        }

        return requiredChunks;
    }
}
