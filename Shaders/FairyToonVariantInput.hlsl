// Separate-shader inputs. Names/defaults and optical/fur controls follow lilToon (MIT, lilxyzw).
#ifndef FAIRYTOON_VARIANT_INPUT_INCLUDED
#define FAIRYTOON_VARIANT_INPUT_INCLUDED

#if defined(FAIRYTOON_REFRACTION)
#define FT_VARIANT_PROPERTIES \
    float _RefractionStrength; \
    float _RefractionFresnelPower; \
    float _RefractionColorFromMain; \
    float4 _RefractionColor; \
    float _RefractionBlur;
#endif

#if defined(FAIRYTOON_FUR)
#define FT_VARIANT_PROPERTIES \
    float4 _FurNoiseMask_ST; \
    float _FurVectorScale; \
    float4 _FurVector; \
    float _VertexColor2FurVector; \
    float _FurGravity; \
    float _FurRandomize; \
    float _FurAO; \
    float _FurLayerNum; \
    float _FurRootOffset; \
    float _FurCutoutLength; \
    float _FurTouchStrength; \
    float4 _FurRimColor; \
    float _FurRimFresnelPower; \
    float _FurRimAntiLight; \
    float _FurCull; \
    float _FurSrcBlend; \
    float _FurDstBlend; \
    float _FurSrcBlendAlpha; \
    float _FurDstBlendAlpha; \
    float _FurBlendOp; \
    float _FurBlendOpAlpha; \
    float _FurSrcBlendFA; \
    float _FurDstBlendFA; \
    float _FurSrcBlendAlphaFA; \
    float _FurDstBlendAlphaFA; \
    float _FurBlendOpFA; \
    float _FurBlendOpAlphaFA; \
    float _FurZClip; \
    float _FurZWrite; \
    float _FurZTest; \
    float _FurStencilRef; \
    float _FurStencilReadMask; \
    float _FurStencilWriteMask; \
    float _FurStencilComp; \
    float _FurStencilPass; \
    float _FurStencilFail; \
    float _FurStencilZFail; \
    float _FurOffsetFactor; \
    float _FurOffsetUnits; \
    float _FurColorMask; \
    float _FurAlphaToMask; \
    float _FurMode; \
    float _SubpassCutoff;
#endif

#if defined(FAIRYTOON_GEM)
#define FT_VARIANT_PROPERTIES \
    float _RefractionStrength; \
    float _RefractionFresnelPower; \
    float _GemChromaticAberration; \
    float _GemEnvContrast; \
    float4 _GemEnvColor; \
    float _GemParticleLoop; \
    float4 _GemParticleColor; \
    float _GemVRParallaxStrength;
#endif

#include "FairyToonInput.hlsl"

#if defined(FAIRYTOON_REFRACTION)
#endif

#if defined(FAIRYTOON_FUR)
TEXTURE2D(_FurNoiseMask);
TEXTURE2D(_FurMask);
TEXTURE2D(_FurLengthMask);
TEXTURE2D(_FurVectorTex);
#endif

#if defined(FAIRYTOON_GEM)
#endif

#endif
