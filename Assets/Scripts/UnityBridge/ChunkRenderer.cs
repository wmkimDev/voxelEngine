using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

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

    private IMeshBuilder meshBuilder = new NaiveMeshBuilder();
    private IChunkDataStore chunkData;
    private ChunkNeighborhood neighborhood;
    private Mesh generatedMesh;
    private readonly List<Vector3> unityVertices = new();
    private readonly List<Vector3> unityNormals = new();
    private readonly List<Vector2> unityUvs = new();
    private readonly MeshBuildState meshBuildState = new();

    public void Initialize(
        ChunkNeighborhood chunkNeighborhood,
        IMeshBuilder builder,
        Material sharedMaterial,
        Texture2D atlas)
    {
        neighborhood = chunkNeighborhood;
        chunkData = chunkNeighborhood.Center;
        meshBuilder = builder ?? new NaiveMeshBuilder();
        material = sharedMaterial;
        voxelAtlas = atlas;

        RebuildMesh();
        EnsureMaterial();
    }

    public void UpdateNeighborhood(ChunkNeighborhood chunkNeighborhood)
    {
        neighborhood = chunkNeighborhood;
        chunkData = chunkNeighborhood.Center;

        if (TryGetComponent(out ChunkEditInteractor editInteractor))
        {
            editInteractor.SetChunkData(chunkData);
        }
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

}
