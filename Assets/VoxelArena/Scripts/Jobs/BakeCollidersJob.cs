using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// Готовит данные MeshCollider в рабочих потоках. После этого назначение
/// sharedMesh на главном потоке не пересчитывает коллайдер заново.
/// </summary>
public struct BakeCollidersJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<int> meshIds;

    public void Execute(int index)
    {
        Physics.BakeMesh(meshIds[index], false);
    }
}
