// Glitter adapted from lilToon (MIT, lilxyzw). The procedural cells and optional
// particle-shape atlas work independently of VRChat or any other game SDK.
#ifndef FAIRYTOON_GLITTER_INCLUDED
#define FAIRYTOON_GLITTER_INCLUDED

#if defined(_FT_GLITTER)

// Keep the same integer hash as lilToon so the same UVs produce the same facets.
// Original hash reference: https://www.shadertoy.com/view/MdcfDj
void FairyToonGlitterHash(float2 pos, out float3 noise0, out float3 noise1,
    out float3 noise2, out float3 noise3)
{
    const uint m1 = 1597334677u;
    const uint m2 = 3812015801u;
    const uint m3 = 2912667907u;
    uint2 q = (uint2)pos;
    uint4 q2 = uint4(q.x, q.y, q.x + 1u, q.y + 1u) * uint4(m1, m2, m1, m2);
    uint3 n0 = (q2.x ^ q2.y) * uint3(m1, m2, m3);
    uint3 n1 = (q2.z ^ q2.y) * uint3(m1, m2, m3);
    uint3 n2 = (q2.x ^ q2.w) * uint3(m1, m2, m3);
    uint3 n3 = (q2.z ^ q2.w) * uint3(m1, m2, m3);
    const float uintToFloat = 1.0 / float(0xffffffffu);
    noise0 = float3(n0) * uintToFloat;
    noise1 = float3(n1) * uintToFloat;
    noise2 = float3(n2) * uintToFloat;
    noise3 = float3(n3) * uintToFloat;
}

float FairyToonGlitterDistance(float2 a, float2 b)
{
    float2 delta = a - b;
    return dot(delta, delta);
}

float4 FairyToonGlitterVoronoi(float2 pos, out float2 nearOffset, float scaleRandomize)
{
    float3 noise0, noise1, noise2, noise3;
    FairyToonGlitterHash(pos, noise0, noise1, noise2, noise3);

    float4 fracPos = frac(pos).xyxy + float4(0.5, 0.5, -0.5, -0.5);
    float4 dist = float4(
        FairyToonGlitterDistance(fracPos.xy, noise0.xy),
        FairyToonGlitterDistance(fracPos.zy, noise1.xy),
        FairyToonGlitterDistance(fracPos.xw, noise2.xy),
        FairyToonGlitterDistance(fracPos.zw, noise3.xy));
    dist = lerp(dist, dist / max(float4(noise0.z, noise1.z, noise2.z, noise3.z), 0.001), scaleRandomize);

    float3 offset0 = dist.x < dist.y ? float3(0, 0, dist.x) : float3(1, 0, dist.y);
    float3 offset1 = dist.z < dist.w ? float3(0, 1, dist.z) : float3(1, 1, dist.w);
    nearOffset = offset0.z < offset1.z ? offset0.xy : offset1.xy;
    float4 near0 = dist.x < dist.y ? float4(noise0, dist.x) : float4(noise1, dist.y);
    float4 near1 = dist.z < dist.w ? float4(noise2, dist.z) : float4(noise3, dist.w);
    return near0.w < near1.w ? near0 : near1;
}

float3 FairyToonGlitterPattern(float2 uv, float3 N, float3 V, float3 cameraDirection, float3 L)
{
    // Derivative-driven cell reduction preserves the sparkle density at distance.
    float2 pos = uv * _GlitterParams1.xy;
    float2 width = fwidth(pos);
    float factor = frac(sin(dot(floor(pos / floor(width + 3.0)), float2(12.9898, 78.233))) * 46203.4357) + 0.5;
    float2 cellFactor = floor(width + factor * 0.5);
    pos = pos / max(1.0, cellFactor) + _GlitterParams1.xy * cellFactor;

    float2 nearOffset;
    float4 nearest = FairyToonGlitterVoronoi(pos, nearOffset, _GlitterScaleRandomize);
    float3 facetNormal = abs(frac(nearest.xyz * 14.274 + _Time.x * _GlitterParams2.x) * 2.0 - 1.0);
    facetNormal = facetNormal * 2.0 - 1.0;
    facetNormal *= rsqrt(max(dot(facetNormal, facetNormal), 1e-8));

    float sparkle = dot(facetNormal, cameraDirection);
    sparkle = abs(frac(sparkle * _GlitterSensitivity + _GlitterSensitivity) - 0.5) * 4.0 - 1.0;
    sparkle = saturate(1.0 - (sparkle * _GlitterParams1.w + _GlitterParams1.w));
    sparkle = pow(sparkle, _GlitterPostContrast);
    sparkle *= saturate((_GlitterParams1.z - nearest.w) / max(fwidth(nearest.w), 1e-6));

    float3 halfDirection = V + L * _GlitterParams2.z;
    halfDirection *= rsqrt(max(dot(halfDirection, halfDirection), 1e-8));
    float nh = saturate(dot(N, halfDirection));
    sparkle = saturate(sparkle * saturate(nh * _GlitterParams2.y + 1.0 - _GlitterParams2.y));
    float3 sparkleColor = sparkle - sparkle * frac(nearest.xyz * 278.436) * _GlitterParams2.w;

    if (_GlitterApplyShape > 0.5)
    {
        float size = max(_GlitterParams1.z, 1e-6);
        float2 shapeUV = pos - floor(pos) - nearOffset + 0.5 - nearest.xy;
        shapeUV = shapeUV / size * _GlitterShapeTex_ST.xy + _GlitterShapeTex_ST.zw;
        if (_GlitterAngleRandomize > 0.5)
        {
            float sn, cs;
            sincos(nearest.z * 785.238, sn, cs);
            shapeUV = float2(shapeUV.x * cs - shapeUV.y * sn, shapeUV.x * sn + shapeUV.y * cs);
        }
        float randomScale = lerp(1.0, rsqrt(max(nearest.z, 0.001)), _GlitterScaleRandomize);
        shapeUV = shapeUV * randomScale + 0.5;
        bool insideShape = all(shapeUV == saturate(shapeUV));
        float2 atlas = max(floor(_GlitterAtras.xy), 1.0);
        shapeUV = (shapeUV + floor(nearest.xy * atlas)) / atlas;
        float2 mipFactor = 0.125 / size * atlas * _GlitterShapeTex_ST.xy * randomScale;
        float4 shape = SAMPLE_TEXTURE2D_GRAD(_GlitterShapeTex, sampler_ft_linear_clamp, shapeUV,
            abs(ddx(pos)) * mipFactor.x, abs(ddy(pos)) * mipFactor.y);
        shape.a = insideShape ? shape.a : 0.0;
        sparkleColor *= shape.rgb * shape.a;
    }
    return sparkleColor;
}

// lilToon adds glitter after premultiplication. Convert that addition into the
// straight RGB expected by FairyToon's SrcAlpha blend without changing coverage.
float FairyToonGlitterTransparency(float alpha, float applyTransparency)
{
    if (alpha <= 0.0) return 0.0;
    if (applyTransparency > 0.5) return 1.0;
    return 1.0 / max(alpha, 1.0 / 4096.0);
}

half3 FairyToonGlitter(FairyToonSurface s, FairyToonLight l, half3 col)
{
    if (_UseGlitter < 0.5) return col;

    float2 colorUV = s.uvMain;
    if (_GlitterColorTex_UVMode > 0.5) colorUV = s.layers.uv1;
    if (_GlitterColorTex_UVMode > 1.5) colorUV = s.layers.uv2;
    if (_GlitterColorTex_UVMode > 2.5) colorUV = s.layers.uv3;
    half4 glitterColor = _GlitterColor * SAMPLE_TEXTURE2D(_GlitterColorTex, sampler_MainTex,
        colorUV * _GlitterColorTex_ST.xy + _GlitterColorTex_ST.zw);

    float3 N = lerp(s.origN, s.N, _GlitterNormalStrength);
    float3 cameraDirection = lerp(UNITY_MATRIX_V[2].xyz, s.V, _GlitterVRParallaxStrength);
    float2 glitterUV = _GlitterUVMode > 0.5 ? s.layers.uv1 : s.layers.uv0;
    float3 sparkleColor = FairyToonGlitterPattern(glitterUV, N, s.V, cameraDirection, l.L);
    float3 contribution = glitterColor.rgb * sparkleColor;
    contribution *= lerp(1.0, s.emissionAlbedo, _GlitterMainStrength);
    contribution *= lerp(1.0, l.lightColor, _GlitterEnableLighting);
    contribution *= glitterColor.a * lerp(1.0, l.shadowmix, _GlitterShadowMask);
    if (s.facing < (_GlitterBackfaceMask - 1.0)) contribution = 0.0;
    #if defined(_SURFACE_TYPE_TRANSPARENT)
        contribution *= FairyToonGlitterTransparency(s.alpha, _GlitterApplyTransparency);
        // HDR sparkle times inverse alpha must fit native half when this function returns.
        return clamp((float3)col + contribution, -65504.0, 65504.0);
    #else
        return col + contribution;
    #endif
}

#endif
#endif
