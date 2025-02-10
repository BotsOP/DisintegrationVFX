Shader "Custom/RigidTriangleRotation"
{
    Properties
    {
        _MaxDistance ("Max Distance", float) = 1
        _RotationAngle ("Rotation Angle", Range(-1, 1)) = 0
        _WireframeAliasing ("Rotation Angle", Range(0, 10)) = 0
        _RotationDirection ("Rotation Direction", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma require geometry
            #pragma geometry geom
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 mask : TEXCOORD0;
            };

            struct v2g
            {
                float4 positionWS : TEXCOORD0;  // Using world space positions for calculations
                float3 normalWS : TEXCOORD1;
                float mask : TEXCOORD2;
            };

            struct g2f
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float mask : TEXCOORD1;
                float3 barycentric : TEXCOORD2;
            };

            float _RotationAngle;
            float4 _RotationDirection;
            float _WireframeAliasing;
            float _MaxDistance;

            // Helper function to create rotation matrix around arbitrary axis
            float3x3 CreateRotationMatrix(float3 axis, float angle)
            {
                float radians2 = radians(angle);
                float c = cos(radians2);
                float s = sin(radians2);
                float t = 1.0 - c;
                
                float x = axis.x;
                float y = axis.y;
                float z = axis.z;
                
                return float3x3(
                    t * x * x + c,      t * x * y - s * z,  t * x * z + s * y,
                    t * x * y + s * z,  t * y * y + c,      t * y * z - s * x,
                    t * x * z - s * y,  t * y * z + s * x,  t * z * z + c
                );
            }

            v2g vert(Attributes input)
            {
                v2g output;
                
                // Transform to world space but keep as a separate position component
                output.positionWS = float4(TransformObjectToWorld(input.positionOS.xyz), 1.0);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.mask = input.mask.x;
                
                return output;
            }

            [maxvertexcount(3)]
            void geom(triangle v2g input[3], inout TriangleStream<g2f> outStream)
            {
                // Find the pivot vertex (farthest from direction)
                float minProj = 1e10;
                int pivotIdx = 0;
                float3 origin = _RotationDirection;
                float3 avgPos = (input[0].positionWS + input[1].positionWS + input[2].positionWS) / 3;
                float3 avgNormal = (input[0].normalWS + input[1].normalWS + input[2].normalWS) / 3;
                float avgMask = (input[0].mask + input[1].mask + input[2].mask) / 3;
                float3 direction = normalize(_RotationDirection.xyz - avgPos);
                
                // Calculate projections and find pivot
                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    float dist = distance(origin, input[i].positionWS);
                    if (dist > minProj)
                    {
                        minProj = dist;
                        pivotIdx = i;
                    }
                }

                // Store original positions and calculate edge vectors from pivot
                float3 pivotPos = avgPos;
                float3 originalPositions[3];
                float3 edgeVectors[3];
                
                [unroll]
                for (int j = 0; j < 3; j++)
                {
                    originalPositions[j] = input[j].positionWS.xyz;
                    edgeVectors[j] = originalPositions[j] - pivotPos;
                }

                float3 barycentricCoords[3];
                barycentricCoords[0] = float3(1, 0, 0);
                barycentricCoords[1] = float3(0, 1, 0);
                barycentricCoords[2] = float3(0, 0, 1);

                avgMask = avgMask / _MaxDistance;
                avgMask = 1 - avgMask;
                avgMask -= _RotationAngle;

                // Output the transformed triangle
                g2f output;
                [unroll]
                for (int k = 0; k < 3; k++)
                {
                    float3x3 rotationMatrix = CreateRotationMatrix(cross(direction, avgNormal), saturate(avgMask) * 360);
                    // if (k == pivotIdx)
                    // {
                    //     // Pivot vertex stays in place
                    //     output.positionCS = TransformWorldToHClip(originalPositions[k]);
                    // }
                    // else
                    {
                        // Rotate other vertices around pivot while maintaining distances
                        float3 rotatedPos = pivotPos + mul(rotationMatrix, edgeVectors[k]);
                        output.positionCS = TransformWorldToHClip(rotatedPos);
                    }
                    if (avgMask > 0.25)
                    {
                        output.positionCS = TransformWorldToHClip(float4(0, 0, 0, 1));
                    }
                    
                    // Transform normal
                    // output.normalWS = k == pivotIdx ? 
                    //     input[k].normalWS : 
                    //     mul(rotationMatrix, avgNormal);
                    output.normalWS = avgNormal;
                    output.mask = avgMask;
                    output.barycentric = barycentricCoords[k];
                    outStream.Append(output);
                }
                outStream.RestartStrip();
            }

            float4 frag(g2f input) : SV_Target
            {
                float3 unitWidth = fwidth(input.barycentric);
                float3 aliased = smoothstep(float3(0.0, 0.0, 0.0), unitWidth * _WireframeAliasing, input.barycentric);
                // Use the coordinate closest to the edge.
                float alpha = 1 - min(aliased.x, min(aliased.y, aliased.z));
                float color = input.mask.x * alpha;
                return float4(color, color, color, 1);
                // Simple lighting calculation for visualization
                float3 normalWS = normalize(input.normalWS);
                float3 lightDir = normalize(float3(1, 1, -1));
                float ndotl = max(0, dot(normalWS, lightDir));
                
                return float4(ndotl.xxx, 1);
            }
            ENDHLSL
        }
    }
}