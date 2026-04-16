using System.Collections.Generic;

// 청크 콜라이더를 어떤 범위까지 유지할지 결정하는 정책입니다.
// 지금은 단순 거리 기반이지만, 나중에 시야/플레이어 높이/물리 범위 조건이 붙어도
// ChunkManager 대신 이 클래스만 바꾸면 되도록 분리합니다.
public sealed class ChunkColliderPolicy
{
    public void ApplyUsage(ChunkPos centerChunk, int colliderRadiusInChunks, IReadOnlyDictionary<ChunkPos, ChunkMeshController> renderers)
    {
        int colliderRadiusSquared = colliderRadiusInChunks * colliderRadiusInChunks;

        foreach ((ChunkPos chunkPos, ChunkMeshController controller) in renderers)
        {
            bool shouldUseCollider = GetDistanceSquared(chunkPos, centerChunk) <= colliderRadiusSquared;
            controller.SetColliderUsage(shouldUseCollider);
        }
    }

    private static int GetDistanceSquared(ChunkPos a, ChunkPos b)
    {
        int dx = a.X - b.X;
        int dy = a.Y - b.Y;
        int dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
