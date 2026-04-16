using UnityEngine;

[CreateAssetMenu(menuName = "Voxel Engine/Voxel World Settings", fileName = "VoxelWorldSettings")]
public sealed class VoxelWorldSettings : ScriptableObject
{
    public enum MeshBuilderMode
    {
        Naive,
        Greedy,
        JobNaive
    }

    public enum StreamingMode
    {
        Square,
        Radial
    }

    [Header("Streaming")]
    [SerializeField] private StreamingMode streamingMode = StreamingMode.Radial;
    [SerializeField] private int viewDistanceInChunks = 2;
    [SerializeField] private int minLayerY = 0;
    [SerializeField] private int maxLayerY = 0;
    [SerializeField] private int maxChunkLoadsPerFrame = 4;

    [Header("Meshing")]
    [SerializeField] private MeshBuilderMode meshBuilderMode = MeshBuilderMode.Greedy;
    [SerializeField] private Material material;
    [SerializeField] private Texture2D voxelAtlas;

    [Header("Editing")]
    [SerializeField] private float editDistance = 30f;
    [SerializeField] private byte placeVoxelType = VoxelType.Grass;

    [Header("World Generation")]
    [SerializeField] private int seed = 12345;
    [SerializeField] private float noiseScale = 10f;
    [SerializeField] private int baseHeight = 1;
    [SerializeField] private int heightAmplitude = 20;

    public StreamingMode ActiveStreamingMode => streamingMode;
    public int ViewDistanceInChunks => viewDistanceInChunks;
    public int MinLayerY => minLayerY;
    public int MaxLayerY => maxLayerY;
    public int MaxChunkLoadsPerFrame => maxChunkLoadsPerFrame;
    public MeshBuilderMode ActiveMeshBuilderMode => meshBuilderMode;
    public Material Material => material;
    public Texture2D VoxelAtlas => voxelAtlas;
    public float EditDistance => editDistance;
    public byte PlaceVoxelType => placeVoxelType;
    public int Seed => seed;
    public float NoiseScale => noiseScale;
    public int BaseHeight => baseHeight;
    public int HeightAmplitude => heightAmplitude;

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
}
