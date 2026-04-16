using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public sealed class ChunkRenderer : MonoBehaviour
{
    // 플레이 중 비동기 메시 빌드 상태를 한곳에 모아둡니다.
    // handle, 재빌드 재요청 여부, 측정용 스톱워치를 ChunkRenderer 필드로 흩뿌리지 않기 위한 묶음입니다.
    private sealed class MeshBuildState
    {
        // 현재 백그라운드에서 돌고 있는 메시 빌드 작업입니다.
        // null이 아니면 아직 Complete()되지 않은 작업이 있다는 뜻입니다.
        public IMeshBuildHandle PendingHandle;

        // 빌드가 끝나기 전에 청크 상태가 또 바뀌면 true로 표시합니다.
        // 현재 작업이 끝난 직후 최신 neighborhood로 한 번 더 Schedule하기 위해 씁니다.
        public bool RebuildRequestedWhilePending;

        // 메시 재빌드 시간을 측정해 HUD 통계에 기록하기 위한 스톱워치입니다.
        public readonly Stopwatch Stopwatch = new();
    }

    [SerializeField] private Material material;
    [SerializeField] private Texture2D voxelAtlas;
    [SerializeField] private Camera editCamera;
    [SerializeField] private float editDistance = 30f;
    [SerializeField] private byte placeVoxelType = VoxelType.Grass;

    private IMeshBuilder meshBuilder = new NaiveMeshBuilder();
    private IChunkDataStore chunkData;
    private ChunkNeighborhood neighborhood;
    private Action<LocalPos> voxelEdited;
    private Mesh generatedMesh;
    private readonly List<Vector3> unityVertices = new();
    private readonly List<Vector3> unityNormals = new();
    private readonly List<Vector2> unityUvs = new();
    private readonly MeshBuildState meshBuildState = new();

    [ContextMenu("Reset Demo Chunk")]
    private void ResetDemoChunk()
    {
        chunkData = new ChunkData();
        FillHardcodedChunk(chunkData);
        neighborhood = new ChunkNeighborhood(chunkData, null, null, null, null, null, null);
        voxelEdited = null;
        RebuildMesh();
        EnsureMaterial();
    }

    public void Initialize(
        ChunkNeighborhood chunkNeighborhood,
        IMeshBuilder builder,
        Camera camera,
        Material sharedMaterial,
        Texture2D atlas,
        byte voxelTypeToPlace,
        float maxEditDistance,
        Action<LocalPos> onVoxelEdited = null)
    {
        neighborhood = chunkNeighborhood;
        chunkData = chunkNeighborhood.Center;
        meshBuilder = builder ?? new NaiveMeshBuilder();
        editCamera = camera;
        material = sharedMaterial;
        voxelAtlas = atlas;
        placeVoxelType = voxelTypeToPlace;
        editDistance = Mathf.Max(0.1f, maxEditDistance);
        voxelEdited = onVoxelEdited;

        RebuildMesh();
        EnsureMaterial();
    }

    public void UpdateNeighborhood(ChunkNeighborhood chunkNeighborhood)
    {
        neighborhood = chunkNeighborhood;
        chunkData = chunkNeighborhood.Center;
    }

    private void Start()
    {
        if (chunkData == null)
        {
            ResetDemoChunk();
        }
    }

    private void Update()
    {
        HandleMouseEdit();
    }

    private void LateUpdate()
    {
        if (meshBuildState.PendingHandle == null || !meshBuildState.PendingHandle.IsCompleted)
        {
            return;
        }

        CompletePendingMeshBuild();
    }

    private void OnValidate()
    {
        editDistance = Mathf.Max(0.1f, editDistance);
        placeVoxelType = (byte)Mathf.Clamp(placeVoxelType, VoxelType.Dirt, VoxelType.Sand);

        if (!isActiveAndEnabled || chunkData == null)
        {
            return;
        }

        RebuildMesh();
        EnsureMaterial();
    }

    private void OnDestroy()
    {
        if (meshBuildState.PendingHandle != null)
        {
            // Job이 아직 끝나지 않았더라도 Complete()를 호출해 네이티브 메모리를 정리합니다.
            meshBuildState.PendingHandle.Complete();
            meshBuildState.PendingHandle = null;
        }

        if (generatedMesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(generatedMesh);
            }
            else
            {
                DestroyImmediate(generatedMesh);
            }
        }
    }

    private void FillHardcodedChunk(IChunkDataStore data)
    {
        // 먼저 y 높이에 따라 다른 voxel 타입으로 채웁니다.
        // 같은 byte 배열 안에서도 1은 Dirt, 2는 Grass, 3은 Stone처럼 의미를 나눌 수 있습니다.
        for (int z = 0; z < data.Size; z++)
        {
            for (int y = 0; y < data.Size; y++)
            {
                for (int x = 0; x < data.Size; x++)
                {
                    data.SetVoxel(new LocalPos(x, y, z), GetVoxelTypeForHeight(y, data.Size));
                }
            }
        }

        // 가운데를 파내서 빈 공간을 만듭니다.
        // 이 내부 공간과 맞닿는 voxel의 면은 화면에 보여야 합니다.
        for (int z = 2; z <= 5; z++)
        {
            for (int y = 2; y <= 5; y++)
            {
                for (int x = 2; x <= 5; x++)
                {
                    data.SetVoxel(new LocalPos(x, y, z), VoxelType.Air);
                }
            }
        }

        // 앞쪽으로 작은 터널을 뚫습니다.
        // Scene 뷰에서 내부 면과 외부 면을 함께 확인하기 쉽게 하기 위함입니다.
        for (int z = 0; z <= 2; z++)
        {
            for (int y = 2; y <= 4; y++)
            {
                for (int x = 3; x <= 4; x++)
                {
                    data.SetVoxel(new LocalPos(x, y, z), VoxelType.Air);
                }
            }
        }

        // 한쪽 모서리에 Sand voxel을 넣어 타입이 추가되어도 같은 메시 생성 로직을 쓰는지 확인합니다.
        for (int z = 5; z <= 7; z++)
        {
            for (int y = 1; y <= 3; y++)
            {
                for (int x = 0; x <= 2; x++)
                {
                    data.SetVoxel(new LocalPos(x, y, z), VoxelType.Sand);
                }
            }
        }
    }

    public void RebuildMesh()
    {
        if (chunkData == null)
        {
            return;
        }

        // 에디터/비플레이 상태에서는 즉시 결과를 적용해 인스펙터와 씬 뷰 반응성을 유지합니다.
        if (!Application.isPlaying)
        {
            meshBuildState.Stopwatch.Restart();
            IMeshBuildHandle immediateHandle = meshBuilder.Schedule(neighborhood);
            ChunkMeshData immediateMeshData = immediateHandle.Complete();
            meshBuildState.Stopwatch.Stop();
            ApplyMeshData(immediateMeshData, meshBuildState.Stopwatch.Elapsed.TotalMilliseconds);
            return;
        }

        if (meshBuildState.PendingHandle != null)
        {
            // 빌드가 끝나기 전에 다시 요청이 오면, 완료 직후 최신 neighborhood로 한 번 더 스케줄합니다.
            meshBuildState.RebuildRequestedWhilePending = true;
            return;
        }

        ScheduleMeshBuild();
    }

    private void ScheduleMeshBuild()
    {
        meshBuildState.Stopwatch.Restart();
        meshBuildState.PendingHandle = meshBuilder.Schedule(neighborhood);
    }

    private void CompletePendingMeshBuild()
    {
        ChunkMeshData meshData = meshBuildState.PendingHandle.Complete();
        meshBuildState.PendingHandle = null;
        meshBuildState.Stopwatch.Stop();

        ApplyMeshData(meshData, meshBuildState.Stopwatch.Elapsed.TotalMilliseconds);

        if (meshBuildState.RebuildRequestedWhilePending)
        {
            meshBuildState.RebuildRequestedWhilePending = false;
            ScheduleMeshBuild();
        }
    }

    private void ApplyMeshData(ChunkMeshData meshData, double rebuildMilliseconds)
    {
        if (generatedMesh == null)
        {
            generatedMesh = new Mesh
            {
                name = "Chunk Mesh"
            };
        }
        else
        {
            generatedMesh.Clear();
        }

        generatedMesh.indexFormat = meshData.Vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        ConvertMeshData(meshData);

        generatedMesh.SetVertices(unityVertices);
        generatedMesh.SetTriangles(meshData.Triangles, 0);
        generatedMesh.SetNormals(unityNormals);
        generatedMesh.SetUVs(0, unityUvs);
        generatedMesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = generatedMesh;

        // Raycast로 클릭한 voxel을 찾기 위해 MeshCollider도 같은 메시로 갱신합니다.
        // 매번 전체 메시를 다시 넣는 방식이라 비용이 큽니다. 지금 단계에서는 그 비용을 체감하는 것이 목적입니다.
        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }

        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = generatedMesh;

        VoxelPerformanceStats.RecordMeshRebuild(rebuildMilliseconds, meshData);
    }

    private void ConvertMeshData(ChunkMeshData meshData)
    {
        unityVertices.Clear();
        unityNormals.Clear();
        unityUvs.Clear();

        // Core의 메시 데이터는 Unity를 모르는 Vec2/Vec3입니다.
        // UnityBridge에서만 UnityEngine.Vector2/Vector3로 바꿔 Mesh API에 넘깁니다.
        foreach (Vec3 vertex in meshData.Vertices)
        {
            unityVertices.Add(new Vector3(vertex.X, vertex.Y, vertex.Z));
        }

        foreach (Vec3 normal in meshData.Normals)
        {
            unityNormals.Add(new Vector3(normal.X, normal.Y, normal.Z));
        }

        foreach (Vec2 uv in meshData.Uvs)
        {
            unityUvs.Add(new Vector2(uv.X, uv.Y));
        }
    }

    private void EnsureMaterial()
    {
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader != null)
            {
                material = new Material(shader)
                {
                    color = Color.white
                };
            }
        }

        if (material != null)
        {
            if (voxelAtlas == null)
            {
                UnityEngine.Debug.LogError(
                    $"{nameof(voxelAtlas)} is required. Assign voxel_atlas.png in the Inspector.",
                    this);
                return;
            }

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", voxelAtlas);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", voxelAtlas);
            }

            meshRenderer.sharedMaterial = material;
        }
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
        RebuildMesh();
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

        return chunkData != null && chunkData.IsInsideChunk(localPos);
    }

    private static byte GetVoxelTypeForHeight(int y, int chunkSize)
    {
        if (y == chunkSize - 1)
        {
            return VoxelType.Grass;
        }

        if (y <= 1)
        {
            return VoxelType.Stone;
        }

        return VoxelType.Dirt;
    }
}
