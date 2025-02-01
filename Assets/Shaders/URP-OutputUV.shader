Shader "Custom/URP-OutputUV"
{
    Properties
    {
        
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "Forward"
            Tags{"LightMode" = "UniversalForward"}

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION; // Vertex position in object space
                float2 uv         : TEXCOORD0; // UV coordinates
            };


            struct Varyings
            {
                float4 positionHCS : SV_POSITION; // Clip space position
                float2 uv          : TEXCOORD0;   // UV passed to fragment shader
            };


            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                // Convert object space position to clip space
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS);

                // Pass the mesh UV directly
                OUT.uv = IN.uv;
                return OUT;

            }

            float4 Frag(Varyings IN) : SV_Target
            {
                // Ensure UV values are continuous
                float2 uv = frac(IN.uv); // Handle UV values beyond 1
                return float4(uv.x, uv.y, 0.0, 1.0);
            }


            ENDHLSL
        }
    }
}
