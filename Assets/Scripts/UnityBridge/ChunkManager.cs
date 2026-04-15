using System.Collections.Generic;
using UnityEngine;

public sealed class ChunkManager : MonoBehaviour
{
    [SerializeField] private Transform streamingTarget;
    [SerializeField] private int viewDistanceInChunks = 1;
    [SerializeField] private int minLayerY = 0;
    [SerializeField] private int maxLayerY = 0;
    [SerializeField] private int maxChunkLoadsPerFrame = 4;
    [SerializeField] private Material material;
    [SerializeField] private Texture2D voxelAtlas;
    [SerializeField] private Camera editCamera;
    [SerializeField] private float editDistance = 30f;
    [SerializeField] private byte placeVoxelType = VoxelType.Grass;
    [SerializeField] private int seed = 12345;
    [SerializeField] private float noiseScale = 18f;
    [SerializeField] private int baseHeight = 2;
    [SerializeField] private int heightAmplitude = 5;

    private readonly Dictionary<ChunkPos, ChunkData> chunks = new();
    private readonly Dictionary<ChunkPos, ChunkRenderer> renderers = new();
    private readonly IMeshBuilder meshBuilder = new NaiveMeshBuilder();
    private readonly ChunkLoadScheduler loadScheduler = new();
    private IWorldGenerator worldGenerator;
    private IChunkStreamingPolicy streamingPolicy;
    private ChunkPos? currentCenterChunk;

    private void Start()
    {
        worldGenerator = new NoiseWorldGenerator(seed, noiseScale, baseHeight, heightAmplitude);
        RebuildStreamingPolicy();
        UpdateStreaming(force: true);
    }

    private void Update()
    {
        UpdateStreaming(force: false);
    }

    private void OnValidate()
    {
        viewDistanceInChunks = Mathf.Max(0, viewDistanceInChunks);
        maxLayerY = Mathf.Max(minLayerY, maxLayerY);
        maxChunkLoadsPerFrame = Mathf.Max(1, maxChunkLoadsPerFrame);
        editDistance = Mathf.Max(0.1f, editDistance);
        placeVoxelType = (byte)Mathf.Clamp(placeVoxelType, VoxelType.Dirt, VoxelType.Sand);
        noiseScale = Mathf.Max(0.001f, noiseScale);
        baseHeight = Mathf.Max(0, baseHeight);
        heightAmplitude = Mathf.Max(1, heightAmplitude);
    }

    [ContextMenu("Rebuild Streaming World")]
    private void BuildStreamingWorld()
    {
        ClearRuntimeChunks();
        chunks.Clear();
        renderers.Clear();
        worldGenerator = new NoiseWorldGenerator(seed, noiseScale, baseHeight, heightAmplitude);
        RebuildStreamingPolicy();
        currentCenterChunk = null;
        UpdateStreaming(force: true);
    }

    private void RebuildStreamingPolicy()
    {
        streamingPolicy = new SquareStreamingPolicy(viewDistanceInChunks, minLayerY, maxLayerY);
    }

    private void UpdateStreaming(bool force)
    {
        if (worldGenerator == null)
        {
            worldGenerator = new NoiseWorldGenerator(seed, noiseScale, baseHeight, heightAmplitude);
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
        // 마지막으로 새 이웃 관계를 기준으로 경계 면을 다시 계산합니다.
        UnloadMissingChunks(requiredChunks);
        LoadRequiredChunks(requiredChunks, targetChunk);
        RebuildLoadedNeighborhoods();
    }

    private void CreateChunkRenderer(ChunkPos chunkPos, ChunkNeighborhood neighborhood)
    {
        var chunkObject = new GameObject($"Chunk ({chunkPos.X}, {chunkPos.Y}, {chunkPos.Z})");
        chunkObject.transform.SetParent(transform, worldPositionStays: false);
        WorldPos chunkOrigin = chunkPos.ToWorldOrigin(neighborhood.Size);
        chunkObject.transform.localPosition = new Vector3(chunkOrigin.X, chunkOrigin.Y, chunkOrigin.Z);

        ChunkRenderer renderer = chunkObject.AddComponent<ChunkRenderer>();
        renderer.Initialize(
            neighborhood,
            meshBuilder,
            editCamera,
            material,
            voxelAtlas,
            placeVoxelType,
            editDistance,
            editedLocalPos => RebuildNeighborsTouchedByEdit(chunkPos, editedLocalPos));

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

            if (renderers.TryGetValue(chunkPos, out ChunkRenderer renderer))
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

    private void LoadRequiredChunks(IReadOnlyCollection<ChunkPos> requiredChunks, ChunkPos centerChunk)
    {
        var chunksToLoad = new List<ChunkPos>();
        foreach (ChunkPos chunkPos in requiredChunks)
        {
            if (!chunks.ContainsKey(chunkPos))
            {
                chunksToLoad.Add(chunkPos);
            }
        }

        List<ChunkPos> sortedChunks = loadScheduler.SortByDistance(chunksToLoad, centerChunk);
        int loadCount = Mathf.Min(maxChunkLoadsPerFrame, sortedChunks.Count);
        for (int i = 0; i < loadCount; i++)
        {
            // 이번 단계에서는 생성과 메시 빌드를 모두 메인 스레드에서 처리합니다.
            // 일부러 프레임당 개수를 제한해 끊김을 관찰하고, 다음 단계의 비동기화 필요성을 확인합니다.
            ChunkPos chunkPos = sortedChunks[i];
            ChunkData chunkData = CreateChunkData(chunkPos);
            chunks.Add(chunkPos, chunkData);
            CreateChunkRenderer(chunkPos, CreateNeighborhood(chunkPos));
        }
    }

    private void RebuildLoadedNeighborhoods()
    {
        foreach (KeyValuePair<ChunkPos, ChunkRenderer> pair in renderers)
        {
            // 새 청크가 로드되거나 기존 청크가 언로드되면 경계 면 판정이 달라집니다.
            // 그래서 로드된 렌더러는 최신 이웃 정보를 받은 뒤 다시 메시를 만들어야 합니다.
            pair.Value.UpdateNeighborhood(CreateNeighborhood(pair.Key));
            pair.Value.RebuildMesh();
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

    private void RebuildNeighborsTouchedByEdit(ChunkPos chunkPos, LocalPos editedLocalPos)
    {
        ChunkData chunkData = GetChunk(chunkPos);
        if (chunkData == null)
        {
            return;
        }

        int edge = chunkData.Size - 1;

        // 경계 voxel이 바뀌면 이웃 청크의 경계 면 노출 여부도 달라집니다.
        // 그래서 현재 청크는 ChunkRenderer가 직접 재빌드하고, 여기서는 맞닿은 이웃만 추가로 재빌드합니다.
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
        if (renderers.TryGetValue(chunkPos, out ChunkRenderer renderer))
        {
            renderer.RebuildMesh();
        }
    }

}
