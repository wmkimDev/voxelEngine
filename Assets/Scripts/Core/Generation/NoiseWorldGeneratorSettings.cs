using System;

public readonly struct NoiseWorldGeneratorSettings
{
    public readonly int Seed;
    public readonly float NoiseScale;
    public readonly int BaseHeight;
    public readonly int HeightAmplitude;
    public readonly int BeachHeight;
    public readonly int TopSoilDepth;
    public readonly int TopSoilDepthVariation;
    public readonly float CaveNoiseScale;
    public readonly float CaveThreshold;
    public readonly int CaveSurfaceClearance;

    public NoiseWorldGeneratorSettings(
        int seed,
        float noiseScale,
        int baseHeight,
        int heightAmplitude,
        int beachHeight,
        int topSoilDepth,
        int topSoilDepthVariation,
        float caveNoiseScale,
        float caveThreshold,
        int caveSurfaceClearance)
    {
        Seed = seed;
        NoiseScale = Math.Max(0.001f, noiseScale);
        BaseHeight = Math.Max(0, baseHeight);
        HeightAmplitude = Math.Max(1, heightAmplitude);
        BeachHeight = Math.Max(0, beachHeight);
        TopSoilDepth = Math.Max(1, topSoilDepth);
        TopSoilDepthVariation = Math.Max(0, topSoilDepthVariation);
        CaveNoiseScale = Math.Max(0.001f, caveNoiseScale);
        CaveThreshold = Math.Max(0f, Math.Min(1f, caveThreshold));
        CaveSurfaceClearance = Math.Max(1, caveSurfaceClearance);
    }
}
