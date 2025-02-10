using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class Hexagonify : MonoBehaviour
{
    [SerializeField] private Mesh mesh;

    [BurstCompile]
    private struct FloodNeighbouringTriangles : IJob
    {
        private NativeArray<float3> vertices;
        public void Execute()
        {
            throw new System.NotImplementedException();
        }
    }
}
