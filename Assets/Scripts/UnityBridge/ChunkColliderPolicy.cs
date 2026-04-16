using System.Collections.Generic;
using UnityEngine;

// 청크 콜라이더를 어떤 범위까지 유지할지 결정하는 정책입니다.
// 플레이어가 속한 중심 청크 기준으로 반경 N 청크 안은 collider를 켜고,
// 그 밖은 끄는 단순한 격자 정책을 씁니다.
public sealed class ChunkColliderPolicy
{
    public void ApplyUsage(ChunkPos centerChunk, int colliderRadiusInChunks, IReadOnlyDictionary<ChunkPos, ChunkMeshController> renderers)
    {
        foreach ((ChunkPos chunkPos, ChunkMeshController controller) in renderers)
        {
            bool shouldUseCollider = ShouldUseCollider(controller.IsColliderActive, centerChunk, chunkPos, colliderRadiusInChunks);
            controller.SetColliderUsage(shouldUseCollider);
        }
    }

    private static bool ShouldUseCollider(bool isCurrentlyActive, ChunkPos centerChunk, ChunkPos chunkPos, int colliderRadiusInChunks)
    {
        int dx = Mathf.Abs(chunkPos.X - centerChunk.X);
        int dy = Mathf.Abs(chunkPos.Y - centerChunk.Y);
        int dz = Mathf.Abs(chunkPos.Z - centerChunk.Z);
        int gridDistance = Mathf.Max(dx, Mathf.Max(dy, dz));
        return gridDistance <= colliderRadiusInChunks;
    }
}
