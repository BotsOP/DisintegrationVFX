using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VInspector;

public class HexagonifyManager : MonoBehaviour
{
    [SerializeField] private Mesh mesh;
    [SerializeField] private Material material;

    private GraphicsBuffer hexagonsBuffer;

    [Button]
    public void HexagonifyMesh()
    {
        int amountTriangles = mesh.triangles.Length / 3;
        
        List<Vector3> verticesList = new List<Vector3>();
        mesh.GetVertices(verticesList);
        NativeArray<float3> vertices = new NativeArray<float3>(verticesList.Count, Allocator.Persistent);
        
        HelperJobs.Vector3ToFloat3 vector3ToFloat3 = new HelperJobs.Vector3ToFloat3(verticesList.ToNativeArray(Allocator.TempJob), vertices);
        JobHandle jobHandle = new JobHandle();
        jobHandle = vector3ToFloat3.ScheduleParallel(verticesList.Count, 64, jobHandle);
        jobHandle.Complete();
        
        List<ushort> indicesList = new List<ushort>();
        mesh.GetIndices(indicesList, 0);
        NativeArray<ushort> indices = indicesList.ToNativeArray(Allocator.TempJob);

        // HelperJobs.FindMatchingVertices findMatchingVertices = new HelperJobs.FindMatchingVertices(vertices, indices, Allocator.TempJob);
        // jobHandle = findMatchingVertices.ScheduleParallel(verticesList.Count, 64, jobHandle);
        // jobHandle.Complete();
            
        Hexagonify hexagonify = new Hexagonify(vertices, indices);
        hexagonify.Schedule().Complete();
        mesh.SetUVs(1, hexagonify.hexagonIndex);
        hexagonify.Dispose();
    }

    private void OnDisable()
    {
        hexagonsBuffer?.Dispose();
        hexagonsBuffer = null;
    }

    private struct MeshHexagon
    {
        private float3 position;

        public MeshHexagon(float3 position)
        {
            this.position = position;
        }
    }

    [BurstCompile]
    private struct Hexagonify : IJob
    {
        private const float FLOAT_PRECISION = 0.00001f;
        [WriteOnly] public NativeArray<MeshHexagon> hexagons;
        [WriteOnly] public NativeArray<float> hexagonIndex;
        [ReadOnly] private NativeArray<float3> vertices;
        [ReadOnly] private NativeArray<ushort> indices;
        
        private NativeQueue<ushort> hexagonsToVisit;
        private NativeList<ushort> tempHexagonIndices;
        private NativeHashMap<float3, bool> hexagonsVisited;

        public Hexagonify(NativeArray<float3> vertices, NativeArray<ushort> indices)
        {
            this.vertices = vertices;
            this.indices = indices;
            
            hexagons = new NativeArray<MeshHexagon>(indices.Length / 3, Allocator.TempJob);

            hexagonsToVisit = new NativeQueue<ushort>(Allocator.TempJob);
            hexagonsToVisit.Enqueue(0);

            hexagonsVisited = new NativeHashMap<float3, bool>(indices.Length / 3, Allocator.TempJob);
            
            hexagonIndex = new NativeArray<float>(vertices.Length, Allocator.TempJob);

            tempHexagonIndices = new NativeList<ushort>(12, Allocator.TempJob);
        }

        public void Dispose()
        {
            hexagons.Dispose();
            hexagonIndex.Dispose();
            vertices.Dispose();
            indices.Dispose();
            hexagonsToVisit.Dispose();
            tempHexagonIndices.Dispose();
            hexagonsVisited.Dispose();
        }

        public void Execute()
        {
            ushort hexagonCount = 0;
            
            while (hexagonsToVisit.Count > 0)
            {
                ushort middleHexagonIndex = hexagonsToVisit.Dequeue();
                float3 vertex = vertices[middleHexagonIndex];
                
                if(hexagonsVisited.ContainsKey(vertex))
                    continue;
                hexagonsVisited.Add(vertex, true);

                ushort triangleCount = 0;
                for (int i = 0; i < indices.Length / 3; i++)
                {
                    ushort index1 = indices[i * 3 + 0];
                    ushort index2 = indices[i * 3 + 1];
                    ushort index3 = indices[i * 3 + 2];
                    
                    float3 vertex1 = vertices[index1];
                    float3 vertex2 = vertices[index2];
                    float3 vertex3 = vertices[index3];
                    
                    uint count = 0;
                    bool isMiddleIndex1 = math.distance(vertex1, vertex) < FLOAT_PRECISION;
                    count += (uint)math.select(0, 1, isMiddleIndex1);
                    
                    bool isMiddleIndex2 = math.distance(vertex2, vertex) < FLOAT_PRECISION;
                    count += (uint)math.select(0, 1, isMiddleIndex2);
                    
                    bool isMiddleIndex3 = math.distance(vertex3, vertex) < FLOAT_PRECISION;
                    count += (uint)math.select(0, 1, isMiddleIndex3);
                    
                    if(count != 1)
                        continue;
                    
                    if(!isMiddleIndex1)
                        tempHexagonIndices.Add(index1);
                    
                    if(!isMiddleIndex2)
                        tempHexagonIndices.Add(index2);
                    
                    if(!isMiddleIndex3)
                        tempHexagonIndices.Add(index3);

                    triangleCount++;
                    if(triangleCount == 6)
                        break;
                }

                for (int i = 0; i < tempHexagonIndices.Length / 2; i++)
                {
                    ushort hexagonTriangleIndex1 = tempHexagonIndices[i * 2 + 0];
                    ushort hexagonTriangleIndex2 = tempHexagonIndices[i * 2 + 1];

                    float3 vertexHexagon1 = vertices[hexagonTriangleIndex1];
                    float3 vertexHexagon2 = vertices[hexagonTriangleIndex2];
                    
                    hexagonIndex[hexagonTriangleIndex1] = middleHexagonIndex;
                    hexagonIndex[hexagonTriangleIndex2] = middleHexagonIndex;

                    for (int j = 0; j < indices.Length / 3; j++)
                    {
                        ushort index1 = indices[j * 3 + 0];
                        ushort index2 = indices[j * 3 + 1];
                        ushort index3 = indices[j * 3 + 2];

                        float3 vertex1 = vertices[index1];
                        float3 vertex2 = vertices[index2];
                        float3 vertex3 = vertices[index3];
                        
                        int matchingTriangleSides = 0;
                        
                        bool isMiddleIndex1 = math.distance(vertex1, vertexHexagon1) < FLOAT_PRECISION || math.distancesq(vertex1, vertexHexagon2) < FLOAT_PRECISION;
                        matchingTriangleSides += math.select(0, 1, isMiddleIndex1);
                        
                        bool isMiddleIndex2 = math.distance(vertex2, vertexHexagon1) < FLOAT_PRECISION || math.distancesq(vertex2, vertexHexagon2) < FLOAT_PRECISION;
                        matchingTriangleSides += math.select(0, 1, isMiddleIndex2);
                        
                        bool isMiddleIndex3 = math.distance(vertex3, vertexHexagon1) < FLOAT_PRECISION || math.distancesq(vertex3, vertexHexagon2) < FLOAT_PRECISION;
                        matchingTriangleSides += math.select(0, 1, isMiddleIndex3);
                        
                        if(matchingTriangleSides != 2)
                            continue;

                        bool bool1 = !hexagonsVisited.ContainsKey(vertex1);
                        if(!isMiddleIndex1 && bool1)
                            hexagonsToVisit.Enqueue(index1);

                        bool bool2 = !hexagonsVisited.ContainsKey(vertex2);
                        if(!isMiddleIndex2 && bool2)
                            hexagonsToVisit.Enqueue(index2);

                        bool bool3 = !hexagonsVisited.ContainsKey(vertex3);
                        if(!isMiddleIndex3 && bool3)
                            hexagonsToVisit.Enqueue(index3);
                        
                        break;
                    }
                }

                hexagons[hexagonCount] = new MeshHexagon(vertices[middleHexagonIndex]);

                hexagonCount++;
                tempHexagonIndices.Clear();
            }
        }
    }
}
