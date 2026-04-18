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
    private readonly Dictionary<ChunkPos, ChunkMeshController> renderers = new();
    private readonly ChunkDataCache chunkDataCache = new(0);

    // 로드/메싱/충돌/편집처럼 ChunkManager가 협력하는 보조 객체들입니다.
    private IMeshBuilder meshBuilder;
    private IWorldGenerator worldGenerator;
    private readonly ChunkStreamer chunkStreamer = new();
    private readonly ChunkColliderUpdater chunkColliderUpdater = new();
    private VoxelEditController editController;
    private Func<ChunkPos, bool> processQueuedMeshBuildCallback;

    // 새 청크의 첫 메싱과 기존 청크 재빌드를 예산 안에서 처리하는 큐입니다.
    private readonly ChunkMeshBuildQueue meshBuildQueue = new();
    private ObjectPool<ChunkMeshController> chunkControllerPool;

    public int LoadedChunkCount => chunks.Count;
    public Vector3 StreamingTargetPosition => GetStreamingTargetPosition();
    public ChunkPos StreamingTargetChunk => GetStreamingCenterChunk();
    public string ActiveMeshBuilderName => worldSettings != null
        ? worldSettings.ActiveMeshBuilderMode.ToString()
        : "Missing Settings";

    private void Awake()
    {
        processQueuedMeshBuildCallback ??= ProcessQueuedMeshBuild;
        EnsureChunkControllerPool();
        EnsureEditController();
    }

    private void Start()
    {
        if (!EnsureWorldSettingsAssigned())
        {
            return;
        }

        ApplyAtmosphereSettings();
        VoxelPerformanceStats.Reset();
        // 캐시도 world settings를 단일 기준으로 따라가게 합니다.
        chunkDataCache.SetCapacity(worldSettings.CachedChunkCount);
        meshBuilder = CreateMeshBuilder();
        worldGenerator = CreateWorldGenerator();
        ConfigureEditController();
        AlignStreamingTargetToSurface();
        RebuildStreamingPolicy();
        UpdateStreaming(force: true);
        ProcessPendingRebuilds();
        UpdateColliderUsage();
    }

    private void Update()
    {
        UpdateStreaming(force: false);
        ProcessPendingRebuilds();
        UpdateColliderUsage();
    }

    private void OnValidate()
    {
        spawnHeightPadding = Mathf.Max(0f, spawnHeightPadding);

        if (Application.isPlaying && worldSettings != null)
        {
            ApplyAtmosphereSettings();
        }
    }

    private void OnDrawGizmos()
    {
        if (worldSettings == null || !Application.isPlaying)
        {
            return;
        }

        chunkColliderUpdater.DrawDebugGizmos(renderers);
    }

    [ContextMenu("Rebuild Streaming World")]
    private void BuildStreamingWorld()
    {
        if (!EnsureWorldSettingsAssigned())
        {
            return;
        }

        ApplyAtmosphereSettings();
        ClearRuntimeChunks();
        chunks.Clear();
        renderers.Clear();
        // 월드 전체를 다시 만들 때는 예전 청크 캐시도 함께 비웁니다.
        // 생성 규칙/메셔/시드가 달라질 수 있으므로 재사용하면 오히려 잘못된 데이터가 됩니다.
        chunkDataCache.Clear();
        chunkDataCache.SetCapacity(worldSettings.CachedChunkCount);
        DisposeMeshBuilder();
        meshBuilder = CreateMeshBuilder();
        worldGenerator = CreateWorldGenerator();
        ConfigureEditController();
        AlignStreamingTargetToSurface();
        RebuildStreamingPolicy();
        UpdateStreaming(force: true);
        ProcessPendingRebuilds();
        UpdateColliderUsage();
    }

    private void RebuildStreamingPolicy()
    {
        chunkStreamer.RebuildPolicies(worldSettings);
    }

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

    private void DisposeMeshBuilder()
    {
        if (meshBuilder is System.IDisposable disposableBuilder)
        {
            disposableBuilder.Dispose();
        }
    }

    private IWorldGenerator CreateWorldGenerator()
    {
        return new NoiseWorldGenerator(worldSettings.ToNoiseWorldGeneratorSettings());
    }

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

    private void ApplyAtmosphereSettings()
    {
        RenderSettings.fog = worldSettings.EnableFog;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = worldSettings.FogColor;
        RenderSettings.fogStartDistance = worldSettings.FogStartDistance;
        RenderSettings.fogEndDistance = worldSettings.FogEndDistance;
    }

    private void UpdateStreaming(bool force)
    {
        VoxelPerformanceStats.RecordChunkLoadCount(0);

        if (worldGenerator == null)
        {
            worldGenerator = CreateWorldGenerator();
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

    private void UpdateColliderUsage()
    {
        if (worldSettings == null || renderers.Count == 0)
        {
            return;
        }

        ChunkPos centerChunk = GetStreamingCenterChunk();
        chunkColliderUpdater.Update(centerChunk, worldSettings.ColliderDistanceInChunks, renderers);
    }

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
        int surfaceHeight = worldGenerator is NoiseWorldGenerator noiseWorldGenerator
            ? noiseWorldGenerator.GetSurfaceHeight(worldX, worldZ)
            : 0;

        // 플레이어/스트리밍 타깃의 위치는 발 위치 기준으로 보고 있으므로,
        // 표면 voxel의 윗면(surfaceHeight + 1)보다 살짝 위에 시작시킵니다.
        target.position = new Vector3(
            targetPosition.x,
            surfaceHeight + 1f + spawnHeightPadding,
            targetPosition.z);
    }

    private void CreateChunkMeshController(ChunkPos chunkPos, ChunkNeighborhood neighborhood)
    {
        ChunkMeshController renderer = AcquireChunkMeshController();
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

    private ChunkMeshController AcquireChunkMeshController()
    {
        EnsureChunkControllerPool();
        return chunkControllerPool.Get();
    }

    private void ReleaseChunkMeshController(ChunkMeshController renderer)
    {
        renderer.ReleaseForPooling();
        chunkControllerPool.Release(renderer);
    }

    private void EnsureChunkControllerPool()
    {
        if (chunkControllerPool != null)
        {
            return;
        }

        chunkControllerPool = new ObjectPool<ChunkMeshController>(
            CreatePooledChunkMeshController,
            OnTakeChunkMeshControllerFromPool,
            OnReturnChunkMeshControllerToPool,
            OnDestroyPooledChunkMeshController,
            collectionCheck: false,
            defaultCapacity: 32,
            maxSize: 8192);
    }

    private ChunkMeshController CreatePooledChunkMeshController()
    {
        var chunkObject = new GameObject("Pooled Chunk");
        chunkObject.transform.SetParent(transform, worldPositionStays: false);
        ChunkMeshController renderer = chunkObject.AddComponent<ChunkMeshController>();
        chunkObject.AddComponent<ChunkEditInteractor>();
        chunkObject.SetActive(false);
        return renderer;
    }

    private static void OnTakeChunkMeshControllerFromPool(ChunkMeshController renderer)
    {
        if (renderer != null)
        {
            renderer.gameObject.SetActive(true);
        }
    }

    private static void OnReturnChunkMeshControllerToPool(ChunkMeshController renderer)
    {
        if (renderer != null)
        {
            renderer.gameObject.SetActive(false);
        }
    }

    private static void OnDestroyPooledChunkMeshController(ChunkMeshController renderer)
    {
        if (renderer != null)
        {
            Destroy(renderer.gameObject);
        }
    }

    private void EnsureEditController()
    {
        if (editController == null && !TryGetComponent(out editController))
        {
            editController = gameObject.AddComponent<VoxelEditController>();
        }
    }

    private void ConfigureEditController()
    {
        EnsureEditController();
        editController.Initialize(editCamera, worldSettings, this, this);
    }

    public void ProcessPendingRebuildsImmediately()
    {
        ProcessPendingRebuilds();
    }

    private ChunkPos GetStreamingCenterChunk()
    {
        Vector3 targetPosition = GetStreamingTargetPosition();
        WorldPos worldPos = WorldPos.FromFloatsFloor(targetPosition.x, targetPosition.y, targetPosition.z);
        return worldPos.ToChunkPos(ChunkData.DefaultSize);
    }

    private Vector3 GetStreamingTargetPosition()
    {
        Transform target = streamingTarget != null ? streamingTarget : transform;
        return target.position;
    }

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

            if (renderers.TryGetValue(chunkPos, out ChunkMeshController renderer))
            {
                renderers.Remove(chunkPos);
                ReleaseChunkMeshController(renderer);
            }

            meshBuildQueue.Remove(chunkPos);
        }
    }

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

            CreateChunkMeshController(chunkPos, CreateNeighborhood(chunkPos));
            QueueChunkInitialBuild(chunkPos);

            // 새 청크가 들어오면 그 청크와 맞닿은 기존 청크들의 경계 면 판정이 달라질 수 있습니다.
            // 새 청크 자신은 별도 초기 메싱 큐에서 처리하고, 여기서는 이웃만 다시 보게 합니다.
            AddAdjacentChunkPositions(chunkPos, chunksNeedingRebuild);
        }
    }

    private void QueueChunksNeedingMesh(HashSet<ChunkPos> chunksNeedingRebuild)
    {
        foreach (ChunkPos chunkPos in chunksNeedingRebuild)
        {
            QueueChunkRebuild(chunkPos);
        }
    }

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

    private void ClearRuntimeChunks()
    {
        meshBuildQueue.Clear();
        chunkControllerPool?.Clear();
        chunkStreamer.Reset();
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private void OnDestroy()
    {
        DisposeMeshBuilder();
    }

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

    private ChunkData GetChunk(ChunkPos chunkPos)
    {
        return chunks.TryGetValue(chunkPos, out ChunkData chunkData) ? chunkData : null;
    }

    private void AddAdjacentChunkPositions(ChunkPos chunkPos, HashSet<ChunkPos> chunksNeedingRebuild)
    {
        foreach (ChunkPos offset in ChunkPos.OrthogonalOffsets)
        {
            chunksNeedingRebuild.Add(new ChunkPos(
                chunkPos.X + offset.X,
                chunkPos.Y + offset.Y,
                chunkPos.Z + offset.Z));
        }
    }

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

    private void QueueChunkRebuild(ChunkPos chunkPos)
    {
        if (renderers.ContainsKey(chunkPos))
        {
            meshBuildQueue.QueueRebuild(chunkPos);
        }
    }

    private void QueueChunkInitialBuild(ChunkPos chunkPos)
    {
        if (!renderers.ContainsKey(chunkPos))
        {
            return;
        }

        meshBuildQueue.QueueInitialBuild(chunkPos);
    }

    private void RebuildChunkImmediately(ChunkPos chunkPos)
    {
        if (!renderers.TryGetValue(chunkPos, out ChunkMeshController renderer))
        {
            return;
        }

        renderer.UpdateNeighborhood(CreateNeighborhood(chunkPos));
        renderer.RebuildMesh();
    }

    private bool ProcessQueuedMeshBuild(ChunkPos chunkPos)
    {
        if (!renderers.TryGetValue(chunkPos, out ChunkMeshController renderer))
        {
            return false;
        }

        renderer.UpdateNeighborhood(CreateNeighborhood(chunkPos));
        renderer.RebuildMesh();
        return true;
    }

}
