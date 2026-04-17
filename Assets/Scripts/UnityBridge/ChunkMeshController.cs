using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Diagnostics;
#endif

[RequireComponent(typeof(ChunkMeshPresenter))]
public sealed class ChunkMeshController : MonoBehaviour
{
    // 플레이 중 비동기 메시 빌드 상태를 한곳에 모아둡니다.
    // handle, 재빌드 재요청 여부, 측정용 스톱워치를 ChunkMeshController 필드로 흩뿌리지 않기 위한 묶음입니다.
    private sealed class MeshBuildState
    {
        // 현재 백그라운드에서 돌고 있는 메시 빌드 작업입니다.
        // null이 아니면 아직 Complete()되지 않은 작업이 있다는 뜻입니다.
        public IMeshBuildHandle PendingHandle;

        // 빌드가 끝나기 전에 청크 상태가 또 바뀌면 true로 표시합니다.
        // 현재 작업이 끝난 직후 최신 neighborhood로 한 번 더 Schedule하기 위해 씁니다.
        public bool RebuildRequestedWhilePending;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 메시 재빌드 시간을 측정해 HUD 통계에 기록하기 위한 스톱워치입니다.
        public readonly Stopwatch Stopwatch = new();
#endif
    }

    private IMeshBuilder meshBuilder = new NaiveMeshBuilder();
    private IChunkDataStore chunkData;
    private ChunkNeighborhood neighborhood;
    private ChunkMeshPresenter meshPresenter;
    private readonly MeshBuildState meshBuildState = new();

    public bool IsColliderActive => GetMeshPresenter().IsColliderActive;
    public bool HasActiveColliderMesh => GetMeshPresenter().HasActiveColliderMesh;
    public bool HasRenderableMesh => GetMeshPresenter().HasRenderableMesh;

    public void Initialize(
        ChunkNeighborhood chunkNeighborhood,
        IMeshBuilder builder,
        Material sharedMaterial,
        Texture2D atlas)
    {
        neighborhood = chunkNeighborhood;
        chunkData = chunkNeighborhood.Center;
        meshBuilder = builder ?? new NaiveMeshBuilder();
        meshPresenter = GetComponent<ChunkMeshPresenter>();
        meshPresenter.Initialize(sharedMaterial, atlas);

        RebuildMesh();
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
        GetComponent<ChunkMeshPresenter>().EnsureMaterial();
    }

    private void OnDestroy()
    {
        if (meshBuildState.PendingHandle != null)
        {
            // Job이 아직 끝나지 않았더라도 Complete()를 호출해 네이티브 메모리를 정리합니다.
            meshBuildState.PendingHandle.Complete();
            meshBuildState.PendingHandle = null;
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
            BeginMeshBuildTiming();
            IMeshBuildHandle immediateHandle = meshBuilder.Schedule(neighborhood);
            ChunkMeshData immediateMeshData = immediateHandle.Complete();
            double rebuildMilliseconds = EndMeshBuildTiming();
            GetMeshPresenter().ApplyMeshData(immediateMeshData, rebuildMilliseconds);
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

    public void SetColliderUsage(bool shouldEnableCollider)
    {
        GetMeshPresenter().SetColliderUsage(shouldEnableCollider);
    }

    private void ScheduleMeshBuild()
    {
        BeginMeshBuildTiming();
        meshBuildState.PendingHandle = meshBuilder.Schedule(neighborhood);
    }

    private void CompletePendingMeshBuild()
    {
        ChunkMeshData meshData = meshBuildState.PendingHandle.Complete();
        meshBuildState.PendingHandle = null;
        double rebuildMilliseconds = EndMeshBuildTiming();

        GetMeshPresenter().ApplyMeshData(meshData, rebuildMilliseconds);

        if (meshBuildState.RebuildRequestedWhilePending)
        {
            meshBuildState.RebuildRequestedWhilePending = false;
            ScheduleMeshBuild();
        }
    }
    
    private ChunkMeshPresenter GetMeshPresenter()
    {
        if (meshPresenter == null)
        {
            meshPresenter = GetComponent<ChunkMeshPresenter>();
        }

        return meshPresenter;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void BeginMeshBuildTiming()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        meshBuildState.Stopwatch.Restart();
#endif
    }

    private double EndMeshBuildTiming()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        meshBuildState.Stopwatch.Stop();
        return meshBuildState.Stopwatch.Elapsed.TotalMilliseconds;
#else
        return 0d;
#endif
    }
}
