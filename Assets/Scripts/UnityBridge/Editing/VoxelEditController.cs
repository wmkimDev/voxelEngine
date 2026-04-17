using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class VoxelEditController : MonoBehaviour
{
    private Camera editCamera;
    private VoxelWorldSettings worldSettings;
    private Object logContext;
    private ChunkManager chunkManager;

    public void Initialize(Camera camera, VoxelWorldSettings settings, ChunkManager manager, Object context)
    {
        editCamera = camera;
        worldSettings = settings;
        chunkManager = manager;
        logContext = context != null ? context : this;
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || worldSettings == null)
        {
            return;
        }

        bool? place = null;
        if (mouse.leftButton.wasPressedThisFrame)
        {
            place = false;
        }
        else if (mouse.rightButton.wasPressedThisFrame)
        {
            place = true;
        }

        if (!place.HasValue)
        {
            return;
        }

        Camera cameraToUse = editCamera != null ? editCamera : Camera.main;
        if (cameraToUse == null)
        {
            Debug.LogError($"{nameof(editCamera)} is required because no MainCamera was found.", logContext);
            return;
        }

        Ray ray = cameraToUse.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, worldSettings.EditDistance))
        {
            return;
        }

        if (!hit.collider.TryGetComponent(out ChunkEditInteractor editInteractor))
        {
            return;
        }

        if (editInteractor.TryEditFromHit(hit, place.Value, worldSettings.PlaceVoxelType) && chunkManager != null)
        {
            chunkManager.ProcessPendingRebuildsImmediately();
        }
    }
}
