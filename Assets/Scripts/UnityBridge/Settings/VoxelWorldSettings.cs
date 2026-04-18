using UnityEngine;

[CreateAssetMenu(menuName = "Voxel Engine/Voxel World Settings", fileName = "VoxelWorldSettings")]
public sealed class VoxelWorldSettings : ScriptableObject
{
    public enum MeshBuilderMode
    {
        Naive,
        Greedy,
        JobNaive,
        JobGreedy
    }

    public enum StreamingMode
    {
        Square,
        Radial
    }

    [Header("Streaming")]
    [SerializeField] private StreamingMode streamingMode = StreamingMode.Radial;
    [SerializeField] private int viewDistanceInChunks = 2;
    [SerializeField] private int unloadDistanceInChunks = 3;
    [SerializeField] private int colliderDistanceInChunks = 2;
    // 언로드한 ChunkData를 메모리에 몇 개까지 남겨둘지 정합니다.
    // 다시 보는 청크를 생성기부터 다시 만들지 않게 해 회전/왕복 시 팝인을 줄이는 용도입니다.
    [SerializeField] private int cachedChunkCount = 256;
    [SerializeField] private int minLayerY = 0;
    [SerializeField] private int maxLayerY = 0;
    [SerializeField] private int maxChunkLoadsPerFrame = 4;
    [SerializeField] private int loadPriorityShortlistMultiplier = 4;

    [Header("Meshing")]
    [SerializeField] private int maxChunkRebuildsPerFrame = 6;
    [SerializeField] private MeshBuilderMode meshBuilderMode = MeshBuilderMode.Greedy;
    [SerializeField] private Material material;
    [SerializeField] private Texture2D voxelAtlas;

    [Header("Editing")]
    [SerializeField] private float editDistance = 30f;
    [SerializeField] private byte placeVoxelType = VoxelType.Grass;

    [Header("World Generation")]
    [SerializeField] private TerrainGenerationSettings terrainGenerationSettings;

    [Header("Atmosphere")]
    [SerializeField] private bool enableFog = false;
    [SerializeField] private Color fogColor = new(0.78f, 0.84f, 0.9f, 1f);
    [SerializeField] private float fogStartDistance = 110f;
    [SerializeField] private float fogEndDistance = 260f;

    public StreamingMode ActiveStreamingMode => streamingMode;
    public int ViewDistanceInChunks => viewDistanceInChunks;
    public int UnloadDistanceInChunks => unloadDistanceInChunks;
    public int ColliderDistanceInChunks => colliderDistanceInChunks;
    public int CachedChunkCount => cachedChunkCount;
    public int MinLayerY => minLayerY;
    public int MaxLayerY => maxLayerY;
    public int MaxChunkLoadsPerFrame => maxChunkLoadsPerFrame;
    public int LoadPriorityShortlistMultiplier => loadPriorityShortlistMultiplier;
    public int MaxChunkRebuildsPerFrame => maxChunkRebuildsPerFrame;
    public MeshBuilderMode ActiveMeshBuilderMode => meshBuilderMode;
    public Material Material => material;
    public Texture2D VoxelAtlas => voxelAtlas;
    public float EditDistance => editDistance;
    public byte PlaceVoxelType => placeVoxelType;
    public TerrainGenerationSettings TerrainGenerationSettings => terrainGenerationSettings;
    public bool EnableFog => enableFog;
    public Color FogColor => fogColor;
    public float FogStartDistance => fogStartDistance;
    public float FogEndDistance => fogEndDistance;

    public NoiseWorldGeneratorSettings ToNoiseWorldGeneratorSettings()
    {
        return terrainGenerationSettings != null
            ? terrainGenerationSettings.ToNoiseWorldGeneratorSettings()
            : new NoiseWorldGeneratorSettings(12345, 34f, 18, 32, 4, 2);
    }

    private void OnValidate()
    {
        viewDistanceInChunks = Mathf.Max(0, viewDistanceInChunks);
        unloadDistanceInChunks = Mathf.Max(viewDistanceInChunks, unloadDistanceInChunks);
        colliderDistanceInChunks = Mathf.Clamp(colliderDistanceInChunks, 0, unloadDistanceInChunks);
        cachedChunkCount = Mathf.Max(0, cachedChunkCount);
        maxLayerY = Mathf.Max(minLayerY, maxLayerY);
        maxChunkLoadsPerFrame = Mathf.Max(1, maxChunkLoadsPerFrame);
        loadPriorityShortlistMultiplier = Mathf.Max(1, loadPriorityShortlistMultiplier);
        editDistance = Mathf.Max(0.1f, editDistance);
        placeVoxelType = (byte)Mathf.Clamp(placeVoxelType, VoxelType.Dirt, VoxelType.Sand);
        fogStartDistance = Mathf.Max(0f, fogStartDistance);
        fogEndDistance = Mathf.Max(fogStartDistance + 0.1f, fogEndDistance);
    }
}
