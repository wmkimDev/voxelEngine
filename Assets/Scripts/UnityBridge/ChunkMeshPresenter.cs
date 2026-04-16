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

    public void Initialize(Material sharedMaterial, Texture2D atlas)
    {
        material = sharedMaterial;
        voxelAtlas = atlas;
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

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        MeshCollider meshCollider = GetComponent<MeshCollider>();

        if (meshData.Vertices.Count == 0)
        {
            // 빈 청크는 collider를 "비활성화해 두는 대상"이 아니라,
            // 아예 충돌체가 없는 상태로 유지하는 대상입니다.
            // 다만 이전 프레임에는 지형이 있던 청크가 지금은 비게 될 수 있으므로,
            // 그런 전환 상황에서는 남아 있는 MeshCollider를 여기서 제거해 줘야 합니다.
            meshFilter.sharedMesh = null;
            meshRenderer.enabled = false;
            RemoveMeshCollider(meshCollider);
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

        // 실제 지형 면이 있는 청크만 collider를 가집니다.
        // 비어 있던 청크가 새로 지형을 갖게 된 경우에만 여기서 MeshCollider를 만들거나 재사용합니다.
        // Raycast 편집이 같은 형상의 collider를 읽도록 렌더 메시와 같은 데이터를 collider에도 넣습니다.
        meshCollider = GetOrCreateMeshCollider();
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = generatedMesh;

        VoxelPerformanceStats.RecordMeshRebuild(rebuildMilliseconds, meshData);
    }

    public void EnsureMaterial()
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

    private MeshCollider GetOrCreateMeshCollider()
    {
        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            // 빈 청크에는 붙이지 않고, 실제 메시가 생긴 시점에만 충돌체를 추가합니다.
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }

        meshCollider.enabled = true;
        return meshCollider;
    }

    private void RemoveMeshCollider(MeshCollider meshCollider)
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
    }
}
