using System.Collections.Generic;
using UnityEngine;

// 플레이어 중심 청크 기준으로 어떤 청크의 collider를 켜고 끌지 갱신하고,
// 그 결과를 디버그 기즈모로 시각화하는 전용 보조 객체입니다.
public sealed class ChunkColliderUpdater
{
    private readonly ChunkColliderPolicy chunkColliderPolicy = new();

    // 현재 중심 청크와 로드된 renderer 집합을 기준으로 collider on/off를 갱신합니다.
    public void Update(ChunkPos centerChunk, int colliderRadiusInChunks, IReadOnlyDictionary<ChunkPos, ChunkMeshController> renderers)
    {
        if (renderers.Count == 0)
        {
            return;
        }

        chunkColliderPolicy.ApplyUsage(centerChunk, colliderRadiusInChunks, renderers);
    }

    // 현재 실제 collider mesh가 살아 있는 청크만 Scene/Game 뷰 기즈모로 그립니다.
    public void DrawDebugGizmos(IReadOnlyDictionary<ChunkPos, ChunkMeshController> renderers)
    {
        int chunkSize = ChunkData.DefaultSize;
        foreach ((ChunkPos chunkPos, ChunkMeshController controller) in renderers)
        {
            if (!controller.HasActiveColliderMesh)
            {
                continue;
            }

            WorldPos origin = chunkPos.ToWorldOrigin(chunkSize);
            Vector3 chunkCenter = new Vector3(
                origin.X + (chunkSize * 0.5f),
                origin.Y + (chunkSize * 0.5f),
                origin.Z + (chunkSize * 0.5f));

            Vector3 size = Vector3.one * (chunkSize * 0.9f);
            Gizmos.color = new Color(0.1f, 1f, 0.25f, 0.55f);
            Gizmos.DrawWireCube(chunkCenter, size);
        }
    }
}
