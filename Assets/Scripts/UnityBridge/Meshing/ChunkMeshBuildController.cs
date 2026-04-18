using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Diagnostics;
#endif

[RequireComponent(typeof(ChunkMeshPresenter))]
public sealed class ChunkMeshBuildController : MonoBehaviour
{
    // 플레이 중 비동기 메시 빌드 상태를 한곳에 모아둡니다.
    // handle, 재빌드 재요청 여부, 측정용 스톱워치를 ChunkMeshBuildController 필드로 흩뿌리지 않기 위한 묶음입니다.
    private sealed class MeshBuildState
    {
        // 현재 백그라운드에서 돌고 있는 메시 빌드 작업입니다.
        // null이 아니면 아직 Complete()되지 않은 작업이 있다는 뜻입니다.
        public IMeshBuildHandle PendingHandle;

        // 빌드가 끝나기 전에 청크 상태가 또 바뀌면 true로 표시합니다.
        // 현재 작업이 끝난 직후 최신 neighborhood로 한 번 더 Schedule하기 위해 씁니다.
        public bool RebuildRequestedWhilePending;

        // Job 완료 결과를 매번 새 ChunkMeshData로 만들지 않고 재사용하는 버퍼입니다.
        public readonly ChunkMeshData ReusableMeshData = new();

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

    public bool IsColliderActive => EnsureMeshPresenter().IsColliderActive;
    public bool HasActiveColliderMesh => EnsureMeshPresenter().HasActiveColliderMesh;
    public bool HasRenderableMesh => EnsureMeshPresenter().HasRenderableMesh;

    private void Awake()
    {
        SyncLateUpdateSubscription();
    }

    public void Initialize(
        ChunkNeighborhood chunkNeighborhood,
        IMeshBuilder builder,
        Material sharedMaterial,
        Texture2D atlas,
        bool rebuildImmediately = true)
    {
        neighborhood = chunkNeighborhood;
        chunkData = chunkNeighborhood.Center;
        meshBuilder = builder ?? new NaiveMeshBuilder();
        meshPresenter = GetComponent<ChunkMeshPresenter>();
        meshPresenter.Initialize(sharedMaterial, atlas);

        if (rebuildImmediately)
        {
            RequestMeshRebuild();
            return;
        }

        SyncLateUpdateSubscription();
    }

    public void UpdateChunkNeighborhood(ChunkNeighborhood chunkNeighborhood)
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

        ApplyCompletedMeshBuild();
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled || chunkData == null)
        {
            return;
        }

        RequestMeshRebuild();
        EnsureMeshPresenter().EnsureMaterial();
    }

    private void OnDestroy()
    {
        CompleteAndClearPendingBuild();
        SyncLateUpdateSubscription();
    }

    public void RequestMeshRebuild()
    {
        if (chunkData == null)
        {
            return;
        }

        // 에디터/비플레이 상태에서는 즉시 결과를 적용해 인스펙터와 씬 뷰 반응성을 유지합니다.
        if (!Application.isPlaying)
        {
            RebuildMeshInEditor();
            return;
        }

        RequestAsyncRebuild();
    }

    public void SetColliderUsage(bool shouldEnableCollider)
    {
        EnsureMeshPresenter().SetColliderUsage(shouldEnableCollider);
    }

    public void ResetForPooling()
    {
        CompleteAndClearPendingBuild();
        meshBuildState.RebuildRequestedWhilePending = false;
        chunkData = null;
        neighborhood = default;
        SyncLateUpdateSubscription();
        EnsureMeshPresenter().ResetForPooling();

        if (TryGetComponent(out ChunkEditInteractor editInteractor))
        {
            editInteractor.SetChunkData(null);
        }
    }

    // 에디터/비플레이 모드에서는 즉시 메시를 만들고 바로 presenter에 적용합니다.
    private void RebuildMeshInEditor()
    {
        BeginMeshBuildTiming();
        IMeshBuildHandle immediateHandle = meshBuilder.Schedule(neighborhood);
        ChunkMeshData immediateMeshData = immediateHandle.Complete();
        double rebuildMilliseconds = EndMeshBuildTiming();
        EnsureMeshPresenter().ApplyMeshData(immediateMeshData, rebuildMilliseconds);
    }

    // 플레이 중에는 pending 빌드와 재요청 여부를 고려해 비동기 rebuild를 예약합니다.
    private void RequestAsyncRebuild()
    {
        if (meshBuildState.PendingHandle != null)
        {
            // 빌드가 끝나기 전에 다시 요청이 오면, 완료 직후 최신 neighborhood로 한 번 더 스케줄합니다.
            meshBuildState.RebuildRequestedWhilePending = true;
            return;
        }

        StartMeshBuild();
    }

    private void StartMeshBuild()
    {
        BeginMeshBuildTiming();
        meshBuildState.PendingHandle = meshBuilder.Schedule(neighborhood);
        SyncLateUpdateSubscription();
    }

    private void ApplyCompletedMeshBuild()
    {
        ChunkMeshData meshData = meshBuildState.PendingHandle is JobSystemMeshBuildHandle jobHandle
            ? jobHandle.Complete(meshBuildState.ReusableMeshData)
            : meshBuildState.PendingHandle.Complete();

        meshBuildState.PendingHandle = null;
        double rebuildMilliseconds = EndMeshBuildTiming();

        EnsureMeshPresenter().ApplyMeshData(meshData, rebuildMilliseconds);

        if (meshBuildState.RebuildRequestedWhilePending)
        {
            meshBuildState.RebuildRequestedWhilePending = false;
            StartMeshBuild();
            return;
        }

        SyncLateUpdateSubscription();
    }
    
    private ChunkMeshPresenter EnsureMeshPresenter()
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

    // pending handle이 남아 있을 때는 Complete를 호출해 Job 쪽 네이티브 메모리를 함께 정리합니다.
    private void CompleteAndClearPendingBuild()
    {
        if (meshBuildState.PendingHandle == null)
        {
            return;
        }

        meshBuildState.PendingHandle.Complete();
        meshBuildState.PendingHandle = null;
    }

    private void SyncLateUpdateSubscription()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        enabled = meshBuildState.PendingHandle != null;
    }
}
