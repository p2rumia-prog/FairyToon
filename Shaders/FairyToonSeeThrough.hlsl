// SeeThroughHair 1.5.2 by goorm: stencil fringe, view-angle fade and fake shadow, ported with permission.
#ifndef FAIRYTOON_SEETHROUGH_INCLUDED
#define FAIRYTOON_SEETHROUGH_INCLUDED

#include "FairyToonInput.hlsl"

half FairyToonSeeThroughAlpha()
{
    if (_SeeThroughFade < 0.5) return _SeeThroughOpacity;

    float3 origin = _SeeThroughHead.w > 0.5 ? _SeeThroughHead.xyz : TransformObjectToWorld(float3(0, 0, 0));
    float3 forward = dot(_FaceForward.xyz, _FaceForward.xyz) > 1e-8
        ? normalize(_FaceForward.xyz) : TransformObjectToWorldDir(float3(0, 0, 1));
    float3 right = dot(_FaceRight.xyz, _FaceRight.xyz) > 1e-8
        ? normalize(_FaceRight.xyz) : TransformObjectToWorldDir(float3(1, 0, 0));
    float3 up = SafeNormalize(cross(forward, right));
    float3 viewDirectionWS = GetCameraPositionWS() - origin;
    float3 viewDirectionHead = SafeNormalize(float3(dot(viewDirectionWS, right), dot(viewDirectionWS, up), dot(viewDirectionWS, forward)));

    float horizontalStart = cos(radians(min(_SeeThroughFadeHorizontalMin, _SeeThroughFadeHorizontalMax)));
    float horizontalEnd = cos(radians(max(_SeeThroughFadeHorizontalMin, _SeeThroughFadeHorizontalMax)));
    float verticalStart = cos(radians(min(_SeeThroughFadeVerticalMin, _SeeThroughFadeVerticalMax)));
    float verticalEnd = cos(radians(max(_SeeThroughFadeVerticalMin, _SeeThroughFadeVerticalMax)));
    float horizontalFront = smoothstep(horizontalEnd, horizontalStart, viewDirectionHead.z);
    float verticalFront = smoothstep(verticalEnd, verticalStart, length(viewDirectionHead.xz));
    float frontAmount = min(horizontalFront, verticalFront);
    return lerp(_SeeThroughFadeSideAlpha, _SeeThroughOpacity, frontAmount);
}

float3 FairyToonFakeShadowOffsetWS()
{
    float3 customDirectionWS = TransformObjectToWorldDir(_FakeShadowVector.xyz, false);
    float magnitudeSquared = dot(customDirectionWS, customDirectionWS);
    float3 contributionWS = 0;
    if (magnitudeSquared > 1e-8)
        contributionWS = length(_FakeShadowVector.xyz) * customDirectionWS * rsqrt(magnitudeSquared);
    float3 lightDirectionWS = SafeNormalize(_MainLightPosition.xyz + contributionWS);
    return lightDirectionWS * _FakeShadowVector.w;
}

// (share the depth clip, including the base texture's alpha mask, without another material buffer)
#include "FairyToonPasses.hlsl"

// The hair colour pass uses Forward varyings; auxiliary programs use the coverage layout.
#if !defined(FAIRYTOON_PASS_FORWARD)
struct FairyToonSeeThroughVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv0 : TEXCOORD0;
    FT_LAYER_VARYINGS
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

FairyToonSeeThroughVaryings FairyToonSeeThroughVertex(FairyToonAttributes input)
{
    FairyToonSeeThroughVaryings output = (FairyToonSeeThroughVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    #if defined(_FT_EF_IRIS)
        float3 positionWS = FairyToonEFPupilVertexPositionWS(input.positionOS.xyz, input.normalOS, input.tangentOS, input.uv0);
        output.positionCS = TransformWorldToHClip(positionWS);
    #endif
    output.uv0.xy = input.uv0;
    FT_TRANSFER_LAYERS(input, output)
    return output;
}

half4 FairyToonSeeThroughMaskFragment(FairyToonSeeThroughVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    clip(0.5 - abs(_SeeThroughRole - 1.0));
    FairyToonDepthClip(FT_LAYER_CONTEXT(input, IS_FRONT_VFACE(isFront, 1.0, -1.0)));
    half4 mask = SAMPLE_TEXTURE2D(_SeeThroughMask, sampler_ft_linear_clamp, input.uv0.xy);
    int channel = clamp((int)round(_SeeThroughMaskChannel), 0, 3);
    clip(mask[channel] - _SeeThroughMaskCutoff);
    return 0;
}

half4 FairyToonSeeThroughEarlyZFragment(FairyToonSeeThroughVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    clip(0.5 - abs(_SeeThroughRole - 2.0));
    clip(_SeeThroughEarlyZ - 0.5);
    FairyToonDepthClip(FT_LAYER_CONTEXT(input, IS_FRONT_VFACE(isFront, 1.0, -1.0)));
    return 0;
}

FairyToonSeeThroughVaryings FairyToonFakeShadowVertex(FairyToonAttributes input)
{
    FairyToonSeeThroughVaryings output = FairyToonSeeThroughVertex(input);
    UNITY_SETUP_INSTANCE_ID(input);
    float4 lightShiftCS = mul(UNITY_MATRIX_VP, float4(FairyToonFakeShadowOffsetWS(), 0));
    output.positionCS -= lightShiftCS;
    return output;
}

half4 FairyToonFakeShadowFragment(FairyToonSeeThroughVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    clip(0.5 - abs(_SeeThroughRole - 2.0));
    clip(_UseFakeShadow - 0.5);
    FairyToonDepthClip(FT_LAYER_CONTEXT(input, IS_FRONT_VFACE(isFront, 1.0, -1.0)));
    // (a sun shadow: it fades out with FairyToonCharacter's DayStrength in the shade)
    half day = _FT_EFLighting.w > 0.5 ? _FT_EFLighting.x : 1.0;
    return half4(lerp(1.0, _FakeShadowColor.rgb, day), 1.0);
}

#endif // !FAIRYTOON_PASS_FORWARD

#endif
