// Geometry-generated fur ribbons, adapted from lilToon (MIT, lilxyzw). DX11/Vulkan; no Metal geometry support.
#ifndef FAIRYTOON_FUR_INCLUDED
#define FAIRYTOON_FUR_INCLUDED

#include "FairyToonVariants.hlsl"

struct FairyToonFurAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv0 : TEXCOORD0;
    half4 color : COLOR;
    uint vertexID : SV_VertexID;
    FT_LAYER_ATTRIBUTES
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct FairyToonFurVertexOutput
{
    FairyToonVaryings body;
    float3 furVector : TEXCOORD5;
    uint vertexID : TEXCOORD6;
};

struct FairyToonFurVaryings
{
    FairyToonVaryings body;
    float furLayer : TEXCOORD5;
};

FairyToonFurVertexOutput FairyToonFurVertex(FairyToonFurAttributes input)
{
    FairyToonFurVertexOutput output = (FairyToonFurVertexOutput)0;
    FairyToonAttributes bodyInput = (FairyToonAttributes)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, bodyInput);
    bodyInput.positionOS = input.positionOS;
    bodyInput.normalOS = input.normalOS;
    bodyInput.tangentOS = input.tangentOS;
    bodyInput.uv0 = input.uv0;
    #if defined(FT_LAYER_EXTRA_UVS)
        bodyInput.uv1 = input.uv1;
        bodyInput.uv2 = input.uv2;
        bodyInput.uv3 = input.uv3;
    #endif
    output.body = FairyToonForwardVertex(bodyInput);

    float2 uvMain = input.uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
    float3 direction = _FurVector.xyz + float3(0, 0, 0.001);
    if (_VertexColor2FurVector > 0.5) direction = BlendNormal(direction, input.color.xyz);
    float3 vectorTex = UnpackNormalScale(SAMPLE_TEXTURE2D_LOD(_FurVectorTex, sampler_ft_linear_repeat, uvMain, 0), _FurVectorScale);
    direction = BlendNormal(direction, vectorTex);
    float3 bitangentOS = normalize(cross(input.normalOS, input.tangentOS.xyz)) * input.tangentOS.w;
    float3 vectorOS = mul(normalize(direction), float3x3(input.tangentOS.xyz, bitangentOS, input.normalOS)) * _FurVector.w;
    output.furVector = TransformObjectToWorldDir(vectorOS, false);
    output.furVector.y -= _FurGravity * length(output.furVector);
    output.vertexID = input.vertexID;
    #if !defined(FAIRYTOON_FUR_SECOND)
        if (_FurMode > 1.5) output.furVector *= _FurCutoutLength;
    #endif
    return output;
}

// Interpolate only fields actually consumed by this pass; extra layer UVs retain the shared feature gate.
FairyToonVaryings FairyToonFurInterpolate(triangle FairyToonFurVertexOutput input[3], float3 weights)
{
    FairyToonVaryings o = input[0].body;
    o.positionWS = input[0].body.positionWS * weights.x + input[1].body.positionWS * weights.y + input[2].body.positionWS * weights.z;
    o.normalWS = input[0].body.normalWS * weights.x + input[1].body.normalWS * weights.y + input[2].body.normalWS * weights.z;
    o.tangentWS = input[0].body.tangentWS * weights.x + input[1].body.tangentWS * weights.y + input[2].body.tangentWS * weights.z;
    o.uv0 = input[0].body.uv0 * weights.x + input[1].body.uv0 * weights.y + input[2].body.uv0 * weights.z;
    #if defined(FT_LAYER_EXTRA_UVS)
        o.layerUV12 = input[0].body.layerUV12 * weights.x + input[1].body.layerUV12 * weights.y + input[2].body.layerUV12 * weights.z;
        o.layerUV3 = input[0].body.layerUV3 * weights.x + input[1].body.layerUV3 * weights.y + input[2].body.layerUV3 * weights.z;
    #endif
    return o;
}

void FairyToonAppendFur(triangle FairyToonFurVertexOutput input[3], float3 weights,
    inout TriangleStream<FairyToonFurVaryings> stream)
{
    FairyToonFurVaryings o;
    o.body = FairyToonFurInterpolate(input, weights);
    o.body.positionCS = TransformWorldToHClip(o.body.positionWS);
    o.body.fogFactor = ComputeFogFactor(o.body.positionCS.z);
    o.furLayer = 0.0;
    stream.Append(o);
    o.body.positionWS += input[0].furVector * weights.x + input[1].furVector * weights.y + input[2].furVector * weights.z;
    o.body.positionCS = TransformWorldToHClip(o.body.positionWS);
    o.body.fogFactor = ComputeFogFactor(o.body.positionCS.z);
    o.furLayer = 1.0;
    stream.Append(o);
}

[maxvertexcount(26)]
void FairyToonFurGeometry(triangle FairyToonFurVertexOutput input[3], inout TriangleStream<FairyToonFurVaryings> stream)
{
    UNITY_SETUP_INSTANCE_ID(input[0].body);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input[0].body);
    uint3 ids = uint3(input[0].vertexID, input[1].vertexID, input[2].vertexID);
    // Triangle-local hash and length-mask order match lil_common_vert_fur.
    [unroll] for (uint i = 0; i < 3; ++i)
    {
        uint seed = ids.x + ids.y + ids.z + 2u * ids[i];
        uint3 noise = seed * uint3(1597334677u, 3812015801u, 2912667907u);
        float3 randomDirection = normalize(float3(noise) * (2.0 / 4294967295.0) - 1.0);
        input[i].furVector += randomDirection * _FurVector.w * _FurRandomize;
        float2 uvMain = input[i].body.uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
        input[i].furVector *= SAMPLE_TEXTURE2D_LOD(_FurLengthMask, sampler_ft_linear_repeat, uvMain, 0).r;
    }
    // lilToon's perimeter / interior barycentric strip. Densities 1/2/3 emit 8/14/26 vertices.
    FairyToonAppendFur(input, float3(1, 0, 0), stream);
    if (_FurLayerNum >= 2) FairyToonAppendFur(input, float3(0, 1, 1) * 0.5, stream);
    FairyToonAppendFur(input, float3(0, 1, 0), stream);
    if (_FurLayerNum >= 2) FairyToonAppendFur(input, float3(1, 0, 1) * 0.5, stream);
    FairyToonAppendFur(input, float3(0, 0, 1), stream);
    if (_FurLayerNum >= 2) FairyToonAppendFur(input, float3(1, 1, 0) * 0.5, stream);
    if (_FurLayerNum >= 3)
    {
        FairyToonAppendFur(input, float3(1, 4, 1) / 6.0, stream);
        FairyToonAppendFur(input, float3(0, 1, 1) * 0.5, stream);
        FairyToonAppendFur(input, float3(1, 1, 4) / 6.0, stream);
        FairyToonAppendFur(input, float3(1, 0, 1) * 0.5, stream);
        FairyToonAppendFur(input, float3(4, 1, 1) / 6.0, stream);
        FairyToonAppendFur(input, float3(1, 1, 0) * 0.5, stream);
    }
    FairyToonAppendFur(input, float3(1, 0, 0), stream);
    stream.RestartStrip();
}

half4 FairyToonFurFragment(FairyToonFurVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input.body);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input.body);
    // Shell keywords omit normal maps; shared setup keeps geometric strand normals (facing in Endfield).
    FairyToonSurface s = FairyToonForwardSurface(input.body, IS_FRONT_VFACE(isFront, 1.0, -1.0));
    #if defined(_FT_ENDFIELD)
        // Shells skip ShadeBase; orient both geometric normals before RimShade's strength blend.
        s.origN = s.N;
    #endif
    float furLayer = input.furLayer;
    float shifted = furLayer - furLayer * _FurRootOffset + _FurRootOffset;
    float furNoise = SAMPLE_TEXTURE2D(_FurNoiseMask, sampler_MainTex,
        input.body.uv0 * _FurNoiseMask_ST.xy + _FurNoiseMask_ST.zw).r;
    float furMask = SAMPLE_TEXTURE2D(_FurMask, sampler_MainTex, s.uvMain).r;
    #if defined(FAIRYTOON_FUR_SECOND)
        bool cutout = false;
    #else
        bool cutout = _FurMode > 0.5;
    #endif
    float furAlpha = cutout ? saturate(furNoise - shifted * pow(abs(shifted), 3.0) + 0.25)
                            : saturate(furNoise - shifted * shifted * shifted);
    s.alpha *= furAlpha * furMask;
    if (cutout)
    {
        s.alpha = saturate(s.alpha * 5.0 - 2.0);
        clip(s.alpha - 1e-4);
    }
    else clip(s.alpha - max(_Cutoff, 1e-4));

    // Source fur AO modifies albedo before shadow colours and rim shade, not the final rim light.
    float ao = _FurAO * saturate(1.0 - fwidth(furLayer));
    s.albedo *= cutout ? furLayer * ao * 2.0 + 1.0 - ao
                      : saturate(1.0 - furNoise + furNoise * furLayer) * _FurAO * 1.25 + 1.0 - _FurAO;
    half3 col, lightColor;
    #if defined(_FT_ENDFIELD)
        // Fur remains deliberately cheap in Endfield: one shadowed main light plus SH ambient, no skin/hair BRDF.
        Light mainLight = GetMainLight(TransformWorldToShadowCoord(s.positionWS));
        float nl = saturate(dot(s.N, mainLight.direction));
        half3 ambient = max(0.0, FairyToonSH(s.N, s));
        half3 lighting = mainLight.color * (nl * mainLight.distanceAttenuation * mainLight.shadowAttenuation)
                       + ambient;
        col = s.albedo * FairyToonCorrectLight(lighting);
        lightColor = FairyToonCorrectLight(mainLight.color * mainLight.distanceAttenuation + ambient);
    #else
        FairyToonLight l = FairyToonGetLight(s);
        lightColor = l.lightColor;
        col = FairyToonDiffuse(s, l);
        FairyToonAdditionalLights(s, l);
        col = min(col + s.albedo * l.addLightColor * l.aoLit, s.albedo * _LightMaxLimit);
    #endif
    #if defined(_FT_RIMSHADE)
        col *= FairyToonRimShadeMultiplier(s);
    #endif
    half3 invLighting = saturate((1.0 - lightColor) * sqrt(max(lightColor, 0.0)));
    float rim = pow(saturate(1.0 - abs(dot(s.N, s.V))), _FurRimFresnelPower);
    rim *= lerp(1.0, FairyToonGray(invLighting), _FurRimAntiLight) * furLayer;
    col += _FurRimColor.rgb * rim * lightColor;
    return half4(MixFog(col, input.body.fogFactor), s.alpha);
}

#endif
