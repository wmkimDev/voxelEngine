using System;

public readonly struct NoiseWorldGeneratorSettings
{
    public readonly int Seed;
    public readonly float NoiseScale;
    public readonly int BaseHeight;
    public readonly int HeightAmplitude;
    public readonly int TopSoilDepth;
    public readonly int TopSoilDepthVariation;

    public NoiseWorldGeneratorSettings(
        int seed,
        float noiseScale,
        int baseHeight,
        int heightAmplitude,
        int topSoilDepth,
        int topSoilDepthVariation)
    {
        Seed = seed;
        NoiseScale = Math.Max(0.001f, noiseScale);
        BaseHeight = Math.Max(0, baseHeight);
        HeightAmplitude = Math.Max(1, heightAmplitude);
        TopSoilDepth = Math.Max(1, topSoilDepth);
        TopSoilDepthVariation = Math.Max(0, topSoilDepthVariation);
    }
}
