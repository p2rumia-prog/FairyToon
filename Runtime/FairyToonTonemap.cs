using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace NW.FairyToon
{
    public enum FairyToonTonemapMode { None, Filmic }

    /// <summary>Built-in look LUTs, photo-filter style, generated from a few grading steps (no image source).</summary>
    public enum FairyToonLookPreset { None, Warm, Cool, Soft, Vivid }

    [Serializable]
    public sealed class FairyToonTonemapModeParameter : VolumeParameter<FairyToonTonemapMode>
    {
        public FairyToonTonemapModeParameter(FairyToonTonemapMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class FairyToonLookPresetParameter : VolumeParameter<FairyToonLookPreset>
    {
        public FairyToonLookPresetParameter(FairyToonLookPreset value, bool overrideState = false) : base(value, overrideState) { }
    }

    /// <summary>
    /// FairyToon's filmic tonemap, after URP's bloom, vignette and colour grading. Needs URP Tonemapping None and the
    /// pipeline's Grading Mode set to High Dynamic Range; otherwise URP has already mapped the image and the pass skips.
    /// </summary>
    [Serializable, VolumeComponentMenu("Post-processing/FairyToon Tonemap")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class FairyToonTonemap : VolumeComponent, IPostProcessComponent
    {
        /// <summary>The scene value the Mid Grey setting maps.</summary>
        public const float SceneMidGrey = 0.18f;

        [Tooltip("Filmic maps the HDR image with the curve below.")]
        public FairyToonTonemapModeParameter mode = new FairyToonTonemapModeParameter(FairyToonTonemapMode.None);
        [Tooltip("Toe contrast. Above 1 deepens the darks and steepens the mid tones.")]
        public ClampedFloatParameter contrast = new ClampedFloatParameter(1.25f, 0.8f, 1.6f);
        [Tooltip("Shoulder. Lower values roll the highlights off sooner and softer.")]
        public ClampedFloatParameter shoulder = new ClampedFloatParameter(0.96f, 0.9f, 1f);
        [Tooltip("Scene value that maps to white.")]
        public ClampedFloatParameter whitePoint = new ClampedFloatParameter(5f, 2f, 32f);
        [Tooltip("Display value (linear) for scene mid grey 0.18. Raises or lowers the whole image like exposure, but keeps white at the white point.")]
        public ClampedFloatParameter midGrey = new ClampedFloatParameter(0.22f, 0.1f, 0.4f);
        [Tooltip("1 maps the brightest channel and keeps each colour's channel ratios, so skin and hair keep their hue and saturation; 0 maps every channel on its own.")]
        public ClampedFloatParameter huePreservation = new ClampedFloatParameter(1f, 0f, 1f);
        [Tooltip("How far saturated colours roll towards white in the top 30 % of the curve (hue-kept part only).")]
        public ClampedFloatParameter highlightDesaturation = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Built-in look after the curve: Warm (golden light), Cool (clear daylight), Soft (lifted, pastel), Vivid " +
            "(more colour and contrast). A LUT in the slot below takes precedence.")]
        public FairyToonLookPresetParameter lookPreset = new FairyToonLookPresetParameter(FairyToonLookPreset.None);
        [Tooltip("Optional look LUT after the curve, in URP's strip layout (size² × size), looked up in sRGB.")]
        public Texture2DParameter lut = new Texture2DParameter(null);
        [Tooltip("Blend of the look (preset or LUT).")]
        public ClampedFloatParameter lutContribution = new ClampedFloatParameter(1f, 0f, 1f);
        [Tooltip("Sharpening after the curve: a light unsharp mask, clamped to the neighbouring pixels so edges get no halo. 0 = off.")]
        public ClampedFloatParameter sharpen = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Triangular dither amplitude in 8-bit display steps; hides banding in soft gradients.")]
        public ClampedFloatParameter dither = new ClampedFloatParameter(0.5f, 0f, 2f);

        public bool IsActive() => mode.value == FairyToonTonemapMode.Filmic;

        /// <summary>Solves the curve <c>x^a / (x^(a·d)·b + c)</c> so that mid grey and the white point land where set.</summary>
        public Vector4 CurveCoefficients()
        {
            double a = contrast.value, d = shoulder.value, white = whitePoint.value;
            double midIn = SceneMidGrey, midOut = midGrey.value;
            double wa = Math.Pow(white, a), wad = Math.Pow(white, a * d);
            double ma = Math.Pow(midIn, a), mad = Math.Pow(midIn, a * d);
            double denominator = (wad - mad) * midOut;
            double b = (wa * midOut - ma) / denominator;
            // (positive for every value the sliders allow, which keeps the curve rising)
            double c = (wad * ma - wa * mad * midOut) / denominator;
            return new Vector4((float)a, (float)(a * d), (float)b, (float)Math.Max(c, 1e-5));
        }

        /// <summary>The look LUT to use: the LUT slot, else the preset's generated strip, else null.</summary>
        public Texture ActiveLut() => lut.value != null ? lut.value
            : lookPreset.value != FairyToonLookPreset.None ? FairyToonLooks.Get(lookPreset.value) : null;
    }

    /// <summary>Generates the built-in look LUTs: 32³ strips (1024 × 32) holding sRGB out for sRGB in, made once per preset.</summary>
    public static class FairyToonLooks
    {
        private const int Size = 32;
        private static readonly Texture2D[] cache = new Texture2D[Enum.GetValues(typeof(FairyToonLookPreset)).Length];

        public static Texture2D Get(FairyToonLookPreset preset)
        {
            int index = (int)preset;
            if (cache[index] != null) return cache[index];
            var texture = new Texture2D(Size * Size, Size, TextureFormat.RGBA32, false, true)
            {
                name = "FairyToon Look " + preset,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[Size * Size * Size];
            for (int b = 0; b < Size; b++)
            for (int g = 0; g < Size; g++)
            for (int r = 0; r < Size; r++)
            {
                Vector3 c = Grade(preset, new Vector3(r, g, b) / (Size - 1));
                pixels[g * Size * Size + b * Size + r] = new Color32(Byte(c.x), Byte(c.y), Byte(c.z), 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return cache[index] = texture;
        }

        private static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        // Grading on display (sRGB) values: white balance as channel gains, lift / gain, saturation around Rec. 709 luma and
        // a gentle S-curve. Mild on purpose: the Endfield references use post sparingly.
        private static Vector3 Grade(FairyToonLookPreset preset, Vector3 c)
        {
            switch (preset)
            {
                case FairyToonLookPreset.Warm:
                    c = Vector3.Scale(c, new Vector3(1.04f, 1f, 0.9f)) + new Vector3(0.015f, 0.008f, 0f);
                    return Saturation(c, 1.05f);
                case FairyToonLookPreset.Cool:
                    c = Vector3.Scale(c, new Vector3(0.95f, 0.99f, 1.05f));
                    return Contrast(Saturation(c, 0.97f), 0.08f);
                case FairyToonLookPreset.Soft:
                    c = c * 0.9f + new Vector3(0.06f, 0.055f, 0.06f);
                    return Saturation(c, 0.88f);
                case FairyToonLookPreset.Vivid:
                    return Contrast(Saturation(c, 1.18f), 0.12f);
                default:
                    return c;
            }
        }

        private static Vector3 Saturation(Vector3 c, float amount)
        {
            float luma = Vector3.Dot(c, new Vector3(0.2126f, 0.7152f, 0.0722f));
            return new Vector3(luma, luma, luma) + (c - new Vector3(luma, luma, luma)) * amount;
        }

        // (smoothstep-shaped S-curve around 0.5, mixed in by `amount`)
        private static Vector3 Contrast(Vector3 c, float amount) =>
            Vector3.Lerp(c, new Vector3(Smooth(c.x), Smooth(c.y), Smooth(c.z)), amount);

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }

    internal sealed class FairyToonTonemapPass : ScriptableRenderPass
    {
        private static readonly int CurveId = Shader.PropertyToID("_FT_TonemapCurve");
        private static readonly int ParamsId = Shader.PropertyToID("_FT_TonemapParams");
        private static readonly int LutId = Shader.PropertyToID("_FT_TonemapLut");
        private static readonly int LutParamsId = Shader.PropertyToID("_FT_TonemapLutParams");
        private static readonly int SharpenId = Shader.PropertyToID("_FT_TonemapSharpen");
        private static bool warned;
        private readonly FairyToonFeature owner;

        public FairyToonTonemapPass(FairyToonFeature owner)
        {
            this.owner = owner;
            profilingSampler = new ProfilingSampler("FairyToon Tonemap");
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        /// <summary>True when the volume asks for the curve and URP hands this pass an unmapped HDR image.</summary>
        internal static bool ShouldRun(ref CameraData cameraData)
        {
            var tonemap = VolumeManager.instance.stack.GetComponent<FairyToonTonemap>();
            if (tonemap == null || !tonemap.IsActive()) return false;
            // (the last camera of a stack maps the result; HDR displays need URP's own output transform)
            if (!cameraData.postProcessEnabled || !cameraData.isHdrEnabled || !cameraData.resolveFinalTarget
                || cameraData.isHDROutputActive) return false;
            var urpTonemap = VolumeManager.instance.stack.GetComponent<Tonemapping>();
            var pipeline = UniversalRenderPipeline.asset;
            if ((urpTonemap != null && urpTonemap.IsActive())
                || (pipeline != null && pipeline.colorGradingMode != ColorGradingMode.HighDynamicRange))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning("FairyToon Tonemap skipped: set URP Tonemapping to None and the pipeline's Grading Mode to High Dynamic Range.");
                }
                return false;
            }
            return true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var material = owner.TonemapMaterial;
            var tonemap = VolumeManager.instance.stack.GetComponent<FairyToonTonemap>();
            var resources = frameData.Get<UniversalResourceData>();
            if (material == null || tonemap == null || resources.isActiveTargetBackBuffer) return;

            var source = resources.activeColorTexture;
            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "_FairyToonTonemapped";
            // (an 8-bit sRGB target like the back buffer, so the dither lands on real display steps)
            desc.format = GraphicsFormat.R8G8B8A8_SRGB;
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            var destination = renderGraph.CreateTexture(desc);

            var lut = tonemap.ActiveLut();
            float lutBlend = lut != null ? tonemap.lutContribution.value : 0f;
            material.SetVector(CurveId, tonemap.CurveCoefficients());
            material.SetVector(ParamsId, new Vector4(tonemap.huePreservation.value, tonemap.highlightDesaturation.value,
                tonemap.dither.value / 255f, lutBlend));
            material.SetVector(SharpenId, new Vector4(tonemap.sharpen.value, 1f / Mathf.Max(desc.width, 1), 1f / Mathf.Max(desc.height, 1), 0f));
            if (lut != null)
            {
                material.SetTexture(LutId, lut);
                material.SetVector(LutParamsId, new Vector4(1f / lut.width, 1f / lut.height, lut.height - 1f, 0f));
            }

            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0), passName);
            resources.cameraColor = destination;
        }
    }
}
