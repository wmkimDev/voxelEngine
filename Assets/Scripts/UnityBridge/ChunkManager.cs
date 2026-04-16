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

    private readonly Dictionary<ChunkPos, ChunkData> chunks = new();
    private readonly Dictionary<ChunkPos, ChunkMeshController> renderers = new();
    private IMeshBuilder meshBuilder;
    private readonly ChunkLoadScheduler loadScheduler = new();
    private IWorldGenerator worldGenerator;
    private IChunkStreamingPolicy streamingPolicy;
    private ChunkPos? currentCenterChunk;
    private int lastChunkLoadsPerformed;

    public int LoadedChunkCount => chunks.Count;
    public int LastChunkLoadsPerformed => lastChunkLoadsPerformed;
    public string ActiveMeshBuilderName => worldSettings != null
        ? worldSettings.ActiveMeshBuilderMode.ToString()
        : "Missing Settings";

    private void Start()
    {
        if (!EnsureWorldSettingsAssigned())
        {
            return;
        }

        VoxelPerformanceStats.Reset();
        meshBuilder = CreateMeshBuilder();
        worldGenerator = CreateWorldGenerator();
        AlignStreamingTargetToSurface();
        RebuildStreamingPolicy();
        UpdateStreaming(force: true);
    }

    private void Update()
    {
        UpdateStreaming(force: false);
    }

    private void OnValidate()
    {
        spawnHeightPadding = Mathf.Max(0f, spawnHeightPadding);
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
        DisposeMeshBuilder();
        meshBuilder = CreateMeshBuilder();
        worldGenerator = CreateWorldGenerator();
        AlignStreamingTargetToSurface();
        RebuildStreamingPolicy();
        currentCenterChunk = null;
        UpdateStreaming(force: true);
    }

    private void RebuildStreamingPolicy()
    {
        streamingPolicy = worldSettings.ActiveStreamingMode == VoxelWorldSettings.StreamingMode.Radial
            ? new RadialStreamingPolicy(worldSettings.ViewDistanceInChunks, worldSettings.MinLayerY, worldSettings.MaxLayerY)
            : new SquareStreamingPolicy(worldSettings.ViewDistanceInChunks, worldSettings.MinLayerY, worldSettings.MaxLayerY);
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
        lastChunkLoadsPerformed = 0;

        if (worldGenerator == null)
        {
            worldGenerator = CreateWorldGenerator();
        }

        if (streamingPolicy == null)
        {
            RebuildStreamingPolicy();
        }

        ChunkPos targetChunk = GetStreamingCenterChunk();
        IReadOnlyCollection<ChunkPos> requiredChunks = streamingPolicy.GetRequiredChunks(targetChunk);

        // 필요한 청크가 9개여도 한 프레임에는 maxChunkLoadsPerFrame개만 만듭니다.
        // 그래서 플레이어가 같은 청크에 머물러 있어도, 아직 못 만든 청크가 있으면
        // 다음 프레임에도 스트리밍 갱신을 계속해야 합니다.
        // currentCenterChunk가 null이면 아직 비교할 이전 중심 청크가 없다는 뜻입니다.
        // 하지만 실제로 계속 로드할지 여부는 아래 hasMissingChunks가 판단합니다.
        bool centerChanged = !currentCenterChunk.HasValue || !targetChunk.Equals(currentCenterChunk.Value);
        bool hasMissingChunks = HasMissingChunks(requiredChunks);

        if (!force && !centerChanged && !hasMissingChunks)
        {
            return;
        }

        currentCenterChunk = targetChunk;

        // requiredChunks는 "지금 월드에 있어야 하는 청크 목록"입니다.
        // 목록에서 빠진 기존 청크는 삭제하고, 목록에 있는데 아직 없는 청크는 새로 만듭니다.
        // 마지막에는 "이번 변화로 메시를 다시 계산해야 하는 청크"만 골라 재빌드합니다.
        var chunksNeedingRebuild = new HashSet<ChunkPos>();
        CollectChunksNeedingRebuildForUnload(requiredChunks, chunksNeedingRebuild);
        UnloadMissingChunks(requiredChunks);
        LoadRequiredChunks(requiredChunks, targetChunk, chunksNeedingRebuild);
        RebuildChunksNeedingMesh(chunksNeedingRebuild);
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
            editCamera,
            worldSettings.EditDistance,
            worldSettings.PlaceVoxelType,
            editedLocalPos => RebuildAffectedNeighborChunksForEdit(chunkPos, editedLocalPos),
            renderer.RebuildMesh);

        renderers.Add(chunkPos, renderer);
    }

    private ChunkPos GetStreamingCenterChunk()
    {
        Transform target = streamingTarget != null ? streamingTarget : transform;

        // 월드 좌표를 바로 청크 좌표로 바꿉니다.
        // 음수 좌표에서도 올바르게 동작해야 하므로 WorldPos 내부의 floor div/floor mod 규칙을 사용합니다.
        Vector3 targetPosition = target.position;
        WorldPos worldPos = WorldPos.FromFloatsFloor(targetPosition.x, targetPosition.y, targetPosition.z);
        return worldPos.ToChunkPos(ChunkData.DefaultSize);
    }

    private void UnloadMissingChunks(IReadOnlyCollection<ChunkPos> requiredChunks)
    {
        var requiredSet = new HashSet<ChunkPos>(requiredChunks);
        var chunksToUnload = new List<ChunkPos>();

        foreach (ChunkPos chunkPos in chunks.Keys)
        {
            if (!requiredSet.Contains(chunkPos))
            {
                // Dictionary를 foreach 중에 직접 수정하면 예외가 납니다.
                // 먼저 제거할 키만 따로 모은 뒤, 아래 루프에서 실제 삭제합니다.
                chunksToUnload.Add(chunkPos);
            }
        }

        foreach (ChunkPos chunkPos in chunksToUnload)
        {
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
        var chunksToLoad = new List<ChunkPos>();
        foreach (ChunkPos chunkPos in requiredChunks)
        {
            if (!chunks.ContainsKey(chunkPos))
            {
                chunksToLoad.Add(chunkPos);
            }
        }

        HashSet<ChunkPos> visibleChunkPositions = GetVisibleChunkPositions(chunksToLoad);
        List<ChunkPos> sortedChunks = loadScheduler.SortByVisibilityAndDistance(
            chunksToLoad,
            centerChunk,
            visibleChunkPositions);
        int maxChunkLoadsPerFrame = worldSettings.MaxChunkLoadsPerFrame;
        int loadCount = Mathf.Min(maxChunkLoadsPerFrame, sortedChunks.Count);
        lastChunkLoadsPerformed = loadCount;

        for (int i = 0; i < loadCount; i++)
        {
            // 이번 단계에서는 생성과 메시 빌드를 모두 메인 스레드에서 처리합니다.
            // 일부러 프레임당 개수를 제한해 끊김을 관찰하고, 다음 단계의 비동기화 필요성을 확인합니다.
            ChunkPos chunkPos = sortedChunks[i];
            ChunkData chunkData = CreateChunkData(chunkPos);
            chunks.Add(chunkPos, chunkData);
            CreateChunkMeshController(chunkPos, CreateNeighborhood(chunkPos));

            // 새 청크가 들어오면 그 청크와 맞닿은 기존 청크들의 경계 면 판정이 달라질 수 있습니다.
            // 새 청크 자신은 Initialize 안에서 이미 한 번 메시를 만들었으므로, 여기서는 이웃만 다시 보게 합니다.
            AddAdjacentChunkPositions(chunkPos, chunksNeedingRebuild);
        }
    }

    private HashSet<ChunkPos> GetVisibleChunkPositions(IReadOnlyCollection<ChunkPos> chunkPositions)
    {
        // "지금 로드 후보인 청크들 중 카메라 시야 안에 들어오는 청크"만 골라냅니다.
        // 스트리밍 정책이 "무엇이 필요한가"를 정한다면, 이 메서드는 그중 "무엇을 먼저 만들까"에 쓰일 가시 청크 집합을 만듭니다.
        Camera cameraToUse = editCamera != null ? editCamera : Camera.main;
        if (cameraToUse == null || chunkPositions.Count == 0)
        {
            return null;
        }

        // 카메라 시야를 이루는 6개 평면(좌/우/상/하/근/원)을 구합니다.
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cameraToUse);
        var visibleChunks = new HashSet<ChunkPos>();
        int chunkSize = ChunkData.DefaultSize;

        foreach (ChunkPos chunkPos in chunkPositions)
        {
            // 청크 하나를 "정육면체 경계 상자 하나"로 보고 프러스텀과 겹치는지 검사합니다.
            // 청크 원점은 좌하단 모서리이므로, 중심점은 청크 크기의 절반만큼 더한 위치입니다.
            WorldPos origin = chunkPos.ToWorldOrigin(chunkSize);
            Vector3 chunkCenter = new Vector3(
                origin.X + (chunkSize * 0.5f),
                origin.Y + (chunkSize * 0.5f),
                origin.Z + (chunkSize * 0.5f));
            Bounds chunkBounds = new Bounds(chunkCenter, Vector3.one * chunkSize);

            // 경계 상자가 카메라 프러스텀과 겹치면 "지금 화면에 보이거나 곧 보일 가능성이 높은 청크"로 간주합니다.
            if (GeometryUtility.TestPlanesAABB(frustumPlanes, chunkBounds))
            {
                visibleChunks.Add(chunkPos);
            }
        }

        return visibleChunks;
    }

    private void CollectChunksNeedingRebuildForUnload(
        IReadOnlyCollection<ChunkPos> requiredChunks,
        HashSet<ChunkPos> chunksNeedingRebuild)
    {
        var requiredSet = new HashSet<ChunkPos>(requiredChunks);

        foreach (ChunkPos chunkPos in chunks.Keys)
        {
            if (!requiredSet.Contains(chunkPos))
            {
                // 사라질 청크 자신은 곧 삭제되므로 재빌드할 필요가 없습니다.
                // 대신 그 청크에 맞닿아 있던 이웃은 "바깥이 공기인지"를 다시 계산해야 합니다.
                AddAdjacentChunkPositions(chunkPos, chunksNeedingRebuild);
            }
        }
    }

    private void RebuildChunksNeedingMesh(HashSet<ChunkPos> chunksNeedingRebuild)
    {
        foreach (ChunkPos chunkPos in chunksNeedingRebuild)
        {
            if (renderers.TryGetValue(chunkPos, out ChunkMeshController renderer))
            {
                // 로드/언로드 후에는 이웃 참조 자체가 달라질 수 있으므로 최신 neighborhood를 다시 넣습니다.
                renderer.UpdateNeighborhood(CreateNeighborhood(chunkPos));
                renderer.RebuildMesh();
            }
        }
    }

    private ChunkData CreateChunkData(ChunkPos chunkPos)
    {
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

    private void RebuildAffectedNeighborChunksForEdit(ChunkPos chunkPos, LocalPos editedLocalPos)
    {
        ChunkData chunkData = GetChunk(chunkPos);
        if (chunkData == null)
        {
            return;
        }

        int edge = chunkData.Size - 1;

        // 경계 voxel이 바뀌면 이웃 청크의 경계 면 노출 여부도 달라집니다.
        // 그래서 현재 청크는 ChunkMeshController가 직접 재빌드하고, 여기서는 맞닿은 이웃만 추가로 재빌드합니다.
        if (editedLocalPos.X == 0)
        {
            RebuildChunk(new ChunkPos(chunkPos.X - 1, chunkPos.Y, chunkPos.Z));
        }

        if (editedLocalPos.X == edge)
        {
            RebuildChunk(new ChunkPos(chunkPos.X + 1, chunkPos.Y, chunkPos.Z));
        }

        if (editedLocalPos.Y == 0)
        {
            RebuildChunk(new ChunkPos(chunkPos.X, chunkPos.Y - 1, chunkPos.Z));
        }

        if (editedLocalPos.Y == edge)
        {
            RebuildChunk(new ChunkPos(chunkPos.X, chunkPos.Y + 1, chunkPos.Z));
        }

        if (editedLocalPos.Z == 0)
        {
            RebuildChunk(new ChunkPos(chunkPos.X, chunkPos.Y, chunkPos.Z - 1));
        }

        if (editedLocalPos.Z == edge)
        {
            RebuildChunk(new ChunkPos(chunkPos.X, chunkPos.Y, chunkPos.Z + 1));
        }
    }

    private void RebuildChunk(ChunkPos chunkPos)
    {
        if (renderers.TryGetValue(chunkPos, out ChunkMeshController renderer))
        {
            renderer.RebuildMesh();
        }
    }

}
