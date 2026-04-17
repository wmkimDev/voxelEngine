using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class ChunkMeshPresenter : MonoBehaviour
{
    [SerializeField] private Material material;
    [SerializeField] private Texture2D voxelAtlas;

    private Mesh generatedMesh;
    private readonly List<Vector3> unityVertices = new();
    private readonly List<Vector3> unityNormals = new();
    private readonly List<Vector2> unityUvs = new();
    private bool shouldUseCollider = false;
    private bool hasRenderableMesh;
    private MeshFilter cachedMeshFilter;
    private MeshRenderer cachedMeshRenderer;
    private MeshCollider cachedMeshCollider;

    public bool IsColliderActive => cachedMeshCollider != null && cachedMeshCollider.enabled;
    public bool HasActiveColliderMesh => cachedMeshCollider != null
        && cachedMeshCollider.enabled
        && cachedMeshCollider.sharedMesh != null;

    public bool HasRenderableMesh => hasRenderableMesh;

    public void Initialize(Material sharedMaterial, Texture2D atlas)
    {
        material = sharedMaterial;
        voxelAtlas = atlas;
        CacheComponents();
        EnsureMaterial();
    }

    public void ApplyMeshData(ChunkMeshData meshData, double rebuildMilliseconds)
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

        CacheComponents();
        MeshFilter meshFilter = cachedMeshFilter;
        MeshRenderer meshRenderer = cachedMeshRenderer;
        MeshCollider meshCollider = cachedMeshCollider;

        if (meshData.Vertices.Count == 0)
        {
            // 빈 청크는 collider를 "비활성화해 두는 대상"이 아니라,
            // 아예 충돌체가 없는 상태로 유지하는 대상입니다.
            // 다만 이전 프레임에는 지형이 있던 청크가 지금은 비게 될 수 있으므로,
            // 그런 전환 상황에서는 남아 있는 MeshCollider를 여기서 제거해 줘야 합니다.
            meshFilter.sharedMesh = null;
            meshRenderer.enabled = false;
            hasRenderableMesh = false;
            DestroyMeshCollider(meshCollider);
            VoxelPerformanceStats.RecordMeshRebuild(rebuildMilliseconds, meshData);
            return;
        }

        generatedMesh.SetVertices(unityVertices);
        generatedMesh.SetTriangles(meshData.Triangles, 0);
        generatedMesh.SetNormals(unityNormals);
        generatedMesh.SetUVs(0, unityUvs);
        generatedMesh.RecalculateBounds();

        meshFilter.sharedMesh = generatedMesh;
        meshRenderer.enabled = true;
        hasRenderableMesh = true;

        RefreshColliderState(meshCollider);

        VoxelPerformanceStats.RecordMeshRebuild(rebuildMilliseconds, meshData);
    }

    public void SetColliderUsage(bool shouldEnableCollider)
    {
        if (shouldUseCollider == shouldEnableCollider && ColliderStateMatchesRequest(shouldEnableCollider))
        {
            return;
        }

        shouldUseCollider = shouldEnableCollider;
        RefreshColliderState(cachedMeshCollider);
    }

    public void EnsureMaterial()
    {
        CacheComponents();
        MeshRenderer meshRenderer = cachedMeshRenderer;

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

    public void ResetForPooling()
    {
        CacheComponents();

        if (cachedMeshFilter != null)
        {
            cachedMeshFilter.sharedMesh = null;
        }

        if (cachedMeshRenderer != null)
        {
            cachedMeshRenderer.enabled = false;
        }

        if (cachedMeshCollider != null)
        {
            cachedMeshCollider.sharedMesh = null;
            cachedMeshCollider.enabled = false;
        }

        hasRenderableMesh = false;
        shouldUseCollider = false;
    }

    private void OnDestroy()
    {
        if (generatedMesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(generatedMesh);
        }
        else
        {
            DestroyImmediate(generatedMesh);
        }
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

    private void CacheComponents()
    {
        if (cachedMeshFilter == null)
        {
            cachedMeshFilter = GetComponent<MeshFilter>();
        }

        if (cachedMeshRenderer == null)
        {
            cachedMeshRenderer = GetComponent<MeshRenderer>();
        }

        if (cachedMeshCollider == null)
        {
            cachedMeshCollider = GetComponent<MeshCollider>();
        }
    }

    private MeshCollider GetOrCreateMeshCollider()
    {
        if (cachedMeshCollider == null)
        {
            // 빈 청크에는 붙이지 않고, 실제 메시가 생긴 시점에만 충돌체를 추가합니다.
            cachedMeshCollider = gameObject.AddComponent<MeshCollider>();
        }

        return cachedMeshCollider;
    }

    private void RefreshColliderState(MeshCollider meshCollider)
    {
        if (!hasRenderableMesh || generatedMesh == null)
        {
            DestroyMeshCollider(meshCollider);
            return;
        }

        if (!shouldUseCollider)
        {
            // 이미 생성된 지형 청크는 거리 정책에 따라 collider를 껐다 켭니다.
            // 이렇게 하면 먼 청크에서 PhysX 비용은 줄이면서도, 가까워졌을 때 매번 컴포넌트를 다시
            // 생성/제거하는 부담은 피할 수 있습니다.
            DisableMeshCollider(meshCollider);
            return;
        }

        // 실제 지형 면이 있고, 현재 청크가 "충돌을 유지할 만큼 가까운 범위"에 있을 때만
        // MeshCollider를 붙입니다. 이렇게 하면 먼 청크는 렌더만 남기고 PhysX 갱신 비용을 줄일 수 있습니다.
        meshCollider = GetOrCreateMeshCollider();
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = generatedMesh;
        meshCollider.enabled = true;
    }

    private void DisableMeshCollider(MeshCollider meshCollider)
    {
        if (meshCollider == null)
        {
            return;
        }

        meshCollider.enabled = false;
    }

    private void DestroyMeshCollider(MeshCollider meshCollider)
    {
        if (meshCollider == null)
        {
            return;
        }

        meshCollider.sharedMesh = null;

        if (Application.isPlaying)
        {
            Destroy(meshCollider);
        }
        else
        {
            DestroyImmediate(meshCollider);
        }

        cachedMeshCollider = null;
    }

    private bool ColliderStateMatchesRequest(bool shouldEnableCollider)
    {
        if (!shouldEnableCollider)
        {
            return cachedMeshCollider == null || !cachedMeshCollider.enabled;
        }

        return hasRenderableMesh
            && generatedMesh != null
            && cachedMeshCollider != null
            && cachedMeshCollider.enabled
            && cachedMeshCollider.sharedMesh == generatedMesh;
    }
}
