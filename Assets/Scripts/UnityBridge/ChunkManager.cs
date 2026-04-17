using System.Collections.Generic;
using UnityEngine;

public sealed class ChunkManager : MonoBehaviour
{
    private static readonly ChunkPos[] NeighborChunkOffsets =
    {
        new ChunkPos(1, 0, 0),
        new ChunkPos(-1, 0, 0),
        new ChunkPos(0, 1, 0),
        new ChunkPos(0, -1, 0),
        new ChunkPos(0, 0, 1),
        new ChunkPos(0, 0, -1),
    };

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
    private IChunkStreamingPolicy loadStreamingPolicy;
    private IChunkStreamingPolicy unloadStreamingPolicy;
    private readonly ChunkLoadScheduler loadScheduler = new();
    private readonly ChunkStreamingPriorityEvaluator streamingPriorityEvaluator = new();
    private readonly ChunkColliderPolicy chunkColliderPolicy = new();
    private VoxelEditController editController;

    // 스트리밍 중심 청크와, 그 기준으로 계산해 둔 required/unload 집합 캐시입니다.
    private ChunkPos? currentCenterChunk;
    private ChunkPos? cachedStreamingCenterChunk;
    private IReadOnlyCollection<ChunkPos> cachedRequiredChunks = System.Array.Empty<ChunkPos>();
    private IReadOnlyCollection<ChunkPos> cachedUnloadProtectedChunks = System.Array.Empty<ChunkPos>();
    private readonly HashSet<ChunkPos> activeUnloadProtectedChunks = new();
    private readonly List<ChunkPos> removedUnloadProtectedChunks = new();

    // 이웃 경계가 바뀌어 다시 메싱해야 하는 청크를 모아두는 작업 큐입니다.
    // HashSet으로 중복 요청을 합치고, List 스냅샷으로 이번 프레임 처리분만 순회합니다.
    private readonly HashSet<ChunkPos> pendingRebuildChunks = new();
    private readonly List<ChunkPos> rebuildQueueSnapshot = new();

    // Update() 핫패스에서 매 프레임 재사용하는 작업 버퍼들입니다.
    private readonly HashSet<ChunkPos> chunksNeedingRebuildBuffer = new();
    private readonly List<ChunkPos> chunksToLoadBuffer = new();
    private readonly HashSet<ChunkPos> visibleChunkPositionsBuffer = new();
    private readonly Dictionary<ChunkPos, float> screenPriorityScoresBuffer = new();
    private readonly HashSet<ChunkPos> currentUnloadProtectedChunkSetBuffer = new();

    public int LoadedChunkCount => chunks.Count;
    public Vector3 StreamingTargetPosition => GetStreamingTargetPosition();
    public ChunkPos StreamingTargetChunk => GetStreamingCenterChunk();
    public string ActiveMeshBuilderName => worldSettings != null
        ? worldSettings.ActiveMeshBuilderMode.ToString()
        : "Missing Settings";

    private void Awake()
    {
        EnsureEditController();
    }

    private void Start()
    {
        if (!EnsureWorldSettingsAssigned())
        {
            return;
        }

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
    }

    private void OnDrawGizmos()
    {
        if (worldSettings == null || !Application.isPlaying)
        {
            return;
        }

        DrawColliderDebugGizmos();
    }

    [ContextMenu("Rebuild Streaming World")]
    private void BuildStreamingWorld()
    {
        if (!EnsureWorldSettingsAssigned())
        {
            return;
        }

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
        currentCenterChunk = null;
        UpdateStreaming(force: true);
        ProcessPendingRebuilds();
        UpdateColliderUsage();
    }

    private void RebuildStreamingPolicy()
    {
        loadStreamingPolicy = CreateStreamingPolicy(worldSettings.ViewDistanceInChunks);
        unloadStreamingPolicy = CreateStreamingPolicy(worldSettings.UnloadDistanceInChunks);
        activeUnloadProtectedChunks.Clear();
        removedUnloadProtectedChunks.Clear();
        cachedStreamingCenterChunk = null;
        cachedRequiredChunks = System.Array.Empty<ChunkPos>();
        cachedUnloadProtectedChunks = System.Array.Empty<ChunkPos>();
    }

    private IChunkStreamingPolicy CreateStreamingPolicy(int horizontalRadius)
    {
        return worldSettings.ActiveStreamingMode == VoxelWorldSettings.StreamingMode.Radial
            ? new RadialStreamingPolicy(horizontalRadius, worldSettings.MinLayerY, worldSettings.MaxLayerY)
            : new SquareStreamingPolicy(horizontalRadius, worldSettings.MinLayerY, worldSettings.MaxLayerY);
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

    private void UpdateStreaming(bool force)
    {
        VoxelPerformanceStats.RecordChunkLoadCount(0);

        if (worldGenerator == null)
        {
            worldGenerator = CreateWorldGenerator();
        }

        if (loadStreamingPolicy == null || unloadStreamingPolicy == null)
        {
            RebuildStreamingPolicy();
        }

        // 런타임 중 설정값을 바꿔도 캐시 용량이 따라가도록 매 스트리밍 틱에서 동기화합니다.
        chunkDataCache.SetCapacity(worldSettings.CachedChunkCount);

        ChunkPos targetChunk = GetStreamingCenterChunk();
        bool centerChanged = !currentCenterChunk.HasValue || !targetChunk.Equals(currentCenterChunk.Value);
        bool canReuseCachedSets = !force
            && !centerChanged
            && cachedStreamingCenterChunk.HasValue
            && cachedStreamingCenterChunk.Value.Equals(targetChunk);

        IReadOnlyCollection<ChunkPos> requiredChunks;
        IReadOnlyCollection<ChunkPos> unloadProtectedChunks;

        if (canReuseCachedSets)
        {
            requiredChunks = cachedRequiredChunks;
            unloadProtectedChunks = cachedUnloadProtectedChunks;
        }
        else
        {
            // 플레이어가 같은 중심 청크 안에 있는 동안 required/unload 집합은 바뀌지 않습니다.
            // 그래서 중심 청크가 바뀌었을 때만 새로 만들고, 나머지 프레임에는 이전 결과를 재사용합니다.
            requiredChunks = loadStreamingPolicy.GetRequiredChunks(targetChunk);
            unloadProtectedChunks = unloadStreamingPolicy.GetRequiredChunks(targetChunk);
            cachedStreamingCenterChunk = targetChunk;
            cachedRequiredChunks = requiredChunks;
            cachedUnloadProtectedChunks = unloadProtectedChunks;
        }

        // 필요한 청크가 9개여도 한 프레임에는 maxChunkLoadsPerFrame개만 만듭니다.
        // 그래서 플레이어가 같은 청크에 머물러 있어도, 아직 못 만든 청크가 있으면
        // 다음 프레임에도 스트리밍 갱신을 계속해야 합니다.
        // currentCenterChunk가 null이면 아직 비교할 이전 중심 청크가 없다는 뜻입니다.
        // 하지만 실제로 계속 로드할지 여부는 아래 hasMissingChunks가 판단합니다.
        bool hasMissingChunks = HasMissingChunks(requiredChunks);

        if (!force && !centerChanged && !hasMissingChunks)
        {
            return;
        }

        currentCenterChunk = targetChunk;

        // requiredChunks는 "지금 새로 로드해야 하는 청크 목록"이고,
        // unloadProtectedChunks는 "아직 유지해도 되는 청크 목록"입니다.
        // 이렇게 로드 반경과 언로드 반경을 분리해 경계에서 생겼다 사라지는 떨림을 줄입니다.
        chunksNeedingRebuildBuffer.Clear();
        HashSet<ChunkPos> chunksNeedingRebuild = chunksNeedingRebuildBuffer;
        if (centerChanged || force)
        {
            CollectRemovedUnloadProtectedChunks(unloadProtectedChunks, removedUnloadProtectedChunks);
            CollectChunksNeedingRebuildForRemovedChunks(removedUnloadProtectedChunks, chunksNeedingRebuild);
            UnloadChunks(removedUnloadProtectedChunks);
            ReplaceActiveUnloadProtectedChunks(unloadProtectedChunks);
        }

        LoadRequiredChunks(requiredChunks, targetChunk, chunksNeedingRebuild);
        QueueChunksNeedingMesh(chunksNeedingRebuild);
    }

    private void UpdateColliderUsage()
    {
        if (worldSettings == null || renderers.Count == 0)
        {
            return;
        }

        ChunkPos centerChunk = GetStreamingCenterChunk();
        chunkColliderPolicy.ApplyUsage(centerChunk, worldSettings.ColliderDistanceInChunks, renderers);
    }

    private void DrawColliderDebugGizmos()
    {
        int chunkSize = ChunkData.DefaultSize;
        foreach ((ChunkPos chunkPos, ChunkMeshController controller) in renderers)
        {
            if (!controller.HasActiveColliderMesh)
            {
                continue;
            }

            WorldPos origin = chunkPos.ToWorldOrigin(chunkSize);
            Vector3 chunkCenter = new Vector3(
                origin.X + (chunkSize * 0.5f),
                origin.Y + (chunkSize * 0.5f),
                origin.Z + (chunkSize * 0.5f));

            Vector3 size = Vector3.one * (chunkSize * 0.9f);
            Gizmos.color = new Color(0.1f, 1f, 0.25f, 0.55f);
            Gizmos.DrawWireCube(chunkCenter, size);
        }
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
        int surfaceHeight = worldGenerator.GetSurfaceHeight(worldX, worldZ);

        // 플레이어/스트리밍 타깃의 위치는 발 위치 기준으로 보고 있으므로,
        // 표면 voxel의 윗면(surfaceHeight + 1)보다 살짝 위에 시작시킵니다.
        target.position = new Vector3(
            targetPosition.x,
            surfaceHeight + 1f + spawnHeightPadding,
            targetPosition.z);
    }

    private void CreateChunkMeshController(ChunkPos chunkPos, ChunkNeighborhood neighborhood)
    {
        var chunkObject = new GameObject($"Chunk ({chunkPos.X}, {chunkPos.Y}, {chunkPos.Z})");
        chunkObject.transform.SetParent(transform, worldPositionStays: false);
        WorldPos chunkOrigin = chunkPos.ToWorldOrigin(neighborhood.Size);
        chunkObject.transform.localPosition = new Vector3(chunkOrigin.X, chunkOrigin.Y, chunkOrigin.Z);

        ChunkMeshController renderer = chunkObject.AddComponent<ChunkMeshController>();
        renderer.Initialize(
            neighborhood,
            meshBuilder,
            worldSettings.Material,
            worldSettings.VoxelAtlas);

        ChunkEditInteractor editInteractor = chunkObject.AddComponent<ChunkEditInteractor>();
        editInteractor.Initialize(
            neighborhood.Center,
            editedLocalPos => QueueAffectedChunksForEdit(chunkPos, editedLocalPos));

        renderers.Add(chunkPos, renderer);
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
        editController.Initialize(editCamera, worldSettings, this);
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

            if (renderers.TryGetValue(chunkPos, out ChunkMeshController renderer))
            {
                renderers.Remove(chunkPos);
                Destroy(renderer.gameObject);
            }
        }
    }

    private bool HasMissingChunks(IReadOnlyCollection<ChunkPos> requiredChunks)
    {
        foreach (ChunkPos chunkPos in requiredChunks)
        {
            if (!chunks.ContainsKey(chunkPos))
            {
                return true;
            }
        }

        return false;
    }

    private void LoadRequiredChunks(
        IReadOnlyCollection<ChunkPos> requiredChunks,
        ChunkPos centerChunk,
        HashSet<ChunkPos> chunksNeedingRebuild)
    {
        chunksToLoadBuffer.Clear();
        foreach (ChunkPos chunkPos in requiredChunks)
        {
            if (!chunks.ContainsKey(chunkPos))
            {
                chunksToLoadBuffer.Add(chunkPos);
            }
        }

        Camera cameraToUse = editCamera != null ? editCamera : Camera.main;
        streamingPriorityEvaluator.CollectVisibleChunkPositions(
            cameraToUse,
            chunksToLoadBuffer,
            visibleChunkPositionsBuffer);
        streamingPriorityEvaluator.CollectScreenPriorityScores(
            cameraToUse,
            chunksToLoadBuffer,
            screenPriorityScoresBuffer);
        loadScheduler.SortByVisibilityAndDistanceInPlace(
            chunksToLoadBuffer,
            centerChunk,
            visibleChunkPositionsBuffer,
            screenPriorityScoresBuffer);
        int maxChunkLoadsPerFrame = worldSettings.MaxChunkLoadsPerFrame;
        int loadCount = Mathf.Min(maxChunkLoadsPerFrame, chunksToLoadBuffer.Count);
        VoxelPerformanceStats.RecordChunkLoadCount(loadCount);

        for (int i = 0; i < loadCount; i++)
        {
            // 이번 단계에서는 생성과 메시 빌드를 모두 메인 스레드에서 처리합니다.
            // 일부러 프레임당 개수를 제한해 끊김을 관찰하고, 다음 단계의 비동기화 필요성을 확인합니다.
            ChunkPos chunkPos = chunksToLoadBuffer[i];
            ChunkData chunkData = CreateChunkData(chunkPos);
            chunks.Add(chunkPos, chunkData);
            CreateChunkMeshController(chunkPos, CreateNeighborhood(chunkPos));

            // 새 청크가 들어오면 그 청크와 맞닿은 기존 청크들의 경계 면 판정이 달라질 수 있습니다.
            // 새 청크 자신은 Initialize 안에서 이미 한 번 메시를 만들었으므로, 여기서는 이웃만 다시 보게 합니다.
            AddAdjacentChunkPositions(chunkPos, chunksNeedingRebuild);
        }
    }

    private void CollectChunksNeedingRebuildForRemovedChunks(
        IReadOnlyCollection<ChunkPos> removedChunks,
        HashSet<ChunkPos> chunksNeedingRebuild)
    {
        foreach (ChunkPos chunkPos in removedChunks)
        {
            // 사라질 청크 자신은 곧 삭제되므로 재빌드할 필요가 없습니다.
            // 대신 그 청크에 맞닿아 있던 이웃은 "바깥이 공기인지"를 다시 계산해야 합니다.
            AddAdjacentChunkPositions(chunkPos, chunksNeedingRebuild);
        }
    }

    private void CollectRemovedUnloadProtectedChunks(
        IReadOnlyCollection<ChunkPos> currentUnloadProtectedChunks,
        List<ChunkPos> removedChunks)
    {
        removedChunks.Clear();
        currentUnloadProtectedChunkSetBuffer.Clear();
        foreach (ChunkPos chunkPos in currentUnloadProtectedChunks)
        {
            currentUnloadProtectedChunkSetBuffer.Add(chunkPos);
        }

        foreach (ChunkPos chunkPos in activeUnloadProtectedChunks)
        {
            if (!currentUnloadProtectedChunkSetBuffer.Contains(chunkPos))
            {
                removedChunks.Add(chunkPos);
            }
        }
    }

    private void ReplaceActiveUnloadProtectedChunks(IReadOnlyCollection<ChunkPos> unloadProtectedChunks)
    {
        activeUnloadProtectedChunks.Clear();
        foreach (ChunkPos chunkPos in unloadProtectedChunks)
        {
            activeUnloadProtectedChunks.Add(chunkPos);
        }
    }

    private void QueueChunksNeedingMesh(HashSet<ChunkPos> chunksNeedingRebuild)
    {
        foreach (ChunkPos chunkPos in chunksNeedingRebuild)
        {
            pendingRebuildChunks.Add(chunkPos);
        }
    }

    private void ProcessPendingRebuilds()
    {
        VoxelPerformanceStats.RecordChunkRebuildCount(0);
        if (pendingRebuildChunks.Count == 0)
        {
            return;
        }

        rebuildQueueSnapshot.Clear();
        foreach (ChunkPos chunkPos in pendingRebuildChunks)
        {
            rebuildQueueSnapshot.Add(chunkPos);
        }

        pendingRebuildChunks.Clear();

        int maxChunkRebuildsPerFrame = Mathf.Max(0, worldSettings.MaxChunkRebuildsPerFrame);
        int rebuildCount = Mathf.Min(maxChunkRebuildsPerFrame, rebuildQueueSnapshot.Count);

        int rebuildsPerformed = 0;
        for (int i = 0; i < rebuildCount; i++)
        {
            ChunkPos chunkPos = rebuildQueueSnapshot[i];
            if (!renderers.TryGetValue(chunkPos, out ChunkMeshController renderer))
            {
                continue;
            }

            // 재빌드 큐에 들어있는 동안 이웃 구성이 바뀌었을 수 있으므로,
            // 실제 RebuildMesh 직전에 최신 neighborhood를 다시 연결합니다.
            renderer.UpdateNeighborhood(CreateNeighborhood(chunkPos));
            renderer.RebuildMesh();
            rebuildsPerformed++;
        }

        VoxelPerformanceStats.RecordChunkRebuildCount(rebuildsPerformed);

        for (int i = rebuildCount; i < rebuildQueueSnapshot.Count; i++)
        {
            pendingRebuildChunks.Add(rebuildQueueSnapshot[i]);
        }

        rebuildQueueSnapshot.Clear();
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
        foreach (ChunkPos offset in NeighborChunkOffsets)
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

        // 편집된 현재 청크도 즉시 재빌드하지 않고 큐에 넣어 한 프레임에 한 번만 처리합니다.
        QueueChunkRebuild(chunkPos);

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
            pendingRebuildChunks.Add(chunkPos);
        }
    }

}
