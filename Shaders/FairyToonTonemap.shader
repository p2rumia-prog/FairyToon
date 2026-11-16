// FairyToon Tonemap (FairyToonFeature runs it after URP's post; see FairyToonTonemap.cs). Filmic curve of the form
// x^a / (x^(a·d)·b + c) (after Timothy Lottes, "Advanced Techniques and Optimization of HDR Color Pipelines", GDC 2016),
// applied to the brightest channel with the channel ratios kept, then optional sharpening, an optional look LUT and a
// triangular dither.
Shader "Hidden/FairyToon/Tonemap"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "FairyToonTonemap"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _FT_TonemapCurve;     // x toe contrast a, y a · shoulder, z b, w c
            float4 _FT_TonemapParams;    // x hue preservation, y highlight desaturation, z dither (display units), w LUT blend
            float4 _FT_TonemapLutParams; // x 1 / width, y 1 / height, z height - 1
            float4 _FT_TonemapSharpen;   // x amount (0 off), yz texel size
            TEXTURE2D(_FT_TonemapLut);

            float3 FilmicCurve(float3 x)
            {
                x = max(x, 0.0);
                return pow(x, _FT_TonemapCurve.x) / (pow(x, _FT_TonemapCurve.y) * _FT_TonemapCurve.z + _FT_TonemapCurve.w);
            }

            // Hue-kept path: map the brightest channel and keep the channel ratios, so warm skin and tinted hair keep
            // their hue and saturation instead of drifting towards yellow and grey; saturated colours roll towards white
            // only near the top of the curve. The roll mixes square-rooted ratios: a linear mix lifts a near-zero channel
            // far more than it looks (12 % turns blue 28 into 96 on an amber iris). Returns display-linear 0..1.
            float3 Tonemap(float3 color)
            {
                color = max(color, 0.0);
                float peak = max(Max3(color.r, color.g, color.b), 1e-5);
                float mapped = FilmicCurve(peak.xxx).x;
                float toWhite = smoothstep(0.7, 1.0, mapped) * _FT_TonemapParams.y;
                float3 ratio = lerp(sqrt(color / peak), 1.0, toWhite);
                float3 hueKept = ratio * ratio * mapped;
                return saturate(lerp(FilmicCurve(color), hueKept, _FT_TonemapParams.x));
            }

            float3 TonemapAt(float2 uv)
            {
                return Tonemap(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb);
            }

            // Two independent uniform values per pixel, summed into a triangular distribution on [-1, 1].
            float TriangularNoise(float2 pixel)
            {
                float a = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
                float b = frac(sin(dot(pixel, float2(12.9898, 78.233))) * 43758.5453);
                return a + b - 1.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord.xy;
                float4 source = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
                float3 color = Tonemap(source.rgb);

                // Optional sharpening: an unsharp mask on the four neighbours, clamped to their range so an edge gets
                // no bright or dark halo.
                UNITY_BRANCH
                if (_FT_TonemapSharpen.x > 0.0)
                {
                    float2 texel = _FT_TonemapSharpen.yz;
                    float3 n = TonemapAt(uv + float2(0.0, texel.y));
                    float3 s = TonemapAt(uv - float2(0.0, texel.y));
                    float3 e = TonemapAt(uv + float2(texel.x, 0.0));
                    float3 w = TonemapAt(uv - float2(texel.x, 0.0));
                    float3 low = min(color, min(min(n, s), min(e, w)));
                    float3 high = max(color, max(max(n, s), max(e, w)));
                    float3 blur = (n + s + e + w) * 0.25;
                    color = clamp(color + (color - blur) * 1.5 * _FT_TonemapSharpen.x, low, high);
                }

                float3 display = LinearToSRGB(color);
                UNITY_BRANCH
                if (_FT_TonemapParams.w > 0.0)
                {
                    float3 graded = ApplyLut2D(TEXTURE2D_ARGS(_FT_TonemapLut, sampler_LinearClamp), display, _FT_TonemapLutParams.xyz);
                    display = lerp(display, graded, _FT_TonemapParams.w);
                }
                display += TriangularNoise(input.positionCS.xy) * _FT_TonemapParams.z;
                return half4(SRGBToLinear(saturate(display)), source.a);
            }
            ENDHLSL
        }
    }
}
