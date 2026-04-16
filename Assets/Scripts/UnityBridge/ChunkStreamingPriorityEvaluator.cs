using System.Collections.Generic;
using UnityEngine;

// 스트리밍 정책이 "무엇이 필요하냐"를 정한다면,
// 이 평가기( evaluator )는 "그중 무엇을 먼저 로드하냐"를 계산합니다.
// 카메라 프러스텀과 화면 중앙 우선순위 계산을 ChunkManager 밖으로 빼
// 스트리밍 조율과 시야 기반 점수 계산 책임을 분리합니다.
public sealed class ChunkStreamingPriorityEvaluator
{
    public HashSet<ChunkPos> GetVisibleChunkPositions(Camera cameraToUse, IReadOnlyCollection<ChunkPos> chunkPositions)
    {
        if (cameraToUse == null || chunkPositions.Count == 0)
        {
            return null;
        }

        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cameraToUse);
        var visibleChunks = new HashSet<ChunkPos>();
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

        return visibleChunks;
    }

    public Dictionary<ChunkPos, float> GetScreenPriorityScores(Camera cameraToUse, IReadOnlyCollection<ChunkPos> chunkPositions)
    {
        if (cameraToUse == null || chunkPositions.Count == 0)
        {
            return null;
        }

        var screenScores = new Dictionary<ChunkPos, float>(chunkPositions.Count);
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

        return screenScores;
    }
}
