using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Pool;

public sealed class ChunkManager : MonoBehaviour
{
    [SerializeField] private Transform streamingTarget;
    [SerializeField] private VoxelWorldSettings worldSettings;
    [SerializeField] private Camera editCamera;
    [SerializeField] private bool alignStreamingTargetToSurfaceOnStart = true;
    [SerializeField] private float spawnHeightPadding = 0.05f;

    // 현재 로드되어 있는 청크 데이터와, 그 청크를 화면에 그리는 Unity 쪽 컨트롤러입니다.
    private readonly Dictionary<ChunkPos, ChunkData> chunks = new();
    private readonly Dictionary<ChunkPos, ChunkMeshBuildController> renderers = new();
    private readonly ChunkDataCache chunkDataCache = new(0);

    // 로드/메싱/충돌/편집처럼 ChunkManager가 협력하는 보조 객체들입니다.
    private IMeshBuilder meshBuilder;
    private IWorldGenerator worldGenerator;
    private ISurfaceHeightSampler surfaceHeightSampler;
    private readonly ChunkStreamer chunkStreamer = new();
    private readonly ChunkColliderUpdater chunkColliderUpdater = new();
    private VoxelEditController editController;
    private Func<ChunkPos, bool> processQueuedMeshBuildCallback;

    // 새 청크의 첫 메싱과 기존 청크 재빌드를 예산 안에서 처리하는 큐입니다.
    private readonly ChunkMeshBuildQueue meshBuildQueue = new();
    private ObjectPool<ChunkMeshBuildController> chunkControllerPool;

    public int LoadedChunkCount => chunks.Count;
    public Vector3 StreamingTargetPosition => GetStreamingTargetPosition();
    public ChunkPos StreamingTargetChunk => GetStreamingCenterChunk();
    public string ActiveMeshBuilderName => worldSettings != null
        ? worldSettings.ActiveMeshBuilderMode.ToString()
        : "Missing Settings";

    // 런타임 보조 객체와 풀을 미리 준비합니다.
    private void Awake()
    {
        processQueuedMeshBuildCallback ??= ProcessQueuedMeshBuild;
        EnsureChunkControllerPool();
        EnsureEditController();
    }

    // 월드 설정을 확인하고 첫 스트리밍 틱까지 초기화합니다.
    private void Start()
    {
        if (!EnsureWorldSettingsAssigned())
        {
            return;
        }

        InitializeRuntimeWorld();
    }

    // 매 프레임 스트리밍 적용, 재빌드 큐 소비, collider 갱신을 순서대로 처리합니다.
    private void Update()
    {
        UpdateStreaming(force: false);
        ProcessPendingRebuilds();
        UpdateColliderUsage();
    }

    // 인스펙터에서 수정된 값 중 런타임에 바로 반영 가능한 설정만 정리합니다.
    private void OnValidate()
    {
        spawnHeightPadding = Mathf.Max(0f, spawnHeightPadding);

        if (Application.isPlaying && worldSettings != null)
        {
            ApplyAtmosphereSettings();
        }
    }

    // 플레이 중 활성 collider 청크를 디버그 기즈모로 그립니다.
    private void OnDrawGizmos()
    {
        if (worldSettings == null || !Application.isPlaying)
        {
            return;
        }

        chunkColliderUpdater.DrawDebugGizmos(renderers);
    }

    // 매니저가 파괴될 때 런타임 상태와 네이티브 자원을 함께 정리합니다.
    private void OnDestroy()
    {
        DisposeMeshBuilder();
    }

    // 런타임 시작 시 필요한 월드 초기화 시퀀스를 수행합니다.
    private void InitializeRuntimeWorld()
    {
        ApplyAtmosphereSettings();
        VoxelPerformanceStats.Reset();

        // 캐시도 world settings를 단일 기준으로 따라가게 합니다.
        chunkDataCache.SetCapacity(worldSettings.CachedChunkCount);
        DisposeMeshBuilder();
        meshBuilder = CreateMeshBuilder();
        worldGenerator = CreateWorldGenerator();
        surfaceHeightSampler = worldGenerator as ISurfaceHeightSampler;
        ConfigureEditController();
        AlignStreamingTargetToSurface();
        RebuildStreamingPolicy();
        UpdateStreaming(force: true);
        ProcessPendingRebuilds();
        UpdateColliderUsage();
    }

    // VoxelEditController가 없으면 같은 오브젝트에 붙입니다.
    private void EnsureEditController()
    {
        if (editController == null && !TryGetComponent(out editController))
        {
            editController = gameObject.AddComponent<VoxelEditController>();
        }
    }

    // 현재 카메라/설정 기준으로 편집 컨트롤러를 다시 초기화합니다.
    private void ConfigureEditController()
    {
        EnsureEditController();
        editController.Initialize(editCamera, worldSettings, this, this);
    }

    // 현재 world settings에 맞는 스트리밍 정책을 다시 만듭니다.
    private void RebuildStreamingPolicy()
    {
        chunkStreamer.RebuildPolicies(worldSettings);
    }

    // 설정된 메셔 모드에 맞는 메시 빌더 구현체를 생성합니다.
    private IMeshBuilder CreateMeshBuilder()
    {
        return worldSettings.ActiveMeshBuilderMode switch
        {
            VoxelWorldSettings.MeshBuilderMode.Naive => new NaiveMeshBuilder(),
            VoxelWorldSettings.MeshBuilderMode.JobNaive => new JobSystemMeshBuilder(),
            VoxelWorldSettings.MeshBuilderMode.JobGreedy => new JobGreedyMeshBuilder(),
            _ => new GreedyMeshBuilder(),
        };
    }

    // IDisposable 메셔라면 네이티브 자원까지 함께 정리합니다.
    private void DisposeMeshBuilder()
    {
        if (meshBuilder is System.IDisposable disposableBuilder)
        {
            disposableBuilder.Dispose();
        }
    }

    // 현재 월드 설정으로 절차적 생성기를 만듭니다.
    private IWorldGenerator CreateWorldGenerator()
    {
        return new NoiseWorldGenerator(worldSettings.ToNoiseWorldGeneratorSettings());
    }

    // 필수 world settings 참조가 없으면 에러를 내고 매니저를 비활성화합니다.
    private bool EnsureWorldSettingsAssigned()
    {
        if (worldSettings != null)
        {
            return true;
        }

        Debug.LogError("ChunkManager requires a VoxelWorldSettings asset reference.", this);
        enabled = false;
        return false;
    }

    // world settings의 포그 값을 RenderSettings에 반영합니다.
    private void ApplyAtmosphereSettings()
    {
        RenderSettings.fog = worldSettings.EnableFog;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = worldSettings.FogColor;
        RenderSettings.fogStartDistance = worldSettings.FogStartDistance;
        RenderSettings.fogEndDistance = worldSettings.FogEndDistance;
    }

    // 스트리밍 기준이 되는 타깃 위치를 월드 좌표로 반환합니다.
    private Vector3 GetStreamingTargetPosition()
    {
        Transform target = streamingTarget != null ? streamingTarget : transform;
        return target.position;
    }

    // 현재 스트리밍 타깃 월드 위치를 청크 좌표로 변환합니다.
    private ChunkPos GetStreamingCenterChunk()
    {
        Vector3 targetPosition = GetStreamingTargetPosition();
        WorldPos worldPos = WorldPos.FromFloatsFloor(targetPosition.x, targetPosition.y, targetPosition.z);
        return worldPos.ToChunkPos(ChunkData.DefaultSize);
    }

    // ChunkStreamer가 계산한 load/unload/rebuild 결과를 실제 청크 상태에 반영합니다.
    private void UpdateStreaming(bool force)
    {
        VoxelPerformanceStats.RecordChunkLoadCount(0);

        if (worldGenerator == null)
        {
            worldGenerator = CreateWorldGenerator();
            surfaceHeightSampler = worldGenerator as ISurfaceHeightSampler;
        }

        // 런타임 중 설정값을 바꿔도 캐시 용량이 따라가도록 매 스트리밍 틱에서 동기화합니다.
        chunkDataCache.SetCapacity(worldSettings.CachedChunkCount);

        ChunkPos targetChunk = GetStreamingCenterChunk();
        Camera cameraToUse = editCamera != null ? editCamera : Camera.main;
        if (!chunkStreamer.Update(
                force,
                targetChunk,
                cameraToUse,
                worldSettings,
                chunks,
                out IReadOnlyList<ChunkPos> chunksToLoad,
                out IReadOnlyCollection<ChunkPos> chunksToUnload,
                out HashSet<ChunkPos> chunksNeedingRebuild))
        {
            return;
        }

        UnloadChunks(chunksToUnload);
        LoadRequiredChunks(chunksToLoad, chunksNeedingRebuild);
        QueueChunksNeedingMesh(chunksNeedingRebuild);
    }

    // 현재 중심 청크 기준으로 가까운 청크 collider만 유지하도록 갱신합니다.
    private void UpdateColliderUsage()
    {
        if (worldSettings == null || renderers.Count == 0)
        {
            return;
        }

        ChunkPos centerChunk = GetStreamingCenterChunk();
        chunkColliderUpdater.Update(centerChunk, worldSettings.ColliderDistanceInChunks, renderers);
    }

    // 외부에서 재빌드 큐를 즉시 한 번 소비하고 싶을 때 호출하는 진입점입니다.
    public void ProcessPendingRebuildsImmediately()
    {
        ProcessPendingRebuilds();
    }

    // 프레임 예산 안에서 초기 빌드/재빌드 큐를 소비합니다.
    private void ProcessPendingRebuilds()
    {
        VoxelPerformanceStats.RecordChunkRebuildCount(0);
        if (!meshBuildQueue.HasPendingBuilds)
        {
            return;
        }

        processQueuedMeshBuildCallback ??= ProcessQueuedMeshBuild;
        int maxChunkRebuildsPerFrame = Mathf.Max(0, worldSettings.MaxChunkRebuildsPerFrame);
        int rebuildsPerformed = meshBuildQueue.ProcessFrameBudget(maxChunkRebuildsPerFrame, processQueuedMeshBuildCallback);
        VoxelPerformanceStats.RecordChunkRebuildCount(rebuildsPerformed);
    }

    // 재빌드 큐에서 꺼낸 청크 하나를 실제 메시 빌드 요청으로 연결합니다.
    private bool ProcessQueuedMeshBuild(ChunkPos chunkPos)
    {
        if (!renderers.TryGetValue(chunkPos, out ChunkMeshBuildController renderer))
        {
            return false;
        }

        renderer.UpdateChunkNeighborhood(CreateNeighborhood(chunkPos));
        renderer.RequestMeshRebuild();
        return true;
    }

    // 시작 시 스트리밍 타깃을 현재 생성기 기준 지표면 위로 맞춥니다.
    private void AlignStreamingTargetToSurface()
    {
        if (!alignStreamingTargetToSurfaceOnStart || worldGenerator == null)
        {
            return;
        }

        Transform target = streamingTarget != null ? streamingTarget : transform;
        Vector3 targetPosition = target.position;
        int worldX = Mathf.FloorToInt(targetPosition.x);
        int worldZ = Mathf.FloorToInt(targetPosition.z);
        int surfaceHeight = surfaceHeightSampler?.GetSurfaceHeight(worldX, worldZ) ?? 0;

        // 플레이어/스트리밍 타깃의 위치는 발 위치 기준으로 보고 있으므로,
        // 표면 voxel의 윗면(surfaceHeight + 1)보다 살짝 위에 시작시킵니다.
        target.position = new Vector3(
            targetPosition.x,
            surfaceHeight + 1f + spawnHeightPadding,
            targetPosition.z);
    }

    // 이번 프레임 유지 범위 밖으로 나간 청크를 언로드하고 캐시에 반납합니다.
    private void UnloadChunks(IReadOnlyCollection<ChunkPos> chunksToUnload)
    {
        foreach (ChunkPos chunkPos in chunksToUnload)
        {
            if (chunks.TryGetValue(chunkPos, out ChunkData chunkData))
            {
                // 렌더 대상에서 빠진 청크를 바로 버리지 않고 메모리에 잠깐 남깁니다.
                // 이렇게 해두면 다시 같은 곳을 볼 때 생성기/노이즈 계산을 건너뛸 수 있습니다.
                chunkDataCache.Store(chunkPos, chunkData);
            }

            chunks.Remove(chunkPos);
            chunkStreamer.NotifyChunkUnloaded(chunkPos);

            if (renderers.TryGetValue(chunkPos, out ChunkMeshBuildController renderer))
            {
                renderers.Remove(chunkPos);
                ReleaseChunkMeshBuildController(renderer);
            }

            meshBuildQueue.Remove(chunkPos);
        }
    }

    // load plan에 따라 새 청크 데이터를 만들고 컨트롤러를 생성한 뒤 첫 메싱을 예약합니다.
    private void LoadRequiredChunks(
        IReadOnlyList<ChunkPos> loadPlan,
        HashSet<ChunkPos> chunksNeedingRebuild)
    {
        int loadCount = loadPlan.Count;
        VoxelPerformanceStats.RecordChunkLoadCount(loadCount);

        for (int i = 0; i < loadCount; i++)
        {
            // 이번 단계에서는 생성과 메시 빌드를 모두 메인 스레드에서 처리합니다.
            // 일부러 프레임당 개수를 제한해 끊김을 관찰하고, 다음 단계의 비동기화 필요성을 확인합니다.
            ChunkPos chunkPos = loadPlan[i];
            ChunkData chunkData = CreateChunkData(chunkPos);
            chunks.Add(chunkPos, chunkData);
            chunkStreamer.NotifyChunkLoaded(chunkPos);

            SpawnAndRegisterChunkMeshBuildController(chunkPos, CreateNeighborhood(chunkPos));
            QueueChunkInitialBuild(chunkPos);

            // 새 청크가 들어오면 그 청크와 맞닿은 기존 청크들의 경계 면 판정이 달라질 수 있습니다.
            // 새 청크 자신은 별도 초기 메싱 큐에서 처리하고, 여기서는 이웃만 다시 보게 합니다.
            AddAdjacentChunkPositions(chunkPos, chunksNeedingRebuild);
        }
    }

    // 경계 변화 등으로 메시 갱신이 필요한 청크들을 재빌드 큐에 넣습니다.
    private void QueueChunksNeedingMesh(HashSet<ChunkPos> chunksNeedingRebuild)
    {
        foreach (ChunkPos chunkPos in chunksNeedingRebuild)
        {
            QueueChunkRebuild(chunkPos);
        }
    }

    // 캐시를 우선 사용하고, 없으면 생성기를 돌려 새 ChunkData를 만듭니다.
    private ChunkData CreateChunkData(ChunkPos chunkPos)
    {
        if (chunkDataCache.TryTake(chunkPos, out ChunkData cachedChunkData))
        {
            // 최근에 언로드된 청크라면 생성기 대신 캐시된 데이터를 그대로 되돌립니다.
            return cachedChunkData;
        }

        var data = new ChunkData();
        // ChunkManager는 "언제 어떤 청크를 만들지"만 결정합니다.
        // 실제 voxel 배치는 IWorldGenerator 구현체가 담당합니다.
        worldGenerator.Generate(chunkPos, data);
        return data;
    }

    // 중심 청크와 6방향 이웃을 묶은 neighborhood 뷰를 만듭니다.
    private ChunkNeighborhood CreateNeighborhood(ChunkPos chunkPos)
    {
        // 청크 좌표 기준으로 바로 옆 6개만 연결합니다.
        // 대각선 청크는 현재 면 생성에는 필요하지 않으므로 포함하지 않습니다.
        return new ChunkNeighborhood(
            GetChunk(chunkPos),
            GetChunk(new ChunkPos(chunkPos.X + 1, chunkPos.Y, chunkPos.Z)),
            GetChunk(new ChunkPos(chunkPos.X - 1, chunkPos.Y, chunkPos.Z)),
            GetChunk(new ChunkPos(chunkPos.X, chunkPos.Y + 1, chunkPos.Z)),
            GetChunk(new ChunkPos(chunkPos.X, chunkPos.Y - 1, chunkPos.Z)),
            GetChunk(new ChunkPos(chunkPos.X, chunkPos.Y, chunkPos.Z + 1)),
            GetChunk(new ChunkPos(chunkPos.X, chunkPos.Y, chunkPos.Z - 1)));
    }

    // 현재 로드된 청크 데이터 딕셔너리에서 좌표에 맞는 청크를 조회합니다.
    private ChunkData GetChunk(ChunkPos chunkPos)
    {
        return chunks.GetValueOrDefault(chunkPos);
    }

    private void SpawnAndRegisterChunkMeshBuildController(ChunkPos chunkPos, ChunkNeighborhood neighborhood)
    {
        ChunkMeshBuildController renderer = AcquireChunkMeshBuildController();
        GameObject chunkObject = renderer.gameObject;
        chunkObject.name = $"Chunk ({chunkPos.X}, {chunkPos.Y}, {chunkPos.Z})";
        chunkObject.transform.SetParent(transform, worldPositionStays: false);
        WorldPos chunkOrigin = chunkPos.ToWorldOrigin(neighborhood.Size);
        chunkObject.transform.localPosition = new Vector3(chunkOrigin.X, chunkOrigin.Y, chunkOrigin.Z);

        renderer.Initialize(
            neighborhood,
            meshBuilder,
            worldSettings.Material,
            worldSettings.VoxelAtlas,
            rebuildImmediately: false);

        ChunkEditInteractor editInteractor = chunkObject.GetComponent<ChunkEditInteractor>();
        editInteractor.Initialize(
            neighborhood.Center,
            editedLocalPos => QueueAffectedChunksForEdit(chunkPos, editedLocalPos));

        renderers.Add(chunkPos, renderer);
    }

    // 풀에서 청크 메시 빌드 컨트롤러를 하나 가져옵니다.
    private ChunkMeshBuildController AcquireChunkMeshBuildController()
    {
        EnsureChunkControllerPool();
        return chunkControllerPool.Get();
    }

    // 컨트롤러 상태를 초기화한 뒤 풀에 반납합니다.
    private void ReleaseChunkMeshBuildController(ChunkMeshBuildController renderer)
    {
        renderer.ResetForPooling();
        chunkControllerPool.Release(renderer);
    }

    // 청크 컨트롤러 재사용용 ObjectPool을 지연 생성합니다.
    private void EnsureChunkControllerPool()
    {
        if (chunkControllerPool != null)
        {
            return;
        }

        chunkControllerPool = new ObjectPool<ChunkMeshBuildController>(
            CreatePooledChunkMeshBuildController,
            OnTakeChunkMeshBuildControllerFromPool,
            OnReturnChunkMeshBuildControllerToPool,
            OnDestroyPooledChunkMeshBuildController,
            collectionCheck: false,
            defaultCapacity: 32,
            maxSize: 8192);
    }

    // 풀에 들어갈 비활성 청크 컨트롤러 GameObject를 생성합니다.
    private ChunkMeshBuildController CreatePooledChunkMeshBuildController()
    {
        var chunkObject = new GameObject("Pooled Chunk");
        chunkObject.transform.SetParent(transform, worldPositionStays: false);
        ChunkMeshBuildController renderer = chunkObject.AddComponent<ChunkMeshBuildController>();
        chunkObject.AddComponent<ChunkEditInteractor>();
        chunkObject.SetActive(false);
        return renderer;
    }

    // 풀에서 꺼낸 컨트롤러 GameObject를 다시 활성화합니다.
    private static void OnTakeChunkMeshBuildControllerFromPool(ChunkMeshBuildController renderer)
    {
        if (renderer != null)
        {
            renderer.gameObject.SetActive(true);
        }
    }

    // 풀에 반납되는 컨트롤러 GameObject를 비활성화합니다.
    private static void OnReturnChunkMeshBuildControllerToPool(ChunkMeshBuildController renderer)
    {
        if (renderer != null)
        {
            renderer.gameObject.SetActive(false);
        }
    }

    // 풀 자체가 정리될 때 남은 컨트롤러 GameObject를 파괴합니다.
    private static void OnDestroyPooledChunkMeshBuildController(ChunkMeshBuildController renderer)
    {
        if (renderer != null)
        {
            Destroy(renderer.gameObject);
        }
    }

    // 한 청크에 맞닿은 6방향 이웃을 재빌드 후보 집합에 추가합니다.
    private void AddAdjacentChunkPositions(ChunkPos chunkPos, HashSet<ChunkPos> chunksNeedingRebuild)
    {
        foreach (ChunkPos offset in ChunkPos.OrthogonalOffsets)
        {
            chunksNeedingRebuild.Add(chunkPos + offset);
        }
    }

    // 블록 편집이 현재 청크와 경계 이웃에 미치는 재빌드 영향을 전파합니다.
    private void QueueAffectedChunksForEdit(ChunkPos chunkPos, LocalPos editedLocalPos)
    {
        ChunkData chunkData = GetChunk(chunkPos);
        if (chunkData == null)
        {
            return;
        }

        int edge = chunkData.Size - 1;

        // 사용자가 방금 편집한 현재 청크는 바로 다시 보이는 게 중요하므로 즉시 재빌드합니다.
        RebuildChunkImmediately(chunkPos);

        // 경계 voxel이 바뀌면 이웃 청크의 경계 면 노출 여부도 달라집니다.
        // 그래서 경계에 닿았을 때는 맞닿은 이웃도 함께 큐에 넣습니다.
        if (editedLocalPos.X == 0)
        {
            QueueChunkRebuild(new ChunkPos(chunkPos.X - 1, chunkPos.Y, chunkPos.Z));
        }

        if (editedLocalPos.X == edge)
        {
            QueueChunkRebuild(new ChunkPos(chunkPos.X + 1, chunkPos.Y, chunkPos.Z));
        }

        if (editedLocalPos.Y == 0)
        {
            QueueChunkRebuild(new ChunkPos(chunkPos.X, chunkPos.Y - 1, chunkPos.Z));
        }

        if (editedLocalPos.Y == edge)
        {
            QueueChunkRebuild(new ChunkPos(chunkPos.X, chunkPos.Y + 1, chunkPos.Z));
        }

        if (editedLocalPos.Z == 0)
        {
            QueueChunkRebuild(new ChunkPos(chunkPos.X, chunkPos.Y, chunkPos.Z - 1));
        }

        if (editedLocalPos.Z == edge)
        {
            QueueChunkRebuild(new ChunkPos(chunkPos.X, chunkPos.Y, chunkPos.Z + 1));
        }
    }

    // 이미 존재하는 청크의 일반 재빌드를 큐에 넣습니다.
    private void QueueChunkRebuild(ChunkPos chunkPos)
    {
        if (renderers.ContainsKey(chunkPos))
        {
            meshBuildQueue.QueueRebuild(chunkPos);
        }
    }

    // 새로 생성된 청크의 첫 메시 빌드를 초기 빌드 큐에 넣습니다.
    private void QueueChunkInitialBuild(ChunkPos chunkPos)
    {
        if (!renderers.ContainsKey(chunkPos))
        {
            return;
        }

        meshBuildQueue.QueueInitialBuild(chunkPos);
    }

    // 편집 직후 현재 청크처럼 즉시 반영이 필요한 경우 바로 neighborhood를 갱신하고 rebuild를 요청합니다.
    private void RebuildChunkImmediately(ChunkPos chunkPos)
    {
        if (!renderers.TryGetValue(chunkPos, out ChunkMeshBuildController renderer))
        {
            return;
        }

        renderer.UpdateChunkNeighborhood(CreateNeighborhood(chunkPos));
        renderer.RequestMeshRebuild();
    }

}
