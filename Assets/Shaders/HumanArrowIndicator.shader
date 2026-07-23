Shader "Custom/HumanArrowIndicator"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.9, 0.1, 1)
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.3)) = 0.05
        _PulseSpeed ("Pulse Speed", Float) = 4
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.25
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
                float _EdgeSoftness;
                float _PulseSpeed;
                float _PulseAmount;
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

            // Flèche pointant vers le bas (vers la tête du joueur) : base large en
            // haut de l'UV (y=1), pointe en bas (y=0). Glow doux ajouté sur les bords
            // + pulsation pour rester visible quelle que soit l'échelle du personnage.
            half4 frag(Varyings IN) : SV_Target
            {
                float halfWidth = lerp(0.0, 0.5, IN.uv.y);
                float dist = abs(IN.uv.x - 0.5) - halfWidth;

                float shape = 1.0 - smoothstep(0.0, _EdgeSoftness, dist);
                float glow = 1.0 - smoothstep(0.0, _EdgeSoftness * 4.0, dist);
                float pulse = 1.0 + _PulseAmount * sin(_Time.y * _PulseSpeed);

                float alpha = saturate(shape + glow * 0.35) * _Color.a * pulse;
                clip(alpha - 0.01);

                return half4(_Color.rgb * pulse, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
