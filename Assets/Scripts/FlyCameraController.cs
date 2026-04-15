using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public sealed class FlyCameraController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float fastMoveMultiplier = 3f;
    [SerializeField] private float lookSensitivity = 0.12f;
    [SerializeField] private bool requireRightMouseButton = true;

    private float yaw;
    private float pitch;

    private void Awake()
    {
        Vector3 eulerAngles = transform.eulerAngles;
        yaw = eulerAngles.y;
        pitch = NormalizePitch(eulerAngles.x);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard == null)
        {
            return;
        }

        UpdateCursorLock(mouse);
        UpdateLook(mouse);
        UpdateMove(keyboard);
    }

    private void UpdateCursorLock(Mouse mouse)
    {
        if (!requireRightMouseButton || mouse == null)
        {
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (mouse.rightButton.wasReleasedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void UpdateLook(Mouse mouse)
    {
        if (mouse == null)
        {
            return;
        }

        if (requireRightMouseButton && !mouse.rightButton.isPressed)
        {
            return;
        }

        Vector2 mouseDelta = mouse.delta.ReadValue();
        yaw += mouseDelta.x * lookSensitivity;
        pitch -= mouseDelta.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void UpdateMove(Keyboard keyboard)
    {
        Vector3 direction = Vector3.zero;

        // WASD는 현재 카메라가 바라보는 방향 기준으로 앞/뒤/좌/우 이동합니다.
        if (keyboard.wKey.isPressed)
        {
            direction += transform.forward;
        }

        if (keyboard.sKey.isPressed)
        {
            direction -= transform.forward;
        }

        if (keyboard.dKey.isPressed)
        {
            direction += transform.right;
        }

        if (keyboard.aKey.isPressed)
        {
            direction -= transform.right;
        }

        // Q/E는 월드 기준 아래/위 이동입니다.
        if (keyboard.eKey.isPressed)
        {
            direction += Vector3.up;
        }

        if (keyboard.qKey.isPressed)
        {
            direction -= Vector3.up;
        }

        if (direction.sqrMagnitude <= 0f)
        {
            return;
        }

        float speed = moveSpeed;
        if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
        {
            speed *= fastMoveMultiplier;
        }

        transform.position += direction.normalized * (speed * Time.deltaTime);
    }

    private static float NormalizePitch(float value)
    {
        return value > 180f ? value - 360f : value;
    }
}
