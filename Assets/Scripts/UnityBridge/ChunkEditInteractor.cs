using System;
using UnityEngine;

[RequireComponent(typeof(ChunkMeshController))]
public sealed class ChunkEditInteractor : MonoBehaviour
{
    private IChunkDataStore chunkData;
    private Action<LocalPos> voxelEdited;

    public void Initialize(
        IChunkDataStore data,
        Action<LocalPos> onVoxelEdited)
    {
        chunkData = data;
        voxelEdited = onVoxelEdited;
    }

    public void SetChunkData(IChunkDataStore data)
    {
        chunkData = data;
    }

    public bool TryEditFromHit(RaycastHit hit, bool place, byte voxelTypeToPlace)
    {
        if (chunkData == null)
        {
            return false;
        }

        if (!TryGetVoxelCoordinateFromHit(hit, place, out LocalPos localPos))
        {
            return false;
        }

        byte placeVoxel = (byte)Mathf.Clamp(voxelTypeToPlace, VoxelType.Dirt, VoxelType.Sand);
        byte nextValue = place ? placeVoxel : VoxelType.Air;
        if (chunkData.GetVoxel(localPos) == nextValue)
        {
            return false;
        }

        chunkData.SetVoxel(localPos, nextValue);
        voxelEdited?.Invoke(localPos);
        return true;
    }

    private bool TryGetVoxelCoordinateFromHit(RaycastHit hit, bool place, out LocalPos localPos)
    {
        // Raycast hit.point는 월드 좌표입니다.
        // 파기는 맞은 면의 안쪽 voxel을 골라야 하므로 normal 반대 방향으로 살짝 이동합니다.
        // 놓기는 맞은 면의 바깥쪽 빈 voxel을 골라야 하므로 normal 방향으로 살짝 이동합니다.
        Vector3 worldPoint = hit.point + (hit.normal * (place ? 0.01f : -0.01f));

        // 월드 좌표를 이 청크 오브젝트 기준 로컬 좌표로 바꿉니다.
        // 지금은 청크 원점이 voxel (0,0,0)의 시작점이라고 보고 floor로 voxel 인덱스를 구합니다.
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        localPos = LocalPos.FromFloatsFloor(localPoint.x, localPoint.y, localPoint.z);

        return chunkData.IsInsideChunk(localPos);
    }
}
