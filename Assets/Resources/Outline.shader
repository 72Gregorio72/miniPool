// Full-screen outline drawn over the opaque scene by Outline.cs: edges from the camera depth and normals textures.
Shader "Hidden/MiniPool/Outline"
{
    Properties
    {
        _Color ("Color", Color) = (0.04, 0.04, 0.06, 1)
        _Width ("Width (pixels)", Float) = 1
        _Depth ("Depth threshold (relative)", Float) = 0.01
        _Normal ("Normal threshold (cosine)", Float) = 0.6 // 0.6 = 53 degrees: box edges yes, facets of 8-sided round shapes and of the balls no
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            half4 _Color;
            float _Width, _Depth, _Normal;

            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(id);
                o.uv = GetFullScreenTriangleTexCoord(id);
                return o;
            }

            // device depth, bigger = nearer, 0 = far plane
            float Raw(float2 uv)
            {
                float d = SampleSceneDepth(uv);
            #if !UNITY_REVERSED_Z
                d = 1 - d;
            #endif
                return d;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 o = _Width * _ScreenSize.zw;
                float2 right = i.uv + float2(o.x, 0), up = i.uv + float2(0, o.y);
                float c = Raw(i.uv);
                // device depth is linear on screen along a plane, so its second difference is 0 on any flat surface, whatever the slope.
                // Only the positive side (a neighbour is nearer): the line is drawn outside the silhouette and doesn't eat thin objects.
                float jump = max(Raw(right) + Raw(i.uv - float2(o.x, 0)) - 2 * c, Raw(up) + Raw(i.uv - float2(0, o.y)) - 2 * c) / max(c, 1e-6);
                float3 n = SampleSceneNormals(i.uv);
                bool crease = c > 0 && min(dot(n, SampleSceneNormals(right)), dot(n, SampleSceneNormals(up))) < _Normal;
                return half4(_Color.rgb, jump > _Depth || crease ? _Color.a : 0);
            }
            ENDHLSL
        }
    }
}
