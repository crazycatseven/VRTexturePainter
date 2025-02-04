Shader "Custom/BrushIndicator"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0.5)
        _Thickness ("Border Thickness", Range(0.0, 0.1)) = 0.02
    }
    
    SubShader
    {
        Tags { 
            "RenderType"="Transparent" 
            "Queue"="Transparent+100" 
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "BrushIndicator"
            
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Thickness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 center = float2(0.5, 0.5);
                float dist = length(IN.uv - center) * 2;
                
                // 创建圆环效果
                float circle = 1 - step(1, dist);
                float border = step(1 - _Thickness, dist) * circle;
                
                half4 color = _Color;
                color.a *= border;
                
                return color;
            }
            ENDHLSL
        }
    }
}