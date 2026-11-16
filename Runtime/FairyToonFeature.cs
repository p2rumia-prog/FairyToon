using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace NW.FairyToon
{
    /// <summary>Draws FairyToon's character shadows, fringe shadow, see-through hair and tonemap in URP's Render Graph.</summary>
    [DisallowMultipleRendererFeature("FairyToon")]
    public sealed class FairyToonFeature : ScriptableRendererFeature
    {
        public bool seeThroughHair = true;
        public bool fakeShadow = true;
        [Tooltip("Ask URP for the camera depth texture before opaques (a depth prepass if nothing else makes one), so " +
                 "Endfield materials with Depth Edge Rim can keep their rim to the true outline. Off: they use the N.V rim.")]
        public bool depthRim = true;
        [Tooltip("With Depth Rim: also draw the FairyToon characters alone into a depth texture (_FT_CharacterDepth) " +
                 "before opaques, and measure the rim against it, so the whole silhouette keeps its rim (feet on the " +
                 "floor, a shoulder at a wall) and props in front of a character do not cut one into it.")]
        public bool characterMask = true;
        public LayerMask layerMask = ~0;

        [Tooltip("Character self-shadow atlas, projector, filtering and bias settings.")]
        public FairyToonCharacterShadowSettings characterShadow = new FairyToonCharacterShadowSettings();
        [SerializeField, HideInInspector] private Shader characterShadowCasterShader;
        [SerializeField, HideInInspector] private Shader tonemapShader;
        private Material characterShadowFallback;
        private Material tonemapMaterial;
        private FairyToonCharacterShadow characterShadowPass;
        private FairyToonCharacterShadow.ResetPass characterShadowResetPass;
        private FairyToonTonemapPass tonemapPass;

        internal Material TonemapMaterial
        {
            get
            {
                if (tonemapShader == null)
                    tonemapShader = Shader.Find("Hidden/FairyToon/Tonemap");
                if (tonemapMaterial == null && tonemapShader != null)
                    tonemapMaterial = CoreUtils.CreateEngineMaterial(tonemapShader);
                return tonemapMaterial;
            }
        }

        internal Material CharacterShadowFallback
        {
            get
            {
                if (characterShadowCasterShader == null)
                    characterShadowCasterShader = Shader.Find("Hidden/FairyToon/CharacterShadowCaster");
                if (characterShadowFallback == null && characterShadowCasterShader != null)
                    characterShadowFallback = CoreUtils.CreateEngineMaterial(characterShadowCasterShader);
                return characterShadowFallback;
            }
        }

        public bool TryGetCharacterShadowDiagnostic(FairyToonCharacter character, out FairyToonCharacterShadowDiagnostic diagnostic)
        {
            diagnostic = default;
            return isActive && characterShadow.enabled && characterShadowPass != null
                && characterShadowPass.TryGetDiagnostic(character, out diagnostic);
        }

        private FringePass shadowPass;
        private FringePass earlyZPass;
        private FringePass hairPass;
        private DepthRimPass depthRimPass;
        private DepthRimPass depthRimResetPass;
        private CharacterMaskPass characterMaskPass;

        public override void Create()
        {
            characterShadow ??= new FairyToonCharacterShadowSettings();
            characterShadowPass?.Dispose();
            CoreUtils.Destroy(characterShadowFallback);
            characterShadowFallback = null;
            if (characterShadowCasterShader == null)
                characterShadowCasterShader = Shader.Find("Hidden/FairyToon/CharacterShadowCaster");
            CoreUtils.Destroy(tonemapMaterial);
            tonemapMaterial = null;
            if (tonemapShader == null)
                tonemapShader = Shader.Find("Hidden/FairyToon/Tonemap");
            characterShadowPass = new FairyToonCharacterShadow(this);
            characterShadowResetPass = new FairyToonCharacterShadow.ResetPass();
            tonemapPass = new FairyToonTonemapPass(this);
            shadowPass = new FringePass("FairyToon Fake Shadow", FairyToon.FakeShadowLightMode,
                RenderPassEvent.AfterRenderingOpaques, RenderQueueRange.opaque, SortingCriteria.CommonOpaque);
            earlyZPass = new FringePass("FairyToon See-through Early Z", FairyToon.SeeThroughEarlyZLightMode,
                RenderPassEvent.AfterRenderingTransparents, RenderQueueRange.all, SortingCriteria.CommonOpaque);
            hairPass = new FringePass("FairyToon See-through Hair", FairyToon.SeeThroughHairLightMode,
                RenderPassEvent.AfterRenderingTransparents, RenderQueueRange.all, SortingCriteria.CommonTransparent);
            depthRimPass = new DepthRimPass(true);
            depthRimResetPass = new DepthRimPass(false);
            characterMaskPass = new CharacterMaskPass();
        }

        protected override void Dispose(bool disposing)
        {
            characterShadowPass?.Dispose();
            CoreUtils.Destroy(characterShadowFallback);
            characterShadowFallback = null;
            CoreUtils.Destroy(tonemapMaterial);
            tonemapMaterial = null;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // (match Render Objects: preview and depth-only target cameras do not run these colour passes)
            if (renderingData.cameraData.cameraType == CameraType.Preview
                || renderingData.cameraData.cameraType == CameraType.Reflection
                || UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData)) return;

            renderer.EnqueuePass(characterShadowPass);
            renderer.EnqueuePass(characterShadowResetPass);

            if (depthRim)
            {
                depthRimPass.CharacterMask = characterMask;
                renderer.EnqueuePass(depthRimPass);
                if (characterMask)
                {
                    characterMaskPass.LayerMask = layerMask;
                    renderer.EnqueuePass(characterMaskPass);
                }
                renderer.EnqueuePass(depthRimResetPass);
            }

            if (fakeShadow)
            {
                shadowPass.LayerMask = layerMask;
                renderer.EnqueuePass(shadowPass);
            }
            if (seeThroughHair)
            {
                earlyZPass.LayerMask = layerMask;
                hairPass.LayerMask = layerMask;
                // (separate renderer lists: every front layer writes depth before any hair blends)
                renderer.EnqueuePass(earlyZPass);
                renderer.EnqueuePass(hairPass);
            }
            // (only when active: any pass after post makes URP keep an intermediate target)
            if (FairyToonTonemapPass.ShouldRun(ref renderingData.cameraData))
                renderer.EnqueuePass(tonemapPass);
        }

        /// <summary>Before opaques: requests the camera depth texture (an input before opaques makes URP render a depth
        /// prepass when nothing else does) and sets _FT_DepthRim for the depth edge rim. After rendering: clears it, so a
        /// camera without this pass falls back to the N.V rim.</summary>
        private sealed class DepthRimPass : ScriptableRenderPass
        {
            private static readonly int DepthRimId = Shader.PropertyToID("_FT_DepthRim");
            private readonly bool set;
            public bool CharacterMask { private get; set; }

            private sealed class PassData
            {
                public Vector4 value;
            }

            public DepthRimPass(bool set)
            {
                this.set = set;
                profilingSampler = new ProfilingSampler(set ? "FairyToon Depth Rim" : "FairyToon Depth Rim Reset");
                renderPassEvent = set ? RenderPassEvent.BeforeRenderingOpaques : RenderPassEvent.AfterRendering;
                if (set) ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var data, profilingSampler))
                {
                    data.value = set ? new Vector4(1f, CharacterMask ? 1f : 0f, 0f, 0f) : Vector4.zero;
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalVector(DepthRimId, pass.value);
                    });
                }
            }
        }

        /// <summary>Before opaques: draws the FairyToon characters (their CharacterMask pass, opaque queues, the feature
        /// layer mask) alone into a camera-sized depth texture and binds it as _FT_CharacterDepth, in the camera depth's
        /// encoding (cleared to the far plane where there is no character). The depth edge rim reads it.</summary>
        private sealed class CharacterMaskPass : ScriptableRenderPass
        {
            private static readonly ShaderTagId ShaderTag = new ShaderTagId(FairyToon.CharacterMaskLightMode);
            private static readonly int TextureId = Shader.PropertyToID("_FT_CharacterDepth");
            public int LayerMask { private get; set; }

            private sealed class PassData
            {
                public RendererListHandle rendererList;
            }

            public CharacterMaskPass()
            {
                profilingSampler = new ProfilingSampler("FairyToon Character Mask");
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();
                var drawing = RenderingUtils.CreateDrawingSettings(ShaderTag, renderingData, cameraData, lightData,
                    SortingCriteria.CommonOpaque);
                var filtering = new FilteringSettings(RenderQueueRange.opaque, LayerMask);
                var rendererList = new RendererListParams(renderingData.cullResults, drawing, filtering);

                // (the camera depth's size and format, single-sampled so shaders can read it as a plain texture)
                var desc = renderGraph.GetTextureDesc(resources.activeDepthTexture);
                desc.name = "_FT_CharacterDepth";
                desc.msaaSamples = MSAASamples.None;
                desc.bindTextureMS = false;
                desc.clearBuffer = false;
                desc.filterMode = FilterMode.Point;
                desc.wrapMode = TextureWrapMode.Clamp;
                var texture = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var data, profilingSampler))
                {
                    data.rendererList = renderGraph.CreateRendererList(rendererList);
                    builder.UseRendererList(data.rendererList);
                    builder.SetRenderAttachmentDepth(texture, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(texture, TextureId);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                    {
                        // (1 = far plane; Unity remaps it for reversed Z)
                        context.cmd.ClearRenderTarget(true, false, Color.black, 1f);
                        context.cmd.DrawRendererList(pass.rendererList);
                    });
                }
            }
        }

        private sealed class FringePass : ScriptableRenderPass
        {
            private readonly ShaderTagId shaderTag;
            private readonly RenderQueueRange queueRange;
            private readonly SortingCriteria sorting;
            public int LayerMask { private get; set; }

            private sealed class PassData
            {
                public RendererListHandle rendererList;
            }

            public FringePass(string name, string lightMode, RenderPassEvent evt,
                RenderQueueRange queueRange, SortingCriteria sorting)
            {
                profilingSampler = new ProfilingSampler(name);
                shaderTag = new ShaderTagId(lightMode);
                renderPassEvent = evt;
                this.queueRange = queueRange;
                this.sorting = sorting;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();
                var drawing = RenderingUtils.CreateDrawingSettings(shaderTag, renderingData, cameraData, lightData, sorting);
                var filtering = new FilteringSettings(queueRange, LayerMask);
                var rendererList = new RendererListParams(renderingData.cullResults, drawing, filtering);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var data, profilingSampler))
                {
                    data.rendererList = renderGraph.CreateRendererList(rendererList);
                    builder.UseRendererList(data.rendererList);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                    // (the lit hair reuses forward lighting, including global probe / cookie textures)
                    builder.UseAllGlobalTextures(true);
                    if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                    if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
                    if (resources.ssaoTexture.IsValid()) builder.UseTexture(resources.ssaoTexture, AccessFlags.Read);
                    builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(pass.rendererList);
                    });
                }
            }
        }
    }
}
