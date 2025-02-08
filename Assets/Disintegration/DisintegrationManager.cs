using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using VInspector;

[BurstCompile]
public class DisintegrationManager : MonoBehaviour
{
    private const float FLOAT_TOLERANCE = 0.0001f;
    [SerializeField] private Mesh mesh;
    [SerializeField] private Material material;
    [SerializeField] private int3[] neighbouringTrisArray;
    [SerializeField] private ushort[] neighbouringTrisShortArray;

    [Header("Debug")] 
    [SerializeField] private Transform meshTransform;
    [SerializeField] private Camera mainCamera;
    
    private NativeArray<int3> neighbouringTris;
    private NativeArray<ushort> neighbouringTrisShort;
    private NativeArray<int> results;
    private GraphicsBuffer resultsBuffer;
    
    private void Awake()
    {
        neighbouringTris = new NativeArray<int3>(neighbouringTrisArray, Allocator.Persistent);
        neighbouringTrisShort = new NativeArray<ushort>(neighbouringTrisShortArray, Allocator.Persistent);
        resultsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, mesh.vertexCount, sizeof(int));
    }

    [Button]
    public void BakeNeighbouringTriangles()
    {
        int amountTriangles = mesh.triangles.Length / 3;
        
        List<Vector3> verticesList = new List<Vector3>();
        mesh.GetVertices(verticesList);
        NativeArray<float3> vertices = new NativeArray<float3>(verticesList.Count, Allocator.Persistent);
            
        Vector3ToFloat3 vector3ToFloat3 = new Vector3ToFloat3(verticesList.ToNativeArray(Allocator.TempJob), vertices);
        JobHandle jobHandle = new JobHandle();
        jobHandle = vector3ToFloat3.ScheduleParallel(verticesList.Count, 64, jobHandle);
        jobHandle.Complete();

        if (mesh.indexFormat == IndexFormat.UInt16)
        {
            List<ushort> indices = new List<ushort>();
            mesh.GetIndices(indices, 0);
                
            NativeArray<ushort> neighbouringTriangles = new NativeArray<ushort>(indices.Count, Allocator.Persistent);
            FindNeighbouringTriangles findNeighbouringTriangles = new FindNeighbouringTriangles(
                neighbouringTriangles,
                indices.ToNativeArray(Allocator.TempJob),
                vertices,
                amountTriangles
            );
            jobHandle = findNeighbouringTriangles.ScheduleParallel(amountTriangles, 8, jobHandle);
            jobHandle.Complete();
            findNeighbouringTriangles.neighbouringTriangles.Dispose();

            neighbouringTrisShort = findNeighbouringTriangles.neighbouringTrianglesShort;
            neighbouringTrisShortArray = neighbouringTrisShort.ToArray();
        }
        if (mesh.indexFormat == IndexFormat.UInt32)
        {
            List<int> indices = new List<int>();
            mesh.GetIndices(indices, 0);

            NativeArray<int3> neighbouringTriangles = new NativeArray<int3>(amountTriangles, Allocator.Persistent);
            FindNeighbouringTriangles findNeighbouringTriangles = new FindNeighbouringTriangles(
                neighbouringTriangles,
                indices.ToNativeArray(Allocator.TempJob),
                vertices,
                amountTriangles
            );
            jobHandle = findNeighbouringTriangles.ScheduleParallel(amountTriangles, 16, jobHandle);
            jobHandle.Complete();
            findNeighbouringTriangles.neighbouringTrianglesShort.Dispose(); 

            neighbouringTris = findNeighbouringTriangles.neighbouringTriangles;
            neighbouringTrisArray = neighbouringTris.ToArray();
        }
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !Input.GetMouseButton(0) || !Input.GetMouseButtonDown(0))
        {
            return;
        }
        
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        MeshRaycaster.RaycastMesh(mesh, ray, meshTransform, out Vector3 v0, out Vector3 v1, out Vector3 v2);
        
        Gizmos.DrawSphere(v0, 0.1f);
        Gizmos.DrawSphere(v1, 0.1f);
        Gizmos.DrawSphere(v2, 0.1f);
    }

    private void Update()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (Input.GetMouseButton(0) && Physics.Raycast(ray, out RaycastHit hit))
        {
            NativeArray<int> result = new NativeArray<int>(mesh.vertexCount, Allocator.TempJob);
            List<ushort> indices = new List<ushort>();
            mesh.GetIndices(indices, 0);
            
            FloodNeighbouringTriangles floodNeighbouringTriangles = new FloodNeighbouringTriangles(result, indices.ToNativeArray(Allocator.TempJob), neighbouringTrisShort, (ushort)hit.triangleIndex);
            floodNeighbouringTriangles.Schedule().Complete();
            floodNeighbouringTriangles.Dispose();
            resultsBuffer.SetData(result);
            
            material.SetBuffer("_Result", resultsBuffer);
            
            result.Dispose();
        }
    }

    private void OnDisable()
    {
        if (neighbouringTrisShort.IsCreated)
            neighbouringTrisShort.Dispose();
        
        if (neighbouringTris.IsCreated)
            neighbouringTris.Dispose();
        
        resultsBuffer?.Dispose();
    }
    
    [BurstCompile]
    private struct FloodNeighbouringTriangles : IJob
    {
        [WriteOnly]
        public NativeArray<int> result;
        [ReadOnly]
        private NativeArray<ushort> neighbouringTrisShort;
        [ReadOnly]
        private NativeArray<ushort> indices;
        
        private NativeList<ushort> trianglesToVisit;
        private BoolArray trianglesVisited;

        public FloodNeighbouringTriangles(NativeArray<int> result, NativeArray<ushort> indices, NativeArray<ushort> neighbouringTrisShort, ushort startTriangle) : this()
        {
            this.result = result;
            this.indices = indices;
            this.neighbouringTrisShort = neighbouringTrisShort;
            trianglesToVisit = new NativeList<ushort>((int)math.ceil(result.Length / 3f), Allocator.TempJob);
            trianglesToVisit.Add(startTriangle);
            trianglesVisited = new BoolArray((uint)indices.Length, Allocator.TempJob);
            trianglesVisited.Set(startTriangle, true);
        }

        public void Execute()
        {
            FloodNeighbours(0);
        }

        private void FloodNeighbours(int wave)
        {
            int initialLength = trianglesToVisit.Length;
            if (initialLength == 0)
            {
                return;
            }
            
            for (int i = 0; i < initialLength; i++)
            {
                ushort startIndex = trianglesToVisit[i];
                ushort index1 = neighbouringTrisShort[startIndex * 3];
                ushort index2 = neighbouringTrisShort[startIndex * 3 + 1];
                ushort index3 = neighbouringTrisShort[startIndex * 3 + 2];

                if (!trianglesVisited.Get(index1))
                {
                    trianglesVisited.Set(index1, true);
                    trianglesToVisit.Add(index1);
                    result[indices[index1 * 3]] = wave;
                    result[indices[index1 * 3 + 1]] = wave;
                    result[indices[index1 * 3 + 2]] = wave;
                }
                
                if (!trianglesVisited.Get(index2))
                {
                    trianglesVisited.Set(index2, true);
                    trianglesToVisit.Add(index2);
                    result[indices[index2 * 3]] = wave;
                    result[indices[index2 * 3 + 1]] = wave;
                    result[indices[index2 * 3 + 2]] = wave;
                }
                
                if (!trianglesVisited.Get(index3))
                {
                    trianglesVisited.Set(index3, true);
                    trianglesToVisit.Add(index3);
                    result[indices[index3 * 3]] = wave;
                    result[indices[index3 * 3 + 1]] = wave;
                    result[indices[index3 * 3 + 2]] = wave;
                }
            }
            trianglesToVisit.RemoveRangeSwapBack(0, initialLength);
            FloodNeighbours(++wave);
        }

        public void Dispose()
        {
            trianglesToVisit.Dispose();
            trianglesVisited.Dispose();
            indices.Dispose();
        }
    }

    [BurstCompile]
    private struct Vector3ToFloat3 : IJobFor
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
    private struct FindNeighbouringTriangles : IJobFor
    {
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int3> neighbouringTriangles;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<ushort> neighbouringTrianglesShort;
        
        [ReadOnly, DeallocateOnJobCompletion] private NativeArray<int> indices;
        [ReadOnly, DeallocateOnJobCompletion] private NativeArray<ushort> indicesShort;
        [ReadOnly, DeallocateOnJobCompletion] private NativeArray<float3> vertices;
        private int amountTriangles;
        private bool useShort;

        public FindNeighbouringTriangles(NativeArray<int3> neighbouringTriangles, NativeArray<int> indices, NativeArray<float3> vertices, int amountTriangles)
        {
            this.neighbouringTriangles = neighbouringTriangles;
            this.indices = indices;
            this.vertices = vertices;
            this.amountTriangles = amountTriangles;
            indicesShort = new NativeArray<ushort>(0, Allocator.TempJob);
            neighbouringTrianglesShort = new NativeArray<ushort>(0, Allocator.TempJob);
            
            useShort = false;
        }
        public FindNeighbouringTriangles(NativeArray<ushort> neighbouringTrianglesShort, NativeArray<ushort> indicesShort, NativeArray<float3> vertices, int amountTriangles)
        {
            this.neighbouringTrianglesShort = neighbouringTrianglesShort;
            this.indicesShort = indicesShort;
            this.vertices = vertices;
            this.amountTriangles = amountTriangles;
            indices = new NativeArray<int>(0, Allocator.TempJob);
            neighbouringTriangles = new NativeArray<int3>(0, Allocator.TempJob);
            
            useShort = true;
        }

        public void Execute(int index)
        {
            if (useShort)
            {
                FindNeighbouringTrianglesShort(index);
            }
            else
            {
                FindNeighbouringTrianglesInt(index);
            }
        }
        private void FindNeighbouringTrianglesShort(int index)
        {
            ushort baseIndex1 = indicesShort[index * 3];
            ushort baseIndex2 = indicesShort[index * 3 + 1];
            ushort baseIndex3 = indicesShort[index * 3 + 2];
            float3 baseVertex1 = vertices[baseIndex1];
            float3 baseVertex2 = vertices[baseIndex2];
            float3 baseVertex3 = vertices[baseIndex3];

            int amountTrianglesFound = 0;
            for (ushort i = 0; i < amountTriangles; i++)
            {
                if (amountTrianglesFound == 3)
                {
                    break;
                }
                
                ushort index1 = indicesShort[i * 3];
                ushort index2 = indicesShort[i * 3 + 1];
                ushort index3 = indicesShort[i * 3 + 2];
                float3 vertex1 = vertices[index1];
                float3 vertex2 = vertices[index2];
                float3 vertex3 = vertices[index3];

                int matchingVertices = 0;
                if (math.all(math.abs(baseVertex1 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                
                if (math.all(math.abs(baseVertex1 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                
                if (math.all(math.abs(baseVertex1 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;

                if (matchingVertices != 2)
                    continue;

                neighbouringTrianglesShort[index * 3 + amountTrianglesFound] = i;
                
                amountTrianglesFound++;
            }
        }
        private void FindNeighbouringTrianglesInt(int index)
        {
            int baseIndex1 = indices[index * 3];
            int baseIndex2 = indices[index * 3 + 1];
            int baseIndex3 = indices[index * 3 + 2];
            float3 baseVertex1 = vertices[baseIndex1];
            float3 baseVertex2 = vertices[baseIndex2];
            float3 baseVertex3 = vertices[baseIndex3];

            int amountTrianglesFound = 0;
            int3 neighbouringTriangle = new int3();
            for (int i = 0; i < amountTriangles; i++)
            {
                int index1 = indices[i * 3];
                int index2 = indices[i * 3 + 1];
                int index3 = indices[i * 3 + 2];
                float3 vertex1 = vertices[index1];
                float3 vertex2 = vertices[index2];
                float3 vertex3 = vertices[index3];

                int matchingVertices = 0;
                if (math.all(math.abs(baseVertex1 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                
                if (math.all(math.abs(baseVertex1 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                
                if (math.all(math.abs(baseVertex1 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;

                if (matchingVertices != 2)
                    continue;
                
                switch (amountTrianglesFound)
                {
                    case 0:
                        neighbouringTriangle.x = i;
                        break;
                    case 1:
                        neighbouringTriangle.y = i;
                        break;
                    case 2:
                        neighbouringTriangle.z = i;
                        break;
                }
                amountTrianglesFound++;
            }
            neighbouringTriangles[index] = neighbouringTriangle;
        }
    }
}
