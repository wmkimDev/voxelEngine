using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(ChunkMeshController))]
public sealed class ChunkEditInteractor : MonoBehaviour
{
    [SerializeField] private Camera editCamera;
    [SerializeField] private float editDistance = 30f;
    [SerializeField] private byte placeVoxelType = VoxelType.Grass;

    private IChunkDataStore chunkData;
    private Action<LocalPos> voxelEdited;
    private Action rebuildMesh;

    public void Initialize(
        IChunkDataStore data,
        Camera camera,
        float maxEditDistance,
        byte voxelTypeToPlace,
        Action<LocalPos> onVoxelEdited,
        Action onRebuildMesh)
    {
        chunkData = data;
        editCamera = camera;
        editDistance = Mathf.Max(0.1f, maxEditDistance);
        placeVoxelType = (byte)Mathf.Clamp(voxelTypeToPlace, VoxelType.Dirt, VoxelType.Sand);
        voxelEdited = onVoxelEdited;
        rebuildMesh = onRebuildMesh;
    }

    public void SetChunkData(IChunkDataStore data)
    {
        chunkData = data;
    }

    private void Update()
    {
        HandleMouseEdit();
    }

    private void OnValidate()
    {
        editDistance = Mathf.Max(0.1f, editDistance);
        placeVoxelType = (byte)Mathf.Clamp(placeVoxelType, VoxelType.Dirt, VoxelType.Sand);
    }

    private void HandleMouseEdit()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            TryEditVoxel(place: false);
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            TryEditVoxel(place: true);
        }
    }

    private void TryEditVoxel(bool place)
    {
        if (chunkData == null)
        {
            return;
        }

        Camera cameraToUse = editCamera != null ? editCamera : Camera.main;
        if (cameraToUse == null)
        {
            UnityEngine.Debug.LogError($"{nameof(editCamera)} is required because no MainCamera was found.", this);
            return;
        }

        // 조준점은 화면 중앙에 있으므로 Raycast도 화면 중앙에서 발사합니다.
        Ray ray = cameraToUse.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, editDistance))
        {
            return;
        }

        // 다른 collider를 클릭한 경우 이 청크의 voxel은 편집하지 않습니다.
        if (hit.collider.gameObject != gameObject)
        {
            return;
        }

        if (!TryGetVoxelCoordinateFromHit(hit, place, out LocalPos localPos))
        {
            return;
        }

        byte nextValue = place ? placeVoxelType : VoxelType.Air;
        if (chunkData.GetVoxel(localPos) == nextValue)
        {
            return;
        }

        chunkData.SetVoxel(localPos, nextValue);

        // 지금은 가장 단순하게 전체 메시를 다시 만듭니다.
        // voxel 하나만 바꿔도 전체 청크 메시와 collider를 다시 만드는 비용이 발생합니다.
        rebuildMesh?.Invoke();
        voxelEdited?.Invoke(localPos);
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
