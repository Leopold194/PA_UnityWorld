Shader "Custom/HumanRingIndicator"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.85, 0.15, 1)
        _Radius ("Radius", Range(0, 0.5)) = 0.1
        _Thickness ("Thickness", Range(0.001, 0.3)) = 0.03
        _Alpha ("Alpha Multiplier", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Radius;
                float _Thickness;
                float _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            // Anneau dessiné par distance au centre de l'UV (quad -0.5..0.5 -> uv 0..1,
            // donc d=0.5 correspond au bord du quad). _Radius/_Thickness pilotés depuis
            // le C# pour l'anneau permanent (joueur humain) et l'anneau de tir (expansion).
            half4 frag(Varyings IN) : SV_Target
            {
                float2 centered = IN.uv - 0.5;
                float d = length(centered);
                float ring = 1.0 - smoothstep(0.0, _Thickness, abs(d - _Radius));

                float alpha = ring * _Color.a * _Alpha;
                clip(alpha - 0.01);

                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
