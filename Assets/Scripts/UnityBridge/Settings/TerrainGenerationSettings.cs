using UnityEngine;

[CreateAssetMenu(menuName = "Voxel Engine/Terrain Generation Settings", fileName = "TerrainGenerationSettings")]
public sealed class TerrainGenerationSettings : ScriptableObject
{
    [SerializeField] private int seed = 12345;
    [SerializeField] private float noiseScale = 26f;
    [SerializeField] private int baseHeight = 5;
    [SerializeField] private int heightAmplitude = 7;
    [SerializeField] private int topSoilDepth = 4;
    [SerializeField] private int topSoilDepthVariation = 2;

    public NoiseWorldGeneratorSettings ToNoiseWorldGeneratorSettings()
    {
        return new NoiseWorldGeneratorSettings(
            seed,
            noiseScale,
            baseHeight,
            heightAmplitude,
            topSoilDepth,
            topSoilDepthVariation);
    }

    private void OnValidate()
    {
        noiseScale = Mathf.Max(0.001f, noiseScale);
        baseHeight = Mathf.Max(0, baseHeight);
        heightAmplitude = Mathf.Max(1, heightAmplitude);
        topSoilDepth = Mathf.Max(1, topSoilDepth);
        topSoilDepthVariation = Mathf.Max(0, topSoilDepthVariation);
    }
}
