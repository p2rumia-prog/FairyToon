// Adapted from lilToon 2.4.1 (MIT, Copyright (c) 2020-2024 lilxyzw).
// Copy / paste / reset follows lilGUIUtility.cs at
// https://github.com/lilxyzw/lilToon/blob/e698a2f791bd28dd1aabf227c74f14bedeca3587/Assets/lilToon/Editor/lilInspector/lilGUIUtility.cs
// Property membership is FairyToon's own section sets. See THIRD_PARTY_NOTICES.md.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NW.FairyToon.Editor
{
    internal enum FairyToonBlock
    {
        None,
        Base,
        Lighting,
        Main,
        Main2nd,
        Main3rd,
        Iris,
        IrisAvatar,
        Shadow,
        EndfieldSurface,
        EndfieldDiffuse,
        EndfieldHair,
        EndfieldSpecular,
        EndfieldRim,
        Sheer,
        Wetness,
        RimShade,
        Emission,
        Emission2nd,
        Normal,
        Normal2nd,
        Anisotropy,
        Backlight,
        Reflection,
        MatCap,
        MatCap2nd,
        Rim,
        Glitter,
        Gem,
        Outline,
        Dissolve,
        Refraction,
        Fur,
        SeeThrough,
        Stencil,
        Rendering
    }

    /// <summary>Session clipboard for one inspector section. Copy reads the first selected material.
    /// Paste and Reset write every selected material. A lilToon inspector does not share this clipboard.</summary>
    internal static class FairyToonClipboard
    {
        private enum Kind
        {
            Number,
            Color,
            Vector,
            Texture
        }

        private struct Copied
        {
            public Kind Kind;
            public float Number;
            public Color Color;
            public Vector4 Vector;
            public Texture Texture;
            public Vector2 Scale;
            public Vector2 Offset;
            public bool HasScale;
        }

        private static readonly Dictionary<string, Copied> s_Clipboard = new Dictionary<string, Copied>();

        private static readonly Dictionary<FairyToonBlock, string> s_ManualAnchors = new Dictionary<FairyToonBlock, string>
        {
            { FairyToonBlock.Base, "base/base" },
            { FairyToonBlock.Lighting, "base/lighting" },
            { FairyToonBlock.Main, "color/maincolor" },
            { FairyToonBlock.Main2nd, "color/maincolor_layer" },
            { FairyToonBlock.Main3rd, "color/maincolor_layer" },
            { FairyToonBlock.Shadow, "color/shadow" },
            { FairyToonBlock.RimShade, "color/rimshade" },
            { FairyToonBlock.Emission, "color/emission" },
            { FairyToonBlock.Emission2nd, "color/emission" },
            { FairyToonBlock.Normal, "reflections/normal" },
            { FairyToonBlock.Normal2nd, "reflections/normal" },
            { FairyToonBlock.Anisotropy, "reflections/anisotropy" },
            { FairyToonBlock.Backlight, "reflections/backlight" },
            { FairyToonBlock.Reflection, "reflections/reflection" },
            { FairyToonBlock.MatCap, "reflections/matcap" },
            { FairyToonBlock.MatCap2nd, "reflections/matcap" },
            { FairyToonBlock.Rim, "reflections/rimlight" },
            { FairyToonBlock.Glitter, "reflections/glitter" },
            { FairyToonBlock.Gem, "reflections/gem" },
            { FairyToonBlock.Outline, "advanced/outline" },
            { FairyToonBlock.Dissolve, "advanced/dissolve" },
            { FairyToonBlock.Refraction, "advanced/refraction" },
            { FairyToonBlock.Fur, "advanced/fur" },
            { FairyToonBlock.Stencil, "advanced/stencil" },
            { FairyToonBlock.Rendering, "advanced/rendering" }
        };

        private static readonly HashSet<string> s_Base = new HashSet<string>
        {
            "_ShadingModel", "_TransparentMode", "_Cull", "_Cutoff", "_AlphaToMask", "_ZWrite",
            "_FlipNormal", "_BackfaceColor", "_ZTest", "_OffsetFactor", "_OffsetUnits", "_AAStrength"
        };

        private static readonly HashSet<string> s_Lighting = new HashSet<string>
        {
            "_LightMinLimit", "_LightMaxLimit", "_MonochromeLighting", "_AsUnlit"
        };

        private static readonly HashSet<string> s_Main = new HashSet<string>
        {
            "_MainTex", "_Color", "_MainTexHSVG", "_MainColorAdjustMask",
            "_AlphaMaskMode", "_AlphaMask", "_AlphaMaskScale", "_AlphaMaskValue"
        };

        private static readonly HashSet<string> s_IrisAvatar = new HashSet<string>
        {
            "_EFIrisFrame", "_EFIrisWorldRadius", "_EFIrisDish", "_EFIrisCenterL", "_EFIrisCenterR", "_EFIrisRadius",
            "_EFPupil", "_EFPupilCenterL", "_EFPupilCenterR", "_EFPupilRadius", "_EFPupilYRatio",
            "_EFPupilScale", "_EFPupilOffset"
        };

        private static readonly HashSet<string> s_Shadow = new HashSet<string>
        {
            "_UseShadow", "_ShadowMaskType", "_ShadowStrengthMask", "_ShadowStrength", "_ShadowFlatBorder", "_ShadowFlatBlur",
            "_UseShadowRamp", "_ShadowRampTex", "_ShadowColorTex", "_ShadowColor", "_ShadowBorder", "_ShadowBlur",
            "_ShadowNormalStrength", "_ShadowReceive", "_CharShadowReceive",
            "_Shadow2ndColorTex", "_Shadow2ndColor", "_Shadow2ndBorder", "_Shadow2ndBlur", "_Shadow2ndNormalStrength", "_Shadow2ndReceive",
            "_Shadow3rdColorTex", "_Shadow3rdColor", "_Shadow3rdBorder", "_Shadow3rdBlur", "_Shadow3rdNormalStrength", "_Shadow3rdReceive",
            "_ShadowMainStrength", "_ShadowEnvStrength", "_ShadowBorderColor", "_ShadowBorderRange", "_ShadowBlurMask",
            "_BackfaceForceShadow", "_ShadowBorderMask", "_ShadowAOShift", "_ShadowAOShift2", "_ShadowPostAO",
            "_SSAOStrength", "_SSAOLitStrength", "_SSAOMask"
        };

        private static readonly HashSet<string> s_Reflection = new HashSet<string>
        {
            "_UseReflection", "_SmoothnessTex", "_Smoothness", "_MetallicGlossMap", "_Metallic", "_Reflectance", "_GSAAStrength",
            "_ApplySpecular", "_SpecularToon", "_SpecularBorder", "_SpecularBlur", "_SpecularNormalStrength",
            "_ApplyReflection", "_ReflectionNormalStrength", "_ReflectionCubeTex", "_ReflectionCubeColor",
            "_ReflectionCubeEnableLighting", "_ReflectionCubeOverride", "_ReflectionColorTex", "_ReflectionColor",
            "_ReflectionBlendMode", "_ReflectionApplyTransparency"
        };

        private static readonly HashSet<string> s_Gem = new HashSet<string>
        {
            "_RefractionStrength", "_RefractionFresnelPower", "_GemChromaticAberration", "_GemEnvContrast", "_GemEnvColor",
            "_GemParticleLoop", "_GemParticleColor", "_GemVRParallaxStrength", "_SmoothnessTex", "_Smoothness",
            "_Reflectance", "_ReflectionCubeOverride", "_ReflectionCubeTex"
        };

        private static readonly HashSet<string> s_Refraction = new HashSet<string>
        {
            "_RefractionStrength", "_RefractionFresnelPower", "_RefractionColorFromMain", "_RefractionColor",
            "_RefractionBlur", "_SmoothnessTex", "_Smoothness"
        };

        private static readonly HashSet<string> s_Rendering = new HashSet<string>
        {
            "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha", "_ZWrite"
        };

        private static readonly HashSet<string> s_EndfieldSurface = new HashSet<string>
        {
            "_EFPart", "_EFEyeLight", "_EFEyeOverlay", "_EFEyeShadowColor", "_EFEyeScatterColor",
            "_SkinLUT", "_EFSSSColor", "_EFSSSStrength", "_ShadowStrengthMask", "_EFFaceSDFScale",
            "_RimColorTex", "_EFEyeMask", "_EFFaceHighlight", "_EFFaceHighlightColor", "_EFFaceHighlightShift",
            "_PBRMap", "_EFMetallic", "_EFSmoothness", "_EFReflectivity", "_EFOcclusion"
        };

        private static readonly HashSet<string> s_EndfieldDiffuse = new HashSet<string>
        {
            "_DiffuseRamp", "_ShadowBorder", "_ShadowBlur", "_EFShadowDarkness", "_EFShadowDarker", "_EFShadowSaturation",
            "_EFBacklight", "_CharShadowReceive", "_ShadowReceive", "_BackfaceForceShadow",
            "_SSAOStrength", "_SSAOLitStrength", "_SSAOMask"
        };

        private static readonly HashSet<string> s_EndfieldSpecular = new HashSet<string>
        {
            "_EFSpecular", "_SpecRamp", "_EFSpecRampFresnel", "_EFEnvironment", "_GSAAStrength",
            "_ReflectionCubeTex", "_ReflectionCubeOverride"
        };

        public static void ShowMenu(FairyToonBlock block, MaterialEditor editor)
        {
            if (block == FairyToonBlock.None || editor == null) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sCopy")), false, () => Copy(block, editor));
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sPaste")), false, () => Paste(block, editor, false));
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sPasteWithTexture")), false, () => Paste(block, editor, true));
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sReset")), false, () => Reset(block, editor));
            if (s_ManualAnchors.TryGetValue(block, out string anchor))
            {
                string url = FairyToonL10n.Text("sManualURL") + anchor;
                menu.AddItem(new GUIContent(FairyToonL10n.Text("sOpenManual")), false, () => Application.OpenURL(url));
            }
            menu.ShowAsContext();
        }

        private static void Copy(FairyToonBlock block, MaterialEditor editor)
        {
            var material = editor.target as Material;
            if (material == null || material.shader == null) return;
            Shader shader = material.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                if (!Belongs(block, name) || !material.HasProperty(name)) continue;
                Store(material, shader, i, name);
            }
        }

        private static void Paste(FairyToonBlock block, MaterialEditor editor, bool withTextures)
        {
            string undo = withTextures ? "FairyToon Paste (with textures)" : "FairyToon Paste";
            int group = BeginUndo(editor, undo);
            MaterialProperty[] props = MaterialEditor.GetMaterialProperties(editor.targets);
            for (int i = 0; i < props.Length; i++)
            {
                MaterialProperty prop = props[i];
                if (prop == null || !Belongs(block, prop.name)) continue;
                if (!s_Clipboard.TryGetValue(prop.name, out Copied copied)) continue;
                if (copied.Kind == Kind.Texture && !withTextures) continue;
                Apply(prop, copied, withTextures);
            }
            ApplyMaterials(editor);
            Undo.CollapseUndoOperations(group);
        }

        private static void Reset(FairyToonBlock block, MaterialEditor editor)
        {
            int group = BeginUndo(editor, "FairyToon Reset");
            MaterialProperty[] props = MaterialEditor.GetMaterialProperties(editor.targets);
            for (int i = 0; i < props.Length; i++)
            {
                MaterialProperty prop = props[i];
                if (prop == null || !Belongs(block, prop.name) || prop.targets == null || prop.targets.Length == 0) continue;
                if (!(prop.targets[0] is Material material) || material.shader == null) continue;
                Shader shader = material.shader;
                int index = shader.FindPropertyIndex(prop.name);
                if (index < 0) continue;
                switch (prop.propertyType)
                {
                    case ShaderPropertyType.Color:
                        Vector4 color = shader.GetPropertyDefaultVectorValue(index);
                        prop.colorValue = new Color(color.x, color.y, color.z, color.w);
                        break;
                    case ShaderPropertyType.Vector:
                        prop.vectorValue = shader.GetPropertyDefaultVectorValue(index);
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        prop.floatValue = shader.GetPropertyDefaultFloatValue(index);
                        break;
                    case ShaderPropertyType.Int:
                        prop.intValue = shader.GetPropertyDefaultIntValue(index);
                        break;
                    case ShaderPropertyType.Texture:
                        prop.textureValue = null;
                        break;
                    default:
                        throw new InvalidOperationException("Unhandled shader property type " + prop.propertyType);
                }
            }
            ApplyMaterials(editor);
            Undo.CollapseUndoOperations(group);
        }

        private static int BeginUndo(MaterialEditor editor, string undo)
        {
            Undo.SetCurrentGroupName(undo);
            int group = Undo.GetCurrentGroup();
            Undo.RecordObjects(editor.targets, undo);
            return group;
        }

        private static void ApplyMaterials(MaterialEditor editor)
        {
            foreach (UnityEngine.Object target in editor.targets)
                FairyToonMaterialSetup.Apply(target as Material);
            editor.PropertiesChanged();
        }

        private static void Store(Material material, Shader shader, int index, string name)
        {
            ShaderPropertyType type = shader.GetPropertyType(index);
            var copied = new Copied();
            switch (type)
            {
                case ShaderPropertyType.Color:
                    copied.Kind = Kind.Color;
                    copied.Color = material.GetColor(name);
                    break;
                case ShaderPropertyType.Vector:
                    copied.Kind = Kind.Vector;
                    copied.Vector = material.GetVector(name);
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    copied.Kind = Kind.Number;
                    copied.Number = material.GetFloat(name);
                    break;
                case ShaderPropertyType.Int:
                    copied.Kind = Kind.Number;
                    copied.Number = material.GetInt(name);
                    break;
                case ShaderPropertyType.Texture:
                    copied.Kind = Kind.Texture;
                    copied.Texture = material.GetTexture(name);
                    copied.HasScale = (shader.GetPropertyFlags(index) & ShaderPropertyFlags.NoScaleOffset) == 0;
                    if (copied.HasScale)
                    {
                        copied.Scale = material.GetTextureScale(name);
                        copied.Offset = material.GetTextureOffset(name);
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unhandled shader property type " + type);
            }
            s_Clipboard[name] = copied;
        }

        private static void Apply(MaterialProperty prop, Copied copied, bool withTextures)
        {
            switch (prop.propertyType)
            {
                case ShaderPropertyType.Color:
                    if (copied.Kind == Kind.Color) prop.colorValue = copied.Color;
                    break;
                case ShaderPropertyType.Vector:
                    if (copied.Kind == Kind.Vector) prop.vectorValue = copied.Vector;
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    if (copied.Kind == Kind.Number) prop.floatValue = copied.Number;
                    break;
                case ShaderPropertyType.Int:
                    if (copied.Kind == Kind.Number) prop.intValue = Mathf.RoundToInt(copied.Number);
                    break;
                case ShaderPropertyType.Texture:
                    if (copied.Kind == Kind.Texture && withTextures)
                    {
                        prop.textureValue = copied.Texture;
                        if (copied.HasScale && (prop.propertyFlags & ShaderPropertyFlags.NoScaleOffset) == 0)
                            prop.textureScaleAndOffset = new Vector4(copied.Scale.x, copied.Scale.y, copied.Offset.x, copied.Offset.y);
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unhandled shader property type " + prop.propertyType);
            }
        }

        private static bool Belongs(FairyToonBlock block, string name)
        {
            switch (block)
            {
                case FairyToonBlock.None: return false;
                case FairyToonBlock.Base: return s_Base.Contains(name);
                case FairyToonBlock.Lighting: return s_Lighting.Contains(name);
                case FairyToonBlock.Main: return s_Main.Contains(name);
                case FairyToonBlock.Main2nd: return name.StartsWith("_Main2nd", StringComparison.Ordinal)
                    || name.StartsWith("_Color2nd", StringComparison.Ordinal)
                    || name.StartsWith("_FT_Main2nd", StringComparison.Ordinal)
                    || name == "_UseMain2ndTex";
                case FairyToonBlock.Main3rd: return name.StartsWith("_Main3rd", StringComparison.Ordinal)
                    || name.StartsWith("_Color3rd", StringComparison.Ordinal)
                    || name.StartsWith("_FT_Main3rd", StringComparison.Ordinal)
                    || name == "_UseMain3rdTex";
                case FairyToonBlock.Iris: return IsIris(name);
                case FairyToonBlock.IrisAvatar: return s_IrisAvatar.Contains(name);
                case FairyToonBlock.Shadow: return s_Shadow.Contains(name);
                case FairyToonBlock.EndfieldSurface: return s_EndfieldSurface.Contains(name) || IsIris(name);
                case FairyToonBlock.EndfieldDiffuse: return s_EndfieldDiffuse.Contains(name);
                case FairyToonBlock.EndfieldHair: return name.StartsWith("_EFHair", StringComparison.Ordinal)
                    || name == "_HairMask" || name == "_SpecRamp" || name == "_SkinLUT";
                case FairyToonBlock.EndfieldSpecular: return s_EndfieldSpecular.Contains(name);
                case FairyToonBlock.EndfieldRim: return name.StartsWith("_EFRim", StringComparison.Ordinal) || name == "_EFLightRim";
                case FairyToonBlock.Sheer: return name.StartsWith("_Sheer", StringComparison.Ordinal);
                case FairyToonBlock.Wetness: return name == "_UseWet" || name.StartsWith("_Wet", StringComparison.Ordinal);
                case FairyToonBlock.RimShade: return name == "_UseRimShade" || name.StartsWith("_RimShade", StringComparison.Ordinal);
                case FairyToonBlock.Emission: return name == "_UseEmission"
                    || (name.StartsWith("_Emission", StringComparison.Ordinal) && !name.StartsWith("_Emission2nd", StringComparison.Ordinal));
                case FairyToonBlock.Emission2nd: return name == "_UseEmission2nd" || name.StartsWith("_Emission2nd", StringComparison.Ordinal);
                case FairyToonBlock.Normal: return name == "_UseBumpMap" || name == "_BumpMap" || name == "_BumpScale";
                case FairyToonBlock.Normal2nd: return name == "_UseBump2ndMap" || name.StartsWith("_Bump2nd", StringComparison.Ordinal);
                case FairyToonBlock.Anisotropy: return name == "_UseAnisotropy" || name.StartsWith("_Anisotropy", StringComparison.Ordinal);
                case FairyToonBlock.Backlight: return name == "_UseBacklight" || name.StartsWith("_Backlight", StringComparison.Ordinal);
                case FairyToonBlock.Reflection: return s_Reflection.Contains(name);
                case FairyToonBlock.MatCap2nd: return name == "_UseMatCap2nd" || name.StartsWith("_MatCap2nd", StringComparison.Ordinal);
                case FairyToonBlock.MatCap: return name == "_UseMatCap"
                    || (name.StartsWith("_MatCap", StringComparison.Ordinal) && !name.StartsWith("_MatCap2nd", StringComparison.Ordinal));
                case FairyToonBlock.Rim: return (name == "_UseRim" || name.StartsWith("_Rim", StringComparison.Ordinal))
                    && name != "_UseRimShade" && !name.StartsWith("_RimShade", StringComparison.Ordinal);
                case FairyToonBlock.Glitter: return name == "_UseGlitter" || name.StartsWith("_Glitter", StringComparison.Ordinal);
                case FairyToonBlock.Gem: return s_Gem.Contains(name) || name.StartsWith("_Gem", StringComparison.Ordinal);
                case FairyToonBlock.Outline: return name == "_UseOutline"
                    || name.StartsWith("_Outline", StringComparison.Ordinal)
                    || name.StartsWith("_EFOutline", StringComparison.Ordinal);
                case FairyToonBlock.Dissolve: return name.StartsWith("_Dissolve", StringComparison.Ordinal)
                    || name.StartsWith("_FT_Dissolve", StringComparison.Ordinal);
                case FairyToonBlock.Refraction: return s_Refraction.Contains(name);
                case FairyToonBlock.Fur: return name.StartsWith("_Fur", StringComparison.Ordinal) || name == "_VertexColor2FurVector";
                case FairyToonBlock.SeeThrough: return name.StartsWith("_SeeThrough", StringComparison.Ordinal)
                    || name.StartsWith("_FakeShadow", StringComparison.Ordinal)
                    || name == "_UseFakeShadow";
                case FairyToonBlock.Stencil: return name.StartsWith("_Stencil", StringComparison.Ordinal);
                case FairyToonBlock.Rendering: return s_Rendering.Contains(name);
                default:
                    throw new InvalidOperationException("Unhandled FairyToon block " + block);
            }
        }

        private static bool IsIris(string name)
        {
            return name.StartsWith("_EFIris", StringComparison.Ordinal)
                || name.StartsWith("_EFPupil", StringComparison.Ordinal)
                || name.StartsWith("_EFCornea", StringComparison.Ordinal);
        }
    }
}
