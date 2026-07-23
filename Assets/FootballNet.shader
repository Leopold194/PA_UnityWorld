Shader "Custom/GoalNetURP"
{
    Properties
    {
        _Color ("Net Color", Color) = (1,1,1,1)
        _CellSize ("Cell Size", Float) = 20
        _LineThickness ("Line Thickness", Range(0.0, 0.5)) = 0.08
        _Sag ("Sag Amount", Float) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 200
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _CellSize;
                float _LineThickness;
                float _Sag;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            float NetMask(float2 uv)
            {
                float2 scaled = uv * _CellSize;
                float a = abs(frac(scaled.x + scaled.y) - 0.5);
                float b = abs(frac(scaled.x - scaled.y) - 0.5);
                return min(a, b);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 posOS = IN.positionOS.xyz;
                float sag = sin(IN.uv.x * PI) * _Sag;
                posOS.y -= sag * (1.0 - IN.uv.y);

                VertexPositionInputs pos = GetVertexPositionInputs(posOS);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                clip(_LineThickness - NetMask(IN.uv));

                float3 normalWS = normalize(IN.normalWS);
                normalWS *= IS_FRONT_VFACE(facing, 1.0, -1.0);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float ndotl = saturate(dot(normalWS, mainLight.direction));
                float3 diffuse = mainLight.color * ndotl * mainLight.shadowAttenuation;
                float3 ambient = SampleSH(normalWS);

                float3 color = _Color.rgb * (diffuse + ambient);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _CellSize;
                float _LineThickness;
                float _Sag;
            CBUFFER_END

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings shadowVert(Attributes IN)
            {
                Varyings OUT;

                float3 posOS = IN.positionOS.xyz;
                float sag = sin(IN.uv.x * PI) * _Sag;
                posOS.y -= sag * (1.0 - IN.uv.y);

                float3 positionWS = TransformObjectToWorld(posOS);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                OUT.positionCS = ApplyShadowBias(positionWS, normalWS, _LightDirection);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 shadowFrag(Varyings IN) : SV_Target
            {
                float2 scaled = IN.uv * _CellSize;
                float a = abs(frac(scaled.x + scaled.y) - 0.5);
                float b = abs(frac(scaled.x - scaled.y) - 0.5);
                clip(_LineThickness - min(a, b));
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}