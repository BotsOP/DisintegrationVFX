using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public class HelperJobs : MonoBehaviour
{
    [BurstCompile]
    public struct Vector3ToFloat3 : IJobFor
    {
        [ReadOnly, DeallocateOnJobCompletion]
        private NativeArray<Vector3> vertices0;
        [WriteOnly]
        private NativeArray<float3> vertices1;

        public Vector3ToFloat3(NativeArray<Vector3> vertices0, NativeArray<float3> vertices1)
        {
            this.vertices0 = vertices0;
            this.vertices1 = vertices1;
        }
        
        public void Execute(int index)
        {
            vertices1[index] = vertices0[index];
        }
    }
    
    [BurstCompile]
    public struct FindMatchingVertices : IJobFor
    {
        private const float FLOAT_PRECISION = 0.001f;
        
        [NativeDisableContainerSafetyRestriction, WriteOnly]
        public NativeParallelMultiHashMap<float3, ushort> unifiedIndices;
        
        [ReadOnly] private NativeArray<float3> vertices;
        [ReadOnly] private NativeArray<ushort> indices;

        public FindMatchingVertices(NativeArray<float3> vertices, NativeArray<ushort> indices, Allocator allocator)
        {
            this.vertices = vertices;
            this.indices = indices;
            unifiedIndices = new NativeParallelMultiHashMap<float3, ushort>(vertices.Length, allocator);
        }

        public void Execute(int index)
        {
            float3 vertex = vertices[indices[index]];
            unifiedIndices.Add(vertex, (ushort)index);

            for (int i = 0; i < indices.Length; i++)
            {
                if(i == index)
                    continue;

                ushort index1 = indices[i];
                float3 vertexToMatch = vertices[index1];
                if (math.distancesq(vertex, vertexToMatch) < FLOAT_PRECISION)
                {
                    unifiedIndices.Add(vertex, index1);
                }
            }
        }
    }
}
