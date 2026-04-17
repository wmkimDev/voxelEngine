using System.Collections.Generic;
using UnityEngine;

// 청크 콜라이더를 어떤 범위까지 유지할지 결정하는 정책입니다.
// 플레이어가 속한 중심 청크 기준으로 반경 N 청크 안은 collider를 켜고,
// 그 밖은 끄는 단순한 격자 정책을 씁니다.
public sealed class ChunkColliderPolicy
{
    // 지난 프레임까지 collider를 켜 둔 청크 집합입니다.
    // 매 프레임 새 반경 박스와 비교해, 그대로 둘 청크와 꺼야 할 청크를 가려냅니다.
    private readonly HashSet<ChunkPos> activeChunks = new();

    // 이번 프레임 플레이어 주변 반경 안에 있어서 collider가 "있어야 하는" 청크 집합입니다.
    // 중심 청크 기준 박스를 매번 다시 만들어 현재 desired 상태를 표현합니다.
    private readonly HashSet<ChunkPos> desiredChunks = new();

    // activeChunks를 순회하면서 이번 프레임에 꺼야 할 청크만 따로 모아두는 임시 버퍼입니다.
    // HashSet을 순회하면서 동시에 수정할 수 없어서 한 번 분리합니다.
    private readonly List<ChunkPos> chunksToDisable = new();

    public void ApplyUsage(ChunkPos centerChunk, int colliderRadiusInChunks, IReadOnlyDictionary<ChunkPos, ChunkMeshController> renderers)
    {
        // 1. 이번 프레임에 collider가 켜져 있어야 하는 청크 박스를 다시 계산합니다.
        desiredChunks.Clear();
        CollectChunkBox(centerChunk, colliderRadiusInChunks, desiredChunks);

        // 2. desired에 포함된 청크는 전부 collider on을 보장합니다.
        foreach (ChunkPos chunkPos in desiredChunks)
        {
            if (!renderers.TryGetValue(chunkPos, out ChunkMeshController controller))
            {
                continue;
            }

            controller.SetColliderUsage(true);
            activeChunks.Add(chunkPos);
        }

        // 3. 지난 프레임에는 active였지만, 이번 desired 박스에서 빠진 청크만 off 후보로 모읍니다.
        chunksToDisable.Clear();
        foreach (ChunkPos chunkPos in activeChunks)
        {
            if (desiredChunks.Contains(chunkPos) && renderers.ContainsKey(chunkPos))
            {
                continue;
            }

            chunksToDisable.Add(chunkPos);
        }

        // 4. 박스 밖으로 밀려난 청크만 collider를 끄고 active 집합에서도 제거합니다.
        foreach (ChunkPos chunkPos in chunksToDisable)
        {
            if (renderers.TryGetValue(chunkPos, out ChunkMeshController controller))
            {
                controller.SetColliderUsage(false);
            }

            activeChunks.Remove(chunkPos);
        }
    }

    // 중심 청크를 기준으로 반경 N짜리 축 정렬 청크 박스를 만들어 results에 채웁니다.
    // 여기서는 거리 계산 대신 "주변 청크 박스"를 그대로 collider 유지 범위로 씁니다.
    private static void CollectChunkBox(ChunkPos centerChunk, int colliderRadiusInChunks, HashSet<ChunkPos> results)
    {
        for (int x = centerChunk.X - colliderRadiusInChunks; x <= centerChunk.X + colliderRadiusInChunks; x++)
        {
            for (int y = centerChunk.Y - colliderRadiusInChunks; y <= centerChunk.Y + colliderRadiusInChunks; y++)
            {
                for (int z = centerChunk.Z - colliderRadiusInChunks; z <= centerChunk.Z + colliderRadiusInChunks; z++)
                {
                    results.Add(new ChunkPos(x, y, z));
                }
            }
        }
    }
}
