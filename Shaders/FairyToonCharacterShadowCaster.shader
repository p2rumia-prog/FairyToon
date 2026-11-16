// Character self-shadow caster for materials without FairyToon's own CharacterShadow pass (FairyToonFeature draws
// them with this). Opaque only: no alpha clip, both faces.
Shader "Hidden/FairyToon/CharacterShadowCaster"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "CharacterShadow"

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4x4 _FT_CharShadowCasterVP;
            float4 _FT_CharShadowCasterLight;
            float4 _FT_CharShadowCasterBias;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 L = _FT_CharShadowCasterLight.xyz;
                positionWS -= L * _FT_CharShadowCasterBias.x;
                positionWS -= normalWS * (_FT_CharShadowCasterBias.y * (1.0 - saturate(dot(L, normalWS))));
                return mul(_FT_CharShadowCasterVP, float4(positionWS, 1.0));
            }

            half4 Frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
