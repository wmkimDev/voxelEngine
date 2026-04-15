using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class VoxelPlayerController : MonoBehaviour
{
    private enum CameraMode
    {
        FirstPerson,
        ThirdPerson
    }

    [Tooltip("플레이어가 조종할 카메라입니다. 비워두면 MainCamera를 자동으로 찾습니다.")]
    [SerializeField] private Transform cameraTransform;

    [Tooltip("Shift를 누르지 않았을 때의 기본 이동 속도입니다.")]
    [SerializeField] private float walkSpeed = 5f;

    [Tooltip("Shift를 누르고 이동할 때의 달리기 속도입니다.")]
    [SerializeField] private float sprintSpeed = 9f;

    [Tooltip("Space를 눌렀을 때 목표로 하는 점프 높이입니다.")]
    [SerializeField] private float jumpHeight = 1.2f;

    [Tooltip("아래 방향 가속도입니다. 절댓값이 클수록 더 빨리 떨어집니다.")]
    [SerializeField] private float gravity = -24f;

    [Tooltip("마우스 움직임이 시점 회전에 반영되는 민감도입니다.")]
    [SerializeField] private float lookSensitivity = 0.12f;

    [Tooltip("위/아래로 볼 수 있는 최대 각도입니다. 90도에 너무 가까우면 조작이 불안정해집니다.")]
    [SerializeField] private float maxLookPitch = 85f;

    [Tooltip("시작할 때 아래로 Raycast해서 지형 위로 플레이어를 내려놓을지 결정합니다.")]
    [SerializeField] private bool snapToGroundOnStart = true;

    [Tooltip("시작 위치에서 바닥을 찾기 위해 아래로 쏘는 Raycast 거리입니다.")]
    [SerializeField] private float groundSnapDistance = 100f;

    [Tooltip("시작 카메라 모드입니다. 실행 중 V 키로 1인칭/3인칭을 전환합니다.")]
    [SerializeField] private CameraMode cameraMode = CameraMode.FirstPerson;

    [Tooltip("1인칭일 때 카메라가 플레이어 발 기준으로 올라가는 높이입니다.")]
    [SerializeField] private float firstPersonCameraHeight = 1.6f;

    [Tooltip("3인칭일 때 카메라가 플레이어 뒤로 떨어지는 거리입니다.")]
    [SerializeField] private float thirdPersonDistance = 5f;

    [Tooltip("3인칭 카메라가 바라보는 기준점 높이입니다.")]
    [SerializeField] private float thirdPersonHeight = 1.5f;

    [Tooltip("3인칭 카메라를 플레이어 중앙에서 살짝 옆으로 빼는 거리입니다.")]
    [SerializeField] private float thirdPersonShoulderOffset = 0.35f;

    [Tooltip("3인칭 카메라 충돌 검사에 쓰는 SphereCast 반지름입니다.")]
    [SerializeField] private float thirdPersonCameraRadius = 0.2f;

    private CharacterController characterController;
    private float yaw;
    private float pitch;
    private float verticalVelocity;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        yaw = transform.eulerAngles.y;
        if (cameraTransform != null)
        {
            pitch = NormalizePitch(cameraTransform.localEulerAngles.x);
        }
    }

    private void Start()
    {
        LockCursor();

        if (snapToGroundOnStart)
        {
            SnapToGround();
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null)
        {
            return;
        }

        LockCursor();
        UpdateCameraMode(keyboard);
        UpdateLook(mouse);
        UpdateMove(keyboard);
    }

    private void LateUpdate()
    {
        UpdateCameraPlacement();
    }

    private void OnValidate()
    {
        walkSpeed = Mathf.Max(0f, walkSpeed);
        sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
        jumpHeight = Mathf.Max(0f, jumpHeight);
        lookSensitivity = Mathf.Max(0f, lookSensitivity);
        maxLookPitch = Mathf.Clamp(maxLookPitch, 1f, 89f);
        groundSnapDistance = Mathf.Max(0f, groundSnapDistance);
        firstPersonCameraHeight = Mathf.Max(0f, firstPersonCameraHeight);
        thirdPersonDistance = Mathf.Max(0.1f, thirdPersonDistance);
        thirdPersonCameraRadius = Mathf.Max(0.01f, thirdPersonCameraRadius);
    }

    private void UpdateCameraMode(Keyboard keyboard)
    {
        if (!keyboard.vKey.wasPressedThisFrame)
        {
            return;
        }

        cameraMode = cameraMode == CameraMode.FirstPerson
            ? CameraMode.ThirdPerson
            : CameraMode.FirstPerson;
    }

    private void UpdateLook(Mouse mouse)
    {
        if (mouse == null || cameraTransform == null)
        {
            return;
        }

        Vector2 mouseDelta = mouse.delta.ReadValue();
        yaw += mouseDelta.x * lookSensitivity;
        pitch -= mouseDelta.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -maxLookPitch, maxLookPitch);

        // 몸은 좌우 회전만 담당하고, 카메라는 위아래 회전만 담당합니다.
        // 이렇게 분리해야 CharacterController 캡슐이 마우스 pitch에 따라 기울지 않습니다.
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void UpdateCameraPlacement()
    {
        if (cameraTransform == null)
        {
            return;
        }

        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        if (cameraMode == CameraMode.FirstPerson)
        {
            cameraTransform.localPosition = Vector3.up * firstPersonCameraHeight;
            return;
        }

        // 3인칭 카메라는 플레이어 머리 근처의 pivot에서 뒤로 물러납니다.
        // pitch를 offset에도 적용해 위/아래를 볼 때 카메라가 플레이어 주변을 자연스럽게 돕니다.
        Vector3 pivot = transform.position + (Vector3.up * thirdPersonHeight);
        Vector3 localOffset = new Vector3(thirdPersonShoulderOffset, 0f, -thirdPersonDistance);
        Vector3 rotatedOffset = transform.rotation * (Quaternion.Euler(pitch, 0f, 0f) * localOffset);
        Vector3 desiredPosition = pivot + rotatedOffset;
        Vector3 safePosition = ResolveCameraCollision(pivot, desiredPosition);

        cameraTransform.position = safePosition;
        cameraTransform.rotation = Quaternion.LookRotation(pivot - safePosition, Vector3.up);
    }

    private void UpdateMove(Keyboard keyboard)
    {
        Vector3 input = Vector3.zero;
        if (keyboard.wKey.isPressed)
        {
            input += Vector3.forward;
        }

        if (keyboard.sKey.isPressed)
        {
            input += Vector3.back;
        }

        if (keyboard.dKey.isPressed)
        {
            input += Vector3.right;
        }

        if (keyboard.aKey.isPressed)
        {
            input += Vector3.left;
        }

        input = Vector3.ClampMagnitude(input, 1f);
        float speed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
            ? sprintSpeed
            : walkSpeed;

        // 이동 방향은 카메라 pitch를 무시하고 플레이어 몸의 yaw만 따릅니다.
        // 아래를 보고 있어도 앞으로 누르면 땅속으로 이동하지 않게 하기 위함입니다.
        Vector3 horizontalVelocity = transform.TransformDirection(input) * speed;

        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (characterController.isGrounded && keyboard.spaceKey.wasPressedThisFrame)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontalVelocity + (Vector3.up * verticalVelocity);
        characterController.Move(velocity * Time.deltaTime);
    }

    private Vector3 ResolveCameraCollision(Vector3 pivot, Vector3 desiredPosition)
    {
        Vector3 toCamera = desiredPosition - pivot;
        float distance = toCamera.magnitude;
        if (distance <= 0.001f)
        {
            return desiredPosition;
        }

        bool wasEnabled = characterController.enabled;
        characterController.enabled = false;

        bool hitObstacle = Physics.SphereCast(
            pivot,
            thirdPersonCameraRadius,
            toCamera / distance,
            out RaycastHit hit,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);

        characterController.enabled = wasEnabled;

        if (!hitObstacle)
        {
            return desiredPosition;
        }

        return pivot + ((toCamera / distance) * Mathf.Max(0f, hit.distance - thirdPersonCameraRadius));
    }

    private void SnapToGround()
    {
        Vector3 rayOrigin = transform.position + Vector3.up;
        bool wasEnabled = characterController.enabled;
        characterController.enabled = false;

        bool hitGround = Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out RaycastHit hit,
            groundSnapDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        characterController.enabled = wasEnabled;

        if (!hitGround)
        {
            return;
        }

        // 이 오브젝트의 위치는 플레이어 발 위치로 봅니다.
        // CharacterController의 center/height는 발 위로 캡슐을 세우는 데 사용합니다.
        transform.position = hit.point;
        verticalVelocity = -2f;
    }

    private static void LockCursor()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static float NormalizePitch(float value)
    {
        return value > 180f ? value - 360f : value;
    }
}
