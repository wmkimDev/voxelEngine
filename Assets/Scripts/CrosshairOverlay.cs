using UnityEngine;

public sealed class CrosshairOverlay : MonoBehaviour
{
    [SerializeField] private float size = 14f;
    [SerializeField] private float thickness = 2f;
    [SerializeField] private Color color = Color.white;

    private Texture2D texture;

    private void Awake()
    {
        texture = Texture2D.whiteTexture;
    }

    private void OnGUI()
    {
        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;
        float halfSize = size * 0.5f;
        float halfThickness = thickness * 0.5f;

        Color previousColor = GUI.color;
        GUI.color = color;

        GUI.DrawTexture(
            new Rect(centerX - halfSize, centerY - halfThickness, size, thickness),
            texture);

        GUI.DrawTexture(
            new Rect(centerX - halfThickness, centerY - halfSize, thickness, size),
            texture);

        GUI.color = previousColor;
    }
}
