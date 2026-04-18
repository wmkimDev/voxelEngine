using System;

public readonly struct NoiseWorldGeneratorSettings
{
    // 같은 시드면 항상 같은 지형이 나오도록 노이즈 해시의 기준값으로 사용합니다.
    public readonly int Seed;

    // 노이즈 샘플링 간격입니다. 값이 클수록 지형 변화가 완만해집니다.
    public readonly float NoiseScale;

    // 모든 높이 계산의 기준이 되는 월드 기본 고도입니다.
    public readonly int BaseHeight;

    // 기본 고도에서 얼마나 크게 높이 차를 만들지 정합니다.
    public readonly int HeightAmplitude;

    // 표면 바로 아래에 기본으로 깔리는 흙 두께입니다.
    public readonly int TopSoilDepth;

    // 컬럼마다 표토 두께에 더해지는 추가 변동 폭입니다.
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
