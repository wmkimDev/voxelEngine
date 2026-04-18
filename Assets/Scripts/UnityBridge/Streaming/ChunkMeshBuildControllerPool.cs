using System;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// ChunkMeshBuildController GameObject를 재사용하고, 새 청크에 맞게 초기화하는 풀입니다.
/// </summary>
public sealed class ChunkMeshBuildControllerPool
{
    private readonly Transform parentTransform;
    private readonly ObjectPool<ChunkMeshBuildController> pool;

    public ChunkMeshBuildControllerPool(Transform parentTransform)
    {
        this.parentTransform = parentTransform;
        pool = new ObjectPool<ChunkMeshBuildController>(
            CreatePooledController,
            OnTakeControllerFromPool,
            OnReturnControllerToPool,
            OnDestroyPooledController,
            collectionCheck: false,
            defaultCapacity: 32,
            maxSize: 8192);
    }

    /// <summary>
    /// 풀에서 컨트롤러를 가져와 청크 좌표와 neighborhood에 맞게 초기화합니다.
    /// </summary>
    public ChunkMeshBuildController Acquire(
        ChunkPos chunkPos,
        ChunkNeighborhood neighborhood,
        IMeshBuilder meshBuilder,
        Material material,
        Texture2D voxelAtlas,
        Action<LocalPos> onEditedLocalPos)
    {
        ChunkMeshBuildController controller = pool.Get();
        GameObject chunkObject = controller.gameObject;
        chunkObject.name = $"Chunk ({chunkPos.X}, {chunkPos.Y}, {chunkPos.Z})";
        chunkObject.transform.SetParent(parentTransform, worldPositionStays: false);

        WorldPos chunkOrigin = chunkPos.ToWorldOrigin(neighborhood.Size);
        chunkObject.transform.localPosition = new Vector3(chunkOrigin.X, chunkOrigin.Y, chunkOrigin.Z);

        controller.Initialize(
            neighborhood,
            meshBuilder,
            material,
            voxelAtlas,
            rebuildImmediately: false);

        ChunkEditInteractor editInteractor = chunkObject.GetComponent<ChunkEditInteractor>();
        editInteractor.Initialize(neighborhood.Center, onEditedLocalPos);
        return controller;
    }

    /// <summary>
    /// 사용이 끝난 컨트롤러를 초기화 후 풀로 돌려보냅니다.
    /// </summary>
    public void Release(ChunkMeshBuildController controller)
    {
        controller.ResetForPooling();
        pool.Release(controller);
    }

    private ChunkMeshBuildController CreatePooledController()
    {
        var chunkObject = new GameObject("Pooled Chunk");
        chunkObject.transform.SetParent(parentTransform, worldPositionStays: false);
        ChunkMeshBuildController controller = chunkObject.AddComponent<ChunkMeshBuildController>();
        chunkObject.AddComponent<ChunkEditInteractor>();
        chunkObject.SetActive(false);
        return controller;
    }

    private static void OnTakeControllerFromPool(ChunkMeshBuildController controller)
    {
        if (controller != null)
        {
            controller.gameObject.SetActive(true);
        }
    }

    private static void OnReturnControllerToPool(ChunkMeshBuildController controller)
    {
        if (controller != null)
        {
            controller.gameObject.SetActive(false);
        }
    }

    private static void OnDestroyPooledController(ChunkMeshBuildController controller)
    {
        if (controller != null)
        {
            UnityEngine.Object.Destroy(controller.gameObject);
        }
    }
}
