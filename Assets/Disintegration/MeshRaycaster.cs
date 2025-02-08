using UnityEngine;

public class MeshRaycaster
{
    public static bool RaycastMesh(Mesh mesh, Ray ray, Transform meshTransform, out Vector3 v0, out Vector3 v1, out Vector3 v2)
    {
        v0 = v1 = v2 = Vector3.zero; // Default output
        
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        float closestDistance = float.MaxValue;
        bool hitFound = false;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            // Get triangle vertices in world space
            Vector3 vertex0 = meshTransform.TransformPoint(vertices[triangles[i]]);
            Vector3 vertex1 = meshTransform.TransformPoint(vertices[triangles[i + 1]]);
            Vector3 vertex2 = meshTransform.TransformPoint(vertices[triangles[i + 2]]);

            if (RayIntersectsTriangle(ray, vertex0, vertex1, vertex2, out float distance))
            {
                if (distance < closestDistance)
                {
                    Debug.Log($"{i}    {triangles[i]} {triangles[i + 1]} {triangles[i + 2]}");
                    closestDistance = distance;
                    v0 = vertex0;
                    v1 = vertex1;
                    v2 = vertex2;
                    hitFound = true;
                }
            }
        }

        return hitFound;
    }

    private static bool RayIntersectsTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
    {
        distance = 0f;

        // Edge vectors
        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;
        
        // Compute determinant
        Vector3 h = Vector3.Cross(ray.direction, edge2);
        float a = Vector3.Dot(edge1, h);

        if (Mathf.Abs(a) < 1e-6f) return false; // Ray is parallel
        
        float f = 1.0f / a;
        Vector3 s = ray.origin - v0;
        float u = f * Vector3.Dot(s, h);

        if (u < 0.0f || u > 1.0f) return false;
        
        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(ray.direction, q);

        if (v < 0.0f || u + v > 1.0f) return false;
        
        // Compute intersection distance
        distance = f * Vector3.Dot(edge2, q);

        return distance > 1e-6f;
    }
}
