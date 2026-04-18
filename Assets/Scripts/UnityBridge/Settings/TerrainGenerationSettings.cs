using UnityEngine;

[CreateAssetMenu(menuName = "Voxel Engine/Terrain Generation Settings", fileName = "TerrainGenerationSettings")]
public sealed class TerrainGenerationSettings : ScriptableObject
{
    [Tooltip("월드 노이즈를 고정하는 시드 값입니다. 같은 시드면 같은 지형이 생성됩니다.")]
    [SerializeField] private int seed = 12345;

    [Tooltip("지형 노이즈를 얼마나 넓게 샘플링할지 정합니다. 값이 클수록 지형 변화가 더 완만해집니다.")]
    [SerializeField] private float noiseScale = 26f;

    [Tooltip("전체 지형이 시작하는 기본 높이입니다.")]
    [SerializeField] private int baseHeight = 5;

    [Tooltip("기본 높이에서 위아래로 얼마나 크게 출렁일지 정합니다.")]
    [SerializeField] private int heightAmplitude = 7;

    [Tooltip("표면 바로 아래에 깔리는 기본 흙 두께입니다.")]
    [SerializeField] private int topSoilDepth = 4;

    [Tooltip("흙 두께에 추가되는 컬럼별 변동 폭입니다.")]
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
