using System.Collections.Generic;
using UnityEngine;

// 스트리밍 정책이 "무엇이 필요하냐"를 정한다면,
// 이 평가기( evaluator )는 "그중 무엇을 먼저 로드하냐"를 계산합니다.
// 카메라 프러스텀과 화면 중앙 우선순위 계산을 ChunkManager 밖으로 빼
// 스트리밍 조율과 시야 기반 점수 계산 책임을 분리합니다.
public sealed class ChunkStreamingPriorityEvaluator
{
    private readonly Plane[] frustumPlanes = new Plane[6];

    public void CollectVisibleChunkPositions(
        Camera cameraToUse,
        IReadOnlyCollection<ChunkPos> chunkPositions,
        HashSet<ChunkPos> visibleChunks)
    {
        visibleChunks.Clear();
        if (cameraToUse == null || chunkPositions.Count == 0)
        {
            return;
        }

        GeometryUtility.CalculateFrustumPlanes(cameraToUse, frustumPlanes);
        int chunkSize = ChunkData.DefaultSize;

        foreach (ChunkPos chunkPos in chunkPositions)
        {
            WorldPos origin = chunkPos.ToWorldOrigin(chunkSize);
            Vector3 chunkCenter = new Vector3(
                origin.X + (chunkSize * 0.5f),
                origin.Y + (chunkSize * 0.5f),
                origin.Z + (chunkSize * 0.5f));
            Bounds chunkBounds = new Bounds(chunkCenter, Vector3.one * chunkSize);

            if (GeometryUtility.TestPlanesAABB(frustumPlanes, chunkBounds))
            {
                visibleChunks.Add(chunkPos);
            }
        }
    }

    public void CollectScreenPriorityScores(
        Camera cameraToUse,
        IReadOnlyCollection<ChunkPos> chunkPositions,
        Dictionary<ChunkPos, float> screenScores)
    {
        screenScores.Clear();
        if (cameraToUse == null || chunkPositions.Count == 0)
        {
            return;
        }

        int chunkSize = ChunkData.DefaultSize;

        foreach (ChunkPos chunkPos in chunkPositions)
        {
            WorldPos origin = chunkPos.ToWorldOrigin(chunkSize);
            Vector3 chunkCenter = new Vector3(
                origin.X + (chunkSize * 0.5f),
                origin.Y + (chunkSize * 0.5f),
                origin.Z + (chunkSize * 0.5f));
            Vector3 viewportPoint = cameraToUse.WorldToViewportPoint(chunkCenter);

            if (viewportPoint.z <= 0f)
            {
                screenScores[chunkPos] = float.NegativeInfinity;
                continue;
            }

            float dx = viewportPoint.x - 0.5f;
            float dy = viewportPoint.y - 0.5f;
            screenScores[chunkPos] = -((dx * dx) + (dy * dy));
        }
    }
}
