using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public sealed class ChunkRenderer : MonoBehaviour
{
    [SerializeField] private Material material;
    [SerializeField] private Texture2D voxelAtlas;
    [SerializeField] private Camera editCamera;
    [SerializeField] private float editDistance = 30f;
    [SerializeField] private byte placeVoxelType = VoxelType.Grass;

    private readonly IMeshBuilder meshBuilder = new NaiveMeshBuilder();
    private IChunkDataStore chunkData;
    private Mesh generatedMesh;

    [ContextMenu("Reset Demo Chunk")]
    private void ResetDemoChunk()
    {
        chunkData = new ChunkData();
        FillHardcodedChunk(chunkData);
        RebuildMesh();
        EnsureMaterial();
    }

    private void Awake()
    {
        ResetDemoChunk();
    }

    private void Update()
    {
        HandleMouseEdit();
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
                    data.SetVoxel(x, y, z, GetVoxelTypeForHeight(y, data.Size));
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
                    data.SetVoxel(x, y, z, VoxelType.Air);
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
                    data.SetVoxel(x, y, z, VoxelType.Air);
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
                    data.SetVoxel(x, y, z, VoxelType.Sand);
                }
            }
        }
    }

    private void RebuildMesh()
    {
        if (chunkData == null)
        {
            return;
        }

        ChunkMeshData meshData = meshBuilder.Build(chunkData);

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

        generatedMesh.SetVertices(meshData.Vertices);
        generatedMesh.SetTriangles(meshData.Triangles, 0);
        generatedMesh.SetNormals(meshData.Normals);
        generatedMesh.SetUVs(0, meshData.Uvs);
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
                Debug.LogError(
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
            Debug.LogError($"{nameof(editCamera)} is required because no MainCamera was found.", this);
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

        if (!TryGetVoxelCoordinateFromHit(hit, place, out int x, out int y, out int z))
        {
            return;
        }

        byte nextValue = place ? placeVoxelType : VoxelType.Air;
        if (chunkData.GetVoxel(x, y, z) == nextValue)
        {
            return;
        }

        chunkData.SetVoxel(x, y, z, nextValue);

        // 지금은 가장 단순하게 전체 메시를 다시 만듭니다.
        // voxel 하나만 바꿔도 전체 청크 메시와 collider를 다시 만드는 비용이 발생합니다.
        RebuildMesh();
    }

    private bool TryGetVoxelCoordinateFromHit(RaycastHit hit, bool place, out int x, out int y, out int z)
    {
        // Raycast hit.point는 월드 좌표입니다.
        // 파기는 맞은 면의 안쪽 voxel을 골라야 하므로 normal 반대 방향으로 살짝 이동합니다.
        // 놓기는 맞은 면의 바깥쪽 빈 voxel을 골라야 하므로 normal 방향으로 살짝 이동합니다.
        Vector3 worldPoint = hit.point + (hit.normal * (place ? 0.01f : -0.01f));

        // 월드 좌표를 이 청크 오브젝트 기준 로컬 좌표로 바꿉니다.
        // 지금은 청크 원점이 voxel (0,0,0)의 시작점이라고 보고 floor로 voxel 인덱스를 구합니다.
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        x = Mathf.FloorToInt(localPoint.x);
        y = Mathf.FloorToInt(localPoint.y);
        z = Mathf.FloorToInt(localPoint.z);

        return chunkData != null && chunkData.IsInsideChunk(x, y, z);
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
