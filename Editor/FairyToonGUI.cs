using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NW.FairyToon.Editor
{
    // Equivalent to lilToon's gamma HDR picker: retain Color upload conversion and allow values above 1.
    // Native [HDR] changes the shader input interpretation, so the drawer carries no HDR property flag.
    public sealed class FairyToonHDRDrawer : MaterialPropertyDrawer
    {
        public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
        {
            bool mixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = prop.hasMixedValue;
            Color value = prop.colorValue;
            if (Draw(position, FairyToonL10n.Text(label), ref value)) prop.colorValue = value;
            EditorGUI.showMixedValue = mixed;
        }

        // Same layout as lilToon's lilHDRDrawer: a 50px colour field (eyedropper and HDR badge included)
        // and a 50px hex field beside it. An 18px rect is too narrow for the eyedropper.
        internal static bool Draw(Rect position, string label, ref Color value)
        {
            float xMax = position.xMax;
            bool labeled = !string.IsNullOrEmpty(label) && position.width > 120f;
            position.width = labeled ? EditorGUIUtility.labelWidth + 50f : Mathf.Min(50f, position.width);
            float intensity = value.maxColorComponent > 1f ? value.maxColorComponent : 1f;
            EditorGUI.BeginChangeCheck();
            Color picked = EditorGUI.ColorField(position, new GUIContent(labeled ? label : string.Empty), value, true, true, true);
            bool colorChanged = EditorGUI.EndChangeCheck();
            EditorGUI.BeginChangeCheck();
            var unit = new Color(picked.r / intensity, picked.g / intensity, picked.b / intensity, 1f);
            string hex = ColorUtility.ToHtmlStringRGB(unit);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            var hexRect = position;
            hexRect.x += position.width + 4f;
            hexRect.width = Mathf.Min(50f, xMax - hexRect.x);
            if (hexRect.width > 10f) hex = EditorGUI.TextField(hexRect, GUIContent.none, "#" + hex);
            EditorGUI.indentLevel = indent;
            bool hexChanged = EditorGUI.EndChangeCheck();
            if (hexChanged)
            {
                if (!ColorUtility.TryParseHtmlString(hex, out Color parsed)) return colorChanged && Assign(ref value, picked);
                picked.r = parsed.r * intensity;
                picked.g = parsed.g * intensity;
                picked.b = parsed.b * intensity;
                value = picked;
                return true;
            }
            if (!colorChanged) return false;
            value = picked;
            return true;
        }

        private static bool Assign(ref Color value, Color picked)
        {
            value = picked;
            return true;
        }
    }

    /// <summary>FairyToon inspector in lilToon 2.4.1's layout: Advanced, Preset, and Shader Setting.</summary>
    /// <remarks>Advanced shows every property. Preset is a placeholder until material presets land.
    /// Derived state (keywords, blend, queue, passes) is never edited here:
    /// <see cref="FairyToonMaterialSetup.Apply"/> runs after each change.
    /// Foldout header and the gear menu follow lilToon; see THIRD_PARTY_NOTICES.md.</remarks>
    public sealed class FairyToonGUI : ShaderGUI
    {
        private const string PrefsPrefix = "NW.FairyToon.GUI.";

        private static GUIStyle s_HeaderLabel;
        private static GUIStyle s_Box;
        private static GUIStyle s_Foldout;
        private static GUIStyle s_Gear;
        private bool m_AlphaMaskAdvanced;
        private int m_InnerDepth;
        private static bool s_ProSkin;
        private static string s_Search = "";

        private MaterialEditor m_Editor;
        private MaterialProperty[] m_Props;

        // One property write validates every target, then MaterialEditor validates again after OnGUI.
        // The result is recorded per material so the first target cannot consume the skip for the rest,
        // and a later inspector cannot inherit it. Search and foldouts do not count as writes.
        private struct NativeState
        {
            public int RawQueue;
            public bool Instancing;
            public bool DoubleSidedGI;
        }

        // Keyed by the material reference. Unity 6000.6 makes Object.GetInstanceID a hard error.
        private static readonly HashSet<Object> s_ConsumedTargets = new HashSet<Object>();
        private static readonly HashSet<Object> s_DirectTargets = new HashSet<Object>();
        private static readonly HashSet<Object> s_NativeChanged = new HashSet<Object>();
        private static readonly HashSet<Object> s_PendingSkip = new HashSet<Object>();
        private static readonly HashSet<Object> s_InspectorTargets = new HashSet<Object>();
        private static readonly Dictionary<Object, NativeState> s_NativeBefore = new Dictionary<Object, NativeState>();
        private static bool s_InspectorPass;
        private static bool s_MenuOperation;
        private static int s_PostPassesLeft;

        internal static bool WroteAsset => s_DirectTargets.Count > 0;

        internal static void NoteCallbackResult(MaterialProperty property, bool handled)
        {
            if (property == null || property.targets == null) return;
            for (int i = 0; i < property.targets.Length; i++)
            {
                Object target = property.targets[i];
                if (target == null) continue;
                if (handled)
                {
                    s_ConsumedTargets.Add(target);
                    // Only this setter's validation is skipped. An earlier direct write stays in s_DirectTargets.
                    s_PendingSkip.Add(target);
                }
                else
                {
                    s_DirectTargets.Add(target);
                    s_PendingSkip.Remove(target);
                }
            }
        }

        private static void ClearValidationScope()
        {
            s_ConsumedTargets.Clear();
            s_DirectTargets.Clear();
            s_NativeChanged.Clear();
            s_PendingSkip.Clear();
            s_InspectorTargets.Clear();
            s_NativeBefore.Clear();
            s_InspectorPass = false;
            s_PostPassesLeft = 0;
        }

        private void CaptureNativeState()
        {
            s_NativeBefore.Clear();
            foreach (Object target in m_Editor.targets)
            {
                var material = target as Material;
                if (material == null) continue;
                s_InspectorTargets.Add(material);
                s_NativeBefore[material] = new NativeState
                {
                    RawQueue = material.rawRenderQueue,
                    Instancing = material.enableInstancing,
                    DoubleSidedGI = material.doubleSidedGI
                };
            }
        }

        private void NoteNativeChanges()
        {
            foreach (Object target in m_Editor.targets)
            {
                var material = target as Material;
                if (material == null || !s_NativeBefore.TryGetValue(material, out NativeState before)) continue;
                if (before.RawQueue != material.rawRenderQueue
                    || before.Instancing != material.enableInstancing
                    || before.DoubleSidedGI != material.doubleSidedGI)
                    s_NativeChanged.Add(material);
            }
        }

        public override void ValidateMaterial(Material material)
        {
            if (material == null) return;
            // This setter's callback kept the write. Do not Apply just because an earlier write was direct.
            if (s_PendingSkip.Remove(material)) return;
            if (s_InspectorPass || s_MenuOperation)
            {
                s_DirectTargets.Add(material);
                FairyToonMaterialSetup.Apply(material);
                return;
            }
            if (s_PostPassesLeft > 0 && s_InspectorTargets.Contains(material))
            {
                s_PostPassesLeft--;
                // Direct writes already ran Apply in their own setter validation.
                if (s_NativeChanged.Contains(material)) FairyToonMaterialSetup.Apply(material);
                if (s_PostPassesLeft == 0) ClearValidationScope();
                return;
            }
            FairyToonMaterialSetup.Apply(material);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            FairyToonMaterialSetup.Apply(material);
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            m_Editor = materialEditor;
            m_Props = properties;
            DrawVersionFoldout();
            DrawHelpFoldout();
            ClearValidationScope();
            s_InspectorPass = true;
            CaptureNativeState();
            var wrapped = new List<SavedCallback>();
            if (properties != null)
                for (int i = 0; i < properties.Length; i++)
                    WrapCallback(properties[i], wrapped);
            bool exitGui = false;
            try
            {
                InitStyles();
                FairyToonL10n.DrawLanguagePopup();
                FairyToonL10n.DrawModeToolbar();
                EditorGUILayout.Space(2f);

                switch (FairyToonL10n.Mode)
                {
                    case FairyToonEditorMode.Advanced:
                        DrawAdvanced();
                        break;
                    case FairyToonEditorMode.Preset:
                        DrawPresetPage();
                        break;
                    case FairyToonEditorMode.ShaderSetting:
                        DrawShaderSettingPage();
                        break;
                    default:
                        throw new InvalidOperationException("Unhandled FairyToon editor mode " + FairyToonL10n.Mode);
                }
            }
            catch (ExitGUIException)
            {
                exitGui = true;
                throw;
            }
            catch (Exception)
            {
                exitGui = true;
                throw;
            }
            finally
            {
                RestoreCallbacks(wrapped);
                s_InspectorPass = false;
                m_InnerDepth = 0;
                if (exitGui)
                {
                    ClearValidationScope();
                }
                else
                {
                    NoteNativeChanges();
                    if (GUI.changed) s_PostPassesLeft = s_InspectorTargets.Count;
                    else ClearValidationScope();
                }
            }
        }

        internal struct SavedCallback
        {
            public MaterialProperty Property;
            public MaterialProperty.ApplyPropertyCallback Original;
        }

        private static void WrapCallback(MaterialProperty property, List<SavedCallback> wrapped)
        {
            if (property == null || property.applyPropertyCallback == null) return;
            MaterialProperty.ApplyPropertyCallback original = property.applyPropertyCallback;
            wrapped.Add(new SavedCallback { Property = property, Original = original });
            property.applyPropertyCallback = (changed, mask, previous) =>
            {
                bool handled = original(changed, mask, previous);
                NoteCallbackResult(changed, handled);
                return handled;
            };
        }

        private static void RestoreCallbacks(List<SavedCallback> wrapped)
        {
            for (int i = 0; i < wrapped.Count; i++)
                if (wrapped[i].Property != null) wrapped[i].Property.applyPropertyCallback = wrapped[i].Original;
        }

        // The gear menu runs after OnGUI restores the original callbacks. Wrap them again for that one action
        // so its setter validation uses the same per-material result as a field edit.
        internal static void BeginMenuOperation(MaterialProperty[] props, List<SavedCallback> wrapped)
        {
            s_ConsumedTargets.Clear();
            s_DirectTargets.Clear();
            s_MenuOperation = true;
            if (props == null) return;
            for (int i = 0; i < props.Length; i++) WrapCallback(props[i], wrapped);
        }

        internal static void EndMenuOperation(List<SavedCallback> wrapped)
        {
            if (wrapped != null) RestoreCallbacks(wrapped);
            s_MenuOperation = false;
            s_ConsumedTargets.Clear();
            s_DirectTargets.Clear();
        }

        internal static void NoteDirectWrite(Object target)
        {
            if (target != null) s_DirectTargets.Add(target);
        }

        internal static bool TargetWroteAsset(Object target)
        {
            return target != null && s_DirectTargets.Contains(target);
        }

        private static void DrawVersionFoldout()
        {
            string key = PrefsPrefix + "Foldout.Version";
            bool open = EditorPrefs.GetBool(key, false);
            var versionStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
            Rect position = EditorGUILayout.GetControlRect();
            EditorGUI.LabelField(position, "FairyToon 0.2.1", versionStyle);
            open = EditorGUI.Foldout(position, open, GUIContent.none, true);
            if (open != EditorPrefs.GetBool(key, false)) EditorPrefs.SetBool(key, open);
            if (!open) return;
            EditorGUI.indentLevel++;
            if (GUILayout.Button("GitHub")) Application.OpenURL("https://github.com/p2rumia-prog/FairyToon");
            EditorGUI.indentLevel--;
        }

        private static void DrawHelpFoldout()
        {
            string key = PrefsPrefix + "Foldout.Help";
            bool open = EditorPrefs.GetBool(key, false);
            open = EditorGUILayout.Foldout(open, FairyToonL10n.Text("sHelp"), true);
            if (open != EditorPrefs.GetBool(key, false)) EditorPrefs.SetBool(key, open);
            if (!open) return;
            EditorGUI.indentLevel++;
            if (GUILayout.Button("README")) Application.OpenURL("https://github.com/p2rumia-prog/FairyToon#readme");
            EditorGUI.indentLevel--;
        }

        // Section order follows lilToon 2.4.1's full inspector, with FairyToon-only sections beside the
        // group they belong to. See the pull request for the before/after list.
        //   Base Setting, Lighting
        //   Color: Main, Main 2nd, Main 3rd, Sheer Fabric, 3D Iris (Toon) or Endfield (in Shadow's place), Shadow, RimShade, Emission
        //   Normal Map & Reflection: Normal, Normal 2nd, Anisotropy, Backlight, Reflection, Wetness, MatCap, Rim Light, Glitter, Gem
        //   Advanced: Outline, Dissolve, Refraction, Fur, See-through Hair, Stencil, Rendering
        private void DrawAdvanced()
        {
            s_Search = EditorGUILayout.TextField(s_Search, EditorStyles.toolbarSearchField);
            RenderingMode();
            EditorGUI.BeginChangeCheck();
            Group("sBaseSetting");
            BaseSetting();
            Lighting();
            Group("sColors");
            MainColor();
            if (Prop("_SheerAmount") != null) SheerFabric();
            var shading = Prop("_ShadingModel");
            bool mixedShading = shading != null && shading.hasMixedValue;
            bool endfield = shading != null && !shading.hasMixedValue
                && Mathf.RoundToInt(Number(shading)) == FairyToonMaterialSetup.ShadingEndfield;
            bool showToon = mixedShading || !endfield;
            bool showEndfield = mixedShading || endfield;
            if (showToon && !EverySelectedIsGem()) ToonIris();
            if (showEndfield) Endfield();
            if (showToon && !EverySelectedIsGem()) Shadow();
            if (!EverySelectedIsGem()) RimShade();
            EmissionGroup();
            Group("sNormalMapReflection");
            NormalGroup();
            if (!EverySelectedIsGem()) Backlight();
            if (!EverySelectedIsGem()) Reflection();
            Wetness(endfield && !mixedShading);
            MatCapGroup();
            RimLight();
            Glitter();
            if (PrimaryMode() == FairyToonMaterialSetup.Gem) Gem();
            Group("sAdvanced");
            if (PrimaryMode() != FairyToonMaterialSetup.Fur && PrimaryMode() != FairyToonMaterialSetup.Gem)
                Outline();
            if (AnyRunsWholeDissolve()) Dissolve();
            if (PrimaryMode() == FairyToonMaterialSetup.Refraction) Refraction();
            if (PrimaryMode() == FairyToonMaterialSetup.Fur) Fur();
            if (PrimaryMode() != FairyToonMaterialSetup.Refraction && PrimaryMode() != FairyToonMaterialSetup.Fur && PrimaryMode() != FairyToonMaterialSetup.Gem)
                SeeThrough();
            Stencil();
            Rendering();
            if (EditorGUI.EndChangeCheck())
            {
                NoteNativeChanges();
                foreach (Object target in m_Editor.targets)
                {
                    var material = target as Material;
                    if (material != null && s_NativeChanged.Contains(material))
                        FairyToonMaterialSetup.Apply(material);
                }
            }
        }

        private void Group(string key)
        {
            string shown = FairyToonL10n.Text(key);
            // A group title is only a heading. It does not unfilter the rows under it.
            if (Searching && !Hit(shown)) return;
            EditorGUILayout.Space();
            GUILayout.Label(shown, EditorStyles.boldLabel);
        }

        private void EmissionGroup()
        {

            if (!PrefixMatches("_Emission") && !PrefixMatches("_UseEmission") && !SearchAllows("|ft.Tiling", "|ft.Offset")) return;
            if (!BeginSection("EmissionGroup", "sEmissionSetting", null, false, PropertyBlock.Emission)) return;
            Emission(false);
            Emission(true);
            EndSection();
        }

        private void NormalGroup()
        {

            if (!SearchAllows("_UseBumpMap", "_BumpMap", "_BumpScale", "_UseBump2ndMap", "_Bump2ndMap", "_Bump2ndScale", "_Bump2ndMap_UVMode", "_Bump2ndScaleMask", "_UseAnisotropy", "_AnisotropyTangentMap", "_AnisotropyScale", "_AnisotropyScaleMask", "_AnisotropyTangentWidth", "_AnisotropyBitangentWidth", "_AnisotropyShift", "_AnisotropyShiftNoiseScale", "_AnisotropySpecularStrength", "_Anisotropy2ndTangentWidth", "_Anisotropy2ndBitangentWidth", "_Anisotropy2ndShift", "_Anisotropy2ndShiftNoiseScale", "_Anisotropy2ndSpecularStrength", "_AnisotropyShiftNoiseMask", "_Anisotropy2Reflection", "_Anisotropy2MatCap", "_Anisotropy2MatCap2nd", "|ft.Tiling", "|ft.Offset")) return;
            if (!BeginSection("NormalGroup", "sNormalMapSetting", null, false, PropertyBlock.NormalMap)) return;
            NormalMaps();
            Anisotropy();
            EndSection();
        }

        private void MatCapGroup()
        {

            if (!PrefixMatches("_MatCap") && !SearchAllows("_UseMatCap", "_UseMatCap2nd")) return;
            if (!BeginSection("MatCapGroup", "sMatCapSetting", null, false, PropertyBlock.MatCaps)) return;
            MatCap();
            EndSection();
        }

        private bool AnyRunsWholeDissolve()
        {
            bool any = false;
            foreach (Object target in m_Editor.targets)
            {
                int mode = FairyToonMaterialSetup.GetRenderingMode(target as Material);
                if (mode != FairyToonMaterialSetup.Opaque && mode != FairyToonMaterialSetup.Gem) any = true;
            }
            return any;
        }

        private int PrimaryMode() => FairyToonMaterialSetup.GetRenderingMode(m_Editor.target as Material);

        // Unity only draws this GUI when every selected material uses the same shader, so the first target's shader is the selection.
        private bool EverySelectedIsGem() => PrimaryMode() == FairyToonMaterialSetup.Gem;

        private static bool Searching => !string.IsNullOrEmpty(s_Search);

        private static bool Hit(string text) => !string.IsNullOrEmpty(text) && text.IndexOf(s_Search, StringComparison.Ordinal) >= 0;

        /// <summary>True when this row would be drawn and its property name or final label contains the query.</summary>
        private bool LabelOnly(string key)
        {
            if (!Searching) return true;
            return Hit(key) || Hit(FairyToonL10n.Text(key));
        }

        private bool UnityRowVisible(string english)
        {
            if (!Searching) return true;
            return Hit(english) || Hit(L10n.Tr(english));
        }

        private bool RowVisible(string name, string shown, string tooltip = null)
        {
            if (!Searching) return true;
            if (!string.IsNullOrEmpty(name) && Hit(name)) return true;
            if (!string.IsNullOrEmpty(shown) && (Hit(shown) || Hit(FairyToonL10n.Text(shown)))) return true;
            if (!string.IsNullOrEmpty(tooltip) && (Hit(tooltip) || Hit(FairyToonL10n.Text(tooltip)))) return true;
            if (string.IsNullOrEmpty(name)) return false;
            var prop = Prop(name);
            if (prop == null) return false;
            return Hit(prop.displayName) || Hit(FairyToonL10n.Property(name, prop.displayName));
        }

        /// <summary>A section stays visible while any row it actually draws matches. A leading "|" is a label key with no property.</summary>
        private bool SearchAllows(params string[] rows)
        {
            if (!Searching) return true;
            for (int i = 0; i < rows.Length; i++)
            {
                string row = rows[i];
                if (string.IsNullOrEmpty(row)) continue;
                if (row[0] == '|')
                {
                    string key = row.Substring(1);
                    if (Hit(key) || Hit(FairyToonL10n.Text(key))) return true;
                    continue;
                }
                int split = row.IndexOf('|');
                if (split >= 0)
                {
                    string name = row.Substring(0, split);
                    string rest = row.Substring(split + 1);
                    int tipSplit = rest.IndexOf('|');
                    string label = tipSplit >= 0 ? rest.Substring(0, tipSplit) : rest;
                    string tip = tipSplit >= 0 ? rest.Substring(tipSplit + 1) : null;
                    if (RowVisible(name, label, tip)) return true;
                    continue;
                }
                if (RowVisible(row, null)) return true;
            }
            return false;
        }

        /// <summary>Matches a drawn property whose name starts with <paramref name="prefix"/> and not <paramref name="except"/>.</summary>
        private bool PrefixMatches(string prefix, string except = null)
        {
            if (!Searching) return true;
            if (m_Props == null) return false;
            for (int i = 0; i < m_Props.Length; i++)
            {
                MaterialProperty prop = m_Props[i];
                if (prop == null || !prop.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (except != null && prop.name.StartsWith(except, StringComparison.Ordinal)) continue;
                if (RowVisible(prop.name, null)) return true;
            }
            return false;
        }

        private void DrawPresetPage()
        {
            Help(FairyToonL10n.Text("ft.presetPlaceholder"), MessageType.Info);
        }

        private void DrawShaderSettingPage()
        {
            Help(FairyToonL10n.Text("ft.shaderSettingIntro"), MessageType.Info);
            EditorGUILayout.LabelField(FairyToonL10n.Text("ft.languageLivesInTopBar"), EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(FairyToonL10n.Text("ft.defaultShadingHeader"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(FairyToonL10n.Text("ft.defaultShadingBody"), EditorStyles.wordWrappedLabel);
        }

        // ---- Sections (lilToon order) ----

        private void BaseSetting()
        {
            if (!SearchAllows("_ShadingModel", "_TransparentMode|sRenderingMode", "_Cull", "_ZWrite", "_Cutoff", "_FlipNormal", "_BackfaceColor", "_AAStrength", "_ShadingModel|Shading Model")) return;
            if (!BeginSection("Base", "sBaseSetting", null, true, PropertyBlock.Base)) return;
            P("_ShadingModel", "Shading Model");
            P("_Cull");
            if (AnyCoverage(FairyToonMaterialSetup.Opaque) || Prop("_TransparentMode")?.hasMixedValue == true)
                P("_ZWrite");
            if (AnyCoverage(FairyToonMaterialSetup.Cutout, FairyToonMaterialSetup.Transparent, FairyToonMaterialSetup.Fur))
                P("_Cutoff");
            P("_FlipNormal");
            P("_BackfaceColor");
            P("_AAStrength");
            EndSection();
        }

        private string MainColorLabel()
        {
            var mode = Prop("_TransparentMode");
            bool alpha = (mode != null && mode.hasMixedValue) || PrimaryMode() != FairyToonMaterialSetup.Opaque;
            return alpha ? "sColorAlpha" : "sColor";
        }

        private void Lighting()
        {

            if (!SearchAllows("_LightMinLimit", "_LightMaxLimit", "_MonochromeLighting", "_AsUnlit", "_LightMinLimit|sLightMinLimit", "_LightMaxLimit|sLightMaxLimit", "_MonochromeLighting|sMonochromeLighting", "_AsUnlit|sAsUnlit")) return;
            if (!BeginSection("Lighting", "sLightingSettings", null, false, PropertyBlock.Lighting)) return;
            P("_LightMinLimit", "sLightMinLimit");
            P("_LightMaxLimit", "sLightMaxLimit");
            P("_MonochromeLighting", "sMonochromeLighting");
            P("_AsUnlit", "sAsUnlit");
            EndSection();
        }

        private void MainColor()
        {
            bool gem = EverySelectedIsGem();
            bool first = gem
                ? SearchAllows("_MainTex", "_Color", "|ft.Tiling", "|ft.Offset")
                : SearchAllows("_MainTex", "_Color", "_MainTexHSVG", "_MainColorAdjustMask", "|ft.Tiling", "|ft.Offset", "|sReset");
            bool alpha = SearchAllows("_AlphaMaskMode", "_AlphaMask", "_AlphaMaskScale", "_AlphaMaskValue", "_Cutoff", "|sAlphaMaskWarnOpaque", "|ft.AlphaMaskInvert", "|ft.AlphaMaskTransparency", "|ft.AlphaMaskAdvanced", "|ft.Offset", "|ft.Tiling");
            bool second = PrefixMatches("_Main2nd") || PrefixMatches("_FT_Main2nd") || SearchAllows("_Color2nd", "_UseMain2ndTex", "|UV Line Angle (radians)", "|Position / Direction (XY UV, XYZ Object, W UV Angle)", "|ft.Tiling", "|ft.Offset", "|Decal", "|sMirrorMode", "|sCopyMode", "|Position X", "|Position Y", "|Scale X", "|Scale Y");
            bool third = PrefixMatches("_Main3rd") || PrefixMatches("_FT_Main3rd") || SearchAllows("_Color3rd", "_UseMain3rdTex", "|UV Line Angle (radians)", "|Position / Direction (XY UV, XYZ Object, W UV Angle)", "|ft.Tiling", "|ft.Offset", "|Decal", "|sMirrorMode", "|sCopyMode", "|Position X", "|Position Y", "|Scale X", "|Scale Y");
            if (Searching && !first && !alpha && !second && !third) return;
            if (!BeginSection("Main", "sMainColorSetting", null, true, PropertyBlock.MainColor)) return;
            if (first) MainColorFirst();
            if (second) MainColorLayer("2nd");
            if (third) MainColorLayer("3rd");
            if (alpha) AlphaMask();
            EndSection();
        }

        private void MainColorFirst()
        {
            // lilToon draws this group directly under the parent, with a label and gear, not another foldout.
            BeginGearRow("sMainColor", PropertyBlock.MainColor1st);
            Tex("_MainTex", "_Color", MainColorLabel());
            ScaleOffset("_MainTex");
            if (!EverySelectedIsGem())
            {
                string adjustKey = PrefsPrefix + "Foldout.MainAdjust";
                bool adjust = EditorGUILayout.Foldout(EditorPrefs.GetBool(adjustKey, false), FairyToonL10n.Text("sColorAdjust"), true);
                if (adjust != EditorPrefs.GetBool(adjustKey, false)) EditorPrefs.SetBool(adjustKey, adjust);
                if (adjust || Searching)
                {
                    // lilToon draws the mask, then HSV / Gamma with its own reset. Default is (0, 1, 1, 1).
                    Tex("_MainColorAdjustMask", null, "Color Adjustment Mask");
                    if (!Searching || RowVisible("_MainTexHSVG", "Hue / Saturation / Value / Gamma"))
                    {
                        EditorGUILayout.LabelField("HSV / Gamma");
                        HsvgSliders("_MainTexHSVG");
                    }
                    var hsvg = Prop("_MainTexHSVG");
                    if (hsvg != null && (!Searching || LabelOnly("sReset")) && GUILayout.Button(FairyToonL10n.Text("sReset")))
                        hsvg.vectorValue = new Vector4(0f, 1f, 1f, 1f);
                }
            }
            EndGearRow();
        }

        private void AlphaMask()
        {
            if (EverySelectedIsGem()) return;
            var coverage = Prop("_TransparentMode");
            bool mixedCoverage = coverage != null && coverage.hasMixedValue;
            bool opaque = PrimaryMode() == FairyToonMaterialSetup.Opaque && !mixedCoverage;
            if (opaque)
            {
                Help("sAlphaMaskWarnOpaque", MessageType.Info);
                return;
            }
            EditorGUILayout.BeginVertical(s_Box);
            EditorGUILayout.LabelField(FairyToonL10n.Text("sAlphaMask"));
            PropertyGearRow("_AlphaMaskMode", "Mode", PropertyBlock.AlphaMask);
            var mode = Prop("_AlphaMaskMode");
            if (mode != null && (mode.hasMixedValue || Number(mode) != 0f))
            {
                Tex("_AlphaMask", null, "Alpha Mask");
                ScaleOffset("_AlphaMask");
                var scale = Prop("_AlphaMaskScale");
                var offset = Prop("_AlphaMaskValue");
                if (scale != null && offset != null)
                {
                    // Alt shows the real property names, as lilToon does. Ordinary search uses each control's own label.
                    string invertLabel = Event.current.alt ? scale.name : FairyToonL10n.Text("ft.AlphaMaskInvert");
                    string transparencyLabel = Event.current.alt
                        ? scale.name + ", " + offset.name
                        : FairyToonL10n.Text("ft.AlphaMaskTransparency");
                    bool invert = Number(scale) < 0f;
                    float transparency = Number(offset) - (invert ? 1f : 0f);
                    bool invertChanged = false;
                    bool transparencyChanged = false;
                    if (!Searching || Hit(invertLabel))
                    {
                        Rect row = EditorGUILayout.GetControlRect();
                        BeginScope(row, scale);
                        BeginScope(row, offset);
                        try
                        {
                            EditorGUI.showMixedValue = scale.hasMixedValue || offset.hasMixedValue;
                            EditorGUI.BeginChangeCheck();
                            invert = EditorGUI.Toggle(row, invertLabel, invert);
                            invertChanged = EditorGUI.EndChangeCheck();
                        }
                        finally
                        {
                            EditorGUI.showMixedValue = false;
                            EndScope();
                            EndScope();
                        }
                    }
                    if (!Searching || Hit(transparencyLabel))
                    {
                        Rect row = EditorGUILayout.GetControlRect();
                        BeginScope(row, offset);
                        try
                        {
                            EditorGUI.showMixedValue = scale.hasMixedValue || offset.hasMixedValue;
                            EditorGUI.BeginChangeCheck();
                            transparency = EditorGUI.Slider(row, transparencyLabel, transparency, -1f, 1f);
                            transparencyChanged = EditorGUI.EndChangeCheck();
                        }
                        finally
                        {
                            EditorGUI.showMixedValue = false;
                            EndScope();
                        }
                    }
                    if (invertChanged || transparencyChanged)
                        WriteAlphaMask(scale, offset, invertChanged, transparencyChanged, invert, transparency);
                }
                if (Prop("_Cutoff") != null) P("_Cutoff");
                m_AlphaMaskAdvanced = EditorGUILayout.Toggle(Event.current.alt ? "_AlphaMaskScale, _AlphaMaskValue" : FairyToonL10n.Text("ft.AlphaMaskAdvanced"), m_AlphaMaskAdvanced);
                if (m_AlphaMaskAdvanced)
                {
                    EditorGUI.indentLevel++;
                    P("_AlphaMaskScale", "Scale");
                    P("_AlphaMaskValue", "Offset");
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void MainColorLayer(string layer)
        {
            string prefix = "_Main" + layer;
            string titleKey = layer == "2nd" ? "sMainColor2nd" : "sMainColor3rd";
            var block = layer == "2nd" ? PropertyBlock.MainColor2nd : PropertyBlock.MainColor3rd;
            if (!BeginSection("Main" + layer, titleKey, Prop("_UseMain" + layer + "Tex"), false, block, false)) return;
            Tex(prefix + "Tex", "_Color" + layer, "sColor");
            ColorAlpha("_Color" + layer);
            P(prefix + "TexIsMSDF", "sMSDFTexture");
            P(prefix + "Tex_Cull", "sCullMode");
            P(prefix + "EnableLighting", "sEnableLighting");
            P(prefix + "TexBlendMode", "sBlendMode");
            P(prefix + "TexAlphaMode", "sTransparentMode");
            P(prefix + "Tex_UVMode", "UV Mode");
            DrawDecal(prefix);
            var decal = Prop(prefix + "TexIsDecal");
            if (decal == null || decal.hasMixedValue || Number(decal) <= 0.5f) ScaleOffset(prefix + "Tex");
            P(prefix + "TexAngle", "sAngle");
            ScrollAndRotate(prefix + "Tex_ScrollRotate");
            Tex(prefix + "BlendMask", null, "sMask");
            if (!EverySelectedIsGem()) DissolveControls(prefix + "Dissolve", false);
            EndSection();
        }

        // lilToon UV4Decal: one Decal toggle, then Mirror Mode and Copy Mode. Turning Decal off clears the five flags.
        private void DrawDecal(string prefix)
        {
            var isDecal = Prop(prefix + "TexIsDecal");
            var left = Prop(prefix + "TexIsLeftOnly");
            var right = Prop(prefix + "TexIsRightOnly");
            var copy = Prop(prefix + "TexShouldCopy");
            var flipMirror = Prop(prefix + "TexShouldFlipMirror");
            var flipCopy = Prop(prefix + "TexShouldFlipCopy");
            if (isDecal == null || left == null || right == null || copy == null || flipMirror == null || flipCopy == null) return;
            if (!Searching || RowVisible(isDecal.name, "Decal"))
            {
                Rect row = EditorGUILayout.GetControlRect();
                BeginScope(row, isDecal);
                EditorGUI.showMixedValue = isDecal.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                bool on = EditorGUI.Toggle(row, FairyToonL10n.Text("Decal"), Number(isDecal) > 0.5f);
                if (EditorGUI.EndChangeCheck())
                {
                    SetNumber(isDecal, on ? 1f : 0f);
                    if (!on)
                    {
                        SetNumber(left, 0f);
                        SetNumber(right, 0f);
                        SetNumber(flipMirror, 0f);
                        SetNumber(copy, 0f);
                        SetNumber(flipCopy, 0f);
                    }
                }
                EditorGUI.showMixedValue = false;
                EndScope();
            }
            if (!isDecal.hasMixedValue && Number(isDecal) <= 0.5f) return;
            DrawMirrorMode(left, right, flipMirror);
            DrawCopyMode(copy, flipCopy);
            if (!isDecal.hasMixedValue && Number(isDecal) > 0.5f)
                DecalPlacement(prefix + "Tex", copy, flipCopy);
        }

        private void DrawMirrorMode(MaterialProperty left, MaterialProperty right, MaterialProperty flip)
        {
            string label = Event.current.alt
                ? left.name + ", " + right.name + ", " + flip.name
                : FairyToonL10n.Text("sMirrorMode");
            if (Searching && !Hit(label)) return;
            bool mixed = left.hasMixedValue || right.hasMixedValue || flip.hasMixedValue;
            int mode = 0;
            if (!mixed)
            {
                if (Number(right) > 0.5f) mode = 3;
                if (Number(flip) > 0.5f) mode++;
                if (Number(left) > 0.5f) mode = 2;
            }
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(label, mixed ? -1 : mode, new[]
            {
                FairyToonL10n.Text("sMirrorModeNormal"),
                FairyToonL10n.Text("sMirrorModeFlip"),
                FairyToonL10n.Text("sMirrorModeLeft"),
                FairyToonL10n.Text("sMirrorModeRight"),
                FairyToonL10n.Text("sMirrorModeRightFlip")
            });
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck() || next < 0) return;
            SetNumber(left, next == 2 ? 1f : 0f);
            SetNumber(right, next >= 3 ? 1f : 0f);
            SetNumber(flip, next == 1 || next == 4 ? 1f : 0f);
        }

        private void DrawCopyMode(MaterialProperty copy, MaterialProperty flipCopy)
        {
            string label = Event.current.alt ? copy.name + ", " + flipCopy.name : FairyToonL10n.Text("sCopyMode");
            if (Searching && !Hit(label)) return;
            bool mixed = copy.hasMixedValue || flipCopy.hasMixedValue;
            int mode = 0;
            if (!mixed)
            {
                if (Number(copy) > 0.5f) mode = 1;
                if (Number(flipCopy) > 0.5f) mode = 2;
            }
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(label, mixed ? -1 : mode, new[]
            {
                FairyToonL10n.Text("sCopyModeNormal"),
                FairyToonL10n.Text("sCopyModeSymmetry"),
                FairyToonL10n.Text("sCopyModeFlip")
            });
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck() || next < 0) return;
            SetNumber(copy, next == 0 ? 0f : 1f);
            SetNumber(flipCopy, next == 2 ? 1f : 0f);
        }

        // Root dissolve is an advanced section after Outline and before Refraction/Fur, as in lilToon.
        private void Dissolve()
        {
            if (!PrefixMatches("_Dissolve") && !PrefixMatches("_FT_Dissolve") && !SearchAllows("|Mode", "|Shape", "|Threshold", "|Edge Width", "|Edge Color", "|Position", "|Direction", "|UV Line Angle (radians)", "|ft.DissolveOff", "|ft.DissolveMask", "|ft.DissolveUV", "|ft.DissolveObject", "|ft.DissolveRadial", "|ft.DissolveLinear")) return;
            if (!BeginSection("Dissolve", "sDissolve", null, false, PropertyBlock.Dissolve)) return;
            DissolveControls("_Dissolve", true);
            EndSection();
        }
        // Mirrors lilMainInspectorGUI's dissolve ordering; native Vector flags need no lilToon drawer dependency.
        private void DissolveControls(string prefix, bool wholeMaterial)
        {
            if (Prop(prefix + "Params") == null) return;
            if (!wholeMaterial) SubHeader("Dissolve");
            if (SelectedRenderingMode(FairyToonMaterialSetup.Fur))
            Help("Dissolve affects the Fur body only; Fur ribbon shells ignore whole-material and layer dissolve.", MessageType.Info);

            DissolveParameter(prefix + "Params", 0, "Mode", new[] { "ft.DissolveOff", "ft.DissolveMask", "ft.DissolveUV", "ft.DissolveObject" });
            var parameters = Prop(prefix + "Params");
            int mode = Mathf.RoundToInt(parameters.vectorValue.x);
            bool mixedMode = DissolveComponentMixed(parameters, 0);
            if (mode == 0 && !mixedMode) return;
            using (new EditorGUI.IndentLevelScope())
            {
                if (mode >= 2 || mixedMode)
                DissolveParameter(prefix + "Params", 1, "Shape", new[] { "ft.DissolveRadial", "ft.DissolveLinear" });
                // lilDissolve uses sliders for mask mode and unrestricted values for positional modes.
                bool maskOnly = mode == 1 && !mixedMode;
                DissolveParameter(prefix + "Params", 2, "Threshold", null, maskOnly, -1f, 2f);
                DissolveParameter(prefix + "Params", 3, "Edge Width", null, maskOnly, 0f, 1f);
                if (mode == 1 || mixedMode)
                {
                    Tex(prefix + "Mask", null, "Mask");
                    ScaleOffset(prefix + "Mask");
                }

                string noiseToggle = "_FT_" + prefix.Substring(1) + "UseNoise";
                var useNoise = Prop(noiseToggle);
                bool noNoise = useNoise != null && !useNoise.hasMixedValue && Number(useNoise) < 0.5f;
                parameters = Prop(prefix + "Params");
                int shape = Mathf.RoundToInt(parameters.vectorValue.y);
                bool mixedShape = DissolveComponentMixed(parameters, 1);
                if (mode >= 2 || mixedMode)
                {
                    string positionLabel = shape == 1 && !mixedShape ? "Direction" : "Position";
                    if (mixedMode || mixedShape || (mode == 2 && shape == 1 && useNoise != null && useNoise.hasMixedValue))
                    P(prefix + "Pos", "Position / Direction (XY UV, XYZ Object, W UV Angle)");
                    else if (mode == 2 && shape == 1 && !mixedShape && noNoise)
                    DissolveParameter(prefix + "Pos", 3, "UV Line Angle (radians)");
                    else
                    DissolvePosition(prefix + "Pos", positionLabel, mode == 2 ? 2 : 3);
                }

                P(noiseToggle, "Use Noise Helper (lilToon Feature)");
                useNoise = Prop(noiseToggle);
                if (useNoise == null || useNoise.hasMixedValue || Number(useNoise) > 0.5f)
                {
                    Tex(prefix + "NoiseMask", prefix + "NoiseStrength", "Noise / Strength");
                    ScaleOffset(prefix + "NoiseMask");
                    DissolvePosition(prefix + "NoiseMask_ScrollRotate", "Noise Scroll XY", 2);
                }
                if (useNoise != null && (useNoise.hasMixedValue || Number(useNoise) < 0.5f))
                Help("Disabled selects lilToon's no-noise helper. UV Linear then uses Position.w as a rotation angle; enabled uses the XY direction. The default matches lilToon's enabled noise feature even with an empty noise texture.", MessageType.Info);
                P(prefix + "Color", "Edge Color");
            }
        }

        private void DissolveParameter(string name, int component, string label, string[] options = null,
        bool slider = false, float minimum = 0f, float maximum = 1f)
        {
            var property = Prop(name);
            if (property == null) return;
            bool optionHit = false;
            if (Searching && options != null)
                for (int i = 0; i < options.Length; i++)
                    optionHit |= Hit(options[i]) || Hit(FairyToonL10n.Text(options[i]));
            if (!RowVisible(name, label) && !optionHit) return;
            Vector4 value = property.vectorValue;
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = DissolveComponentMixed(property, component);
            string shown = FairyToonL10n.Text(label);
            string[] localized = null;
            if (options != null)
            {
                localized = new string[options.Length];
                for (int i = 0; i < options.Length; i++) localized[i] = FairyToonL10n.Text(options[i]);
            }
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            EditorGUI.BeginChangeCheck();
            value[component] = options != null
            ? EditorGUI.Popup(row, shown, Mathf.RoundToInt(value[component]), localized)
            : slider ? EditorGUI.Slider(row, shown, value[component], minimum, maximum)
            : EditorGUI.FloatField(row, shown, value[component]);
            if (EditorGUI.EndChangeCheck()) SetDissolveComponents(property, value, 1 << component);
            EndScope();
            EditorGUI.showMixedValue = previousMixed;
        }

        private void DissolvePosition(string name, string label, int dimensions)
        {
            var scope = Prop(name);
            if (scope == null || !RowVisible(name, label)) return;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, scope);
            Rect values = EditorGUI.PrefixLabel(row, new GUIContent(FairyToonL10n.Text(label)));
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            int previousIndent = EditorGUI.indentLevel;
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUIUtility.labelWidth = 14f;
            EditorGUI.indentLevel = 0;
            try
            {
                const float gap = 4f;
                float width = (values.width - gap * (dimensions - 1)) / dimensions;
                for (int component = 0; component < dimensions; component++)
                {
                    // Refresh after each write, then edit only this axis across the selected materials.
                    var property = Prop(name);
                    Vector4 value = property.vectorValue;
                    EditorGUI.showMixedValue = DissolveComponentMixed(property, component);
                    var field = new Rect(values.x + component * (width + gap), values.y, width, values.height);
                    EditorGUI.BeginChangeCheck();
                    value[component] = EditorGUI.FloatField(field, "XYZ"[component].ToString(), value[component]);
                    if (EditorGUI.EndChangeCheck()) SetDissolveComponents(property, value, 1 << component);
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
                EditorGUI.indentLevel = previousIndent;
                EditorGUI.showMixedValue = previousMixed;
                EndScope();
            }
        }
        private static bool DissolveComponentMixed(MaterialProperty property, int component)
        {
            // The descriptor is the value on screen, including a property-block snapshot Unity already read in.
            // material.GetVector would mix that with the asset. Per-component mixed is not exposed, so any
            // mixed vector marks each component mixed. `component` stays in the signature for the callers.
            if (component < 0 || component > 3) return false;
            return property.hasMixedValue;
        }

        // MaterialProperty only accepts the whole vector. A shared descriptor would copy one target's
        // unedited components onto the others, so an asset edit merges the changed component into each material.
        // A callback is one renderer snapshot: merge into that snapshot and let the callback keep it.
        private void SetDissolveComponents(MaterialProperty property, Vector4 value, int components)
        {
            Vector4 merged = property.vectorValue;
            for (int component = 0; component < 4; component++)
                if ((components & (1 << component)) != 0) merged[component] = value[component];
            if (property.applyPropertyCallback != null)
            {
                // Returning true stops Unity writing this one vector onto every target. If the
                // callback did not keep the write, merge the edited axis into each material.
                bool handled = false;
                MaterialProperty.ApplyPropertyCallback original = property.applyPropertyCallback;
                property.applyPropertyCallback = (changed, mask, previous) =>
                {
                    handled = original(changed, mask, previous);
                    return true;
                };
                try { property.vectorValue = merged; }
                finally { property.applyPropertyCallback = original; }
                if (handled) return;
            }
            else if (property.targets == null || property.targets.Length <= 1)
            {
                property.vectorValue = merged;
                return;
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Modify " + property.displayName);
            foreach (Object target in property.targets)
            {
                var material = target as Material;
                if (material == null || !material.HasProperty(property.name)) continue;
                Vector4 current = material.GetVector(property.name);
                Vector4 next = current;
                for (int component = 0; component < 4; component++)
                    if ((components & (1 << component)) != 0) next[component] = value[component];
                if (next == current) continue;
                Undo.RecordObject(material, "Modify " + property.displayName);
                material.SetVector(property.name, next);
                FairyToonMaterialSetup.Apply(material);
            }
            Undo.CollapseUndoOperations(group);
        }

        private void Shadow()
        {

            if (!SearchAllows("_UseShadow", "_ShadowMaskType", "_ShadowStrengthMask", "_ShadowStrength", "_ShadowFlatBorder", "_ShadowFlatBlur", "_UseShadowRamp", "_ShadowRampTex", "_ShadowColorTex", "_ShadowColor", "_ShadowBorder", "_ShadowBlur", "_ShadowNormalStrength", "_ShadowReceive", "_CharShadowReceive", "_Shadow2ndColorTex", "_Shadow2ndColor", "_Shadow2ndBorder", "_Shadow2ndBlur", "_Shadow2ndNormalStrength", "_Shadow2ndReceive", "_Shadow3rdColorTex", "_Shadow3rdColor", "_Shadow3rdBorder", "_Shadow3rdBlur", "_Shadow3rdNormalStrength", "_Shadow3rdReceive", "_ShadowMainStrength", "_ShadowEnvStrength", "_ShadowBorderColor", "_ShadowBorderRange", "_ShadowBlurMask", "_BackfaceForceShadow", "_ShadowBorderMask", "_ShadowAOShift", "_ShadowAOShift2", "_ShadowPostAO", "_SSAOStrength", "_SSAOLitStrength", "_SSAOMask", "_ShadowStrengthMask|Face SDF", "_ShadowFlatBorder|SDF Offset", "_ShadowFlatBlur|SDF Y Scale", "_ShadowStrengthMask|Strength", "_ShadowFlatBorder|Flat Border", "_ShadowFlatBlur|Flat Blur", "_UseShadowRamp|Ramp (FairyToon)", "_ShadowRampTex|Ramp (U = half lambert)", "_ShadowColorTex|Color", "_CharShadowReceive|Character Shadow", "_Shadow2ndColorTex|Color (A = strength)", "_Shadow2ndBorder|Border", "_Shadow2ndBlur|Blur", "_Shadow2ndNormalStrength|Normal Strength", "_Shadow2ndReceive|Receive Shadow", "_Shadow3rdColorTex|Color (A = strength)", "_Shadow3rdBorder|Border", "_Shadow3rdBlur|Blur", "_Shadow3rdNormalStrength|Normal Strength", "_Shadow3rdReceive|Receive Shadow", "_ShadowBlurMask|Blur Mask", "_ShadowBorderMask|AO Map", "_ShadowAOShift|AO Shift (1st scale, offset, 2nd scale, offset)", "_ShadowAOShift2|AO Shift (3rd scale, offset)", "_SSAOStrength|Ambient Strength", "_SSAOLitStrength|Lit Strength", "_SSAOMask|Mask (white = SSAO on)")) return;
            if (!BeginSection("Shadow", "sShadowSetting", Prop("_UseShadow"), true, PropertyBlock.Shadow)) return;
            var maskType = Prop("_ShadowMaskType");
            P("_ShadowMaskType");
            int type = maskType.hasMixedValue ? -1 : Mathf.RoundToInt(Number(maskType));
            if (type == 2)
            {
                Tex("_ShadowStrengthMask", "_ShadowStrength", "Face SDF");
                Help("Face SDF in lilToon's format: R / G thresholds for light from either side, " +
                "B blend back to normal shading, A strength. Add FairyToonFaceShadow to the avatar root (import the map linear, uncompressed).",
                MessageType.None);
                P("_ShadowFlatBorder", "SDF Offset");
                P("_ShadowFlatBlur", "SDF Y Scale");
            }
            else
            {
                Tex("_ShadowStrengthMask", "_ShadowStrength", "sMaskStrength");
                P("_ShadowFlatBorder", "Flat Border");
                P("_ShadowFlatBlur", "Flat Blur");

            }

            ToggleRow("_UseShadowRamp", "Ramp (FairyToon)");
            bool ramp = Float("_UseShadowRamp") > 0.5f;
            if (ramp)
            {
                using (new EditorGUI.IndentLevelScope())
                Tex("_ShadowRampTex", null, "Ramp (U = half lambert)");
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            SubHeader(ramp ? "1st Shadow (ramp replaces the colours)" : "1st Shadow");
            if (!ramp) Tex("_ShadowColorTex", "_ShadowColor", "Color");
            P("_ShadowBorder");
            P("_ShadowBlur");
            P("_ShadowNormalStrength");
            P("_ShadowReceive");
            P("_CharShadowReceive", "Character Shadow");
            EditorGUILayout.EndVertical();

            if (!ramp)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                SubHeader("2nd Shadow");
                Tex("_Shadow2ndColorTex", "_Shadow2ndColor", "Color (A = strength)");
                P("_Shadow2ndBorder", "Border");
                P("_Shadow2ndBlur", "Blur");
                P("_Shadow2ndNormalStrength", "Normal Strength");
                P("_Shadow2ndReceive", "Receive Shadow");
                EditorGUILayout.EndVertical();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                SubHeader("3rd Shadow");
                Tex("_Shadow3rdColorTex", "_Shadow3rdColor", "Color (A = strength)");
                P("_Shadow3rdBorder", "Border");
                P("_Shadow3rdBlur", "Blur");
                P("_Shadow3rdNormalStrength", "Normal Strength");
                P("_Shadow3rdReceive", "Receive Shadow");
                EditorGUILayout.EndVertical();
            }

            P("_ShadowMainStrength");
            P("_ShadowEnvStrength");
            P("_ShadowBorderColor");
            P("_ShadowBorderRange");
            Tex("_ShadowBlurMask", null, "Blur Mask");
            P("_BackfaceForceShadow");

            string aoKey = PrefsPrefix + "Foldout.ShadowAO";
            bool aoOpen = EditorGUILayout.Foldout(EditorPrefs.GetBool(aoKey, false), FairyToonL10n.Text("AO Map"), true);
            if (aoOpen != EditorPrefs.GetBool(aoKey, false)) EditorPrefs.SetBool(aoKey, aoOpen);
            if (aoOpen || Searching)
            {
                Tex("_ShadowBorderMask", null, "AO Map");
                P("_ShadowAOShift", "AO Shift (1st scale, offset, 2nd scale, offset)");
                P("_ShadowAOShift2", "AO Shift (3rd scale, offset)");
                P("_ShadowPostAO");
                P("_SSAOStrength", "Ambient Strength");
                P("_SSAOLitStrength", "Lit Strength");
                Tex("_SSAOMask", null, "Mask (white = SSAO on)");
            }
            EndSection();
        }

        private void Emission(bool second)
        {

            string prefix = second ? "_Emission2nd" : "_Emission";
            if (!PrefixMatches(prefix, second ? null : "_Emission2nd") && !SearchAllows(second ? "_UseEmission2nd" : "_UseEmission", "|ft.Tiling", "|ft.Offset")) return;
            string prefs = second ? "Emission 2nd" : "Emission";
            string title = second ? "sEmission2nd" : "sEmissionSetting";
            var block = second ? PropertyBlock.Emission2nd : PropertyBlock.Emission1st;
            if (!BeginSection(prefs, title, Prop(second ? "_UseEmission2nd" : "_UseEmission"), false, block, false)) return;
            Tex(prefix + "Map", prefix + "Color", "Color");
            ColorAlpha(prefix + "Color");
            P(prefix + "Blend", "sBlend");
            P(prefix + "BlendMode", "sBlendMode");
            P(prefix + "MainStrength", "sStrengthFromMain");
            ScaleOffset(prefix + "Map");
            P(prefix + "Map_UVMode", "UV Mode");
            P(prefix + "Map_ScrollRotate", "Scroll XY / Angle Z / Rotation Speed W");
            Tex(prefix + "BlendMask", null, "sMask");
            ScaleOffset(prefix + "BlendMask");
            P(prefix + "BlendMask_ScrollRotate", "Mask Scroll XY / Angle Z / Rotation Speed W");
            P(prefix + "Blink", "Blink Strength / Hard / Speed / Phase");
            P(prefix + "UseGrad", "Gradation");
            if (Float(prefix + "UseGrad") > 0.5f || Prop(prefix + "UseGrad").hasMixedValue)
            {
                Tex(prefix + "GradTex", null, "Gradation Texture");
                P(prefix + "GradSpeed", "Gradation Speed");
            }
            P(prefix + "Fluorescence", "Fluorescence");

            EndSection();
        }

        private void Endfield()
        {

            if (!IrisMatches() && !SearchAllows("_EFPart", "_EFEyeLight", "_SkinLUT", "_EFSSSColor", "_EFSSSStrength", "_ShadowStrengthMask", "_EFFaceSDFScale", "_RimColorTex", "_EFEyeMask", "_EFFaceHighlight", "_EFFaceHighlightColor", "_EFFaceHighlightShift", "_PBRMap", "_EFMetallic", "_EFSmoothness", "_EFReflectivity", "_EFOcclusion", "_DiffuseRamp", "_ShadowBorder", "_ShadowBlur", "_EFShadowDarkness", "_EFShadowDarker", "_EFShadowSaturation", "_EFBacklight", "_CharShadowReceive", "_ShadowReceive", "_BackfaceForceShadow", "_SSAOStrength", "_SSAOLitStrength", "_SSAOMask", "_HairMask", "_SpecRamp", "_EFHairCentre", "_EFHairBandOffset", "_EFHairLightFollow", "_EFHairFlatten", "_EFHairShift", "_EFHairExponent", "_EFHairSpecular", "_EFHairTopCut", "_EFHairShift2", "_EFHairExponent2", "_EFHairSpecular2", "_EFHairColor2", "_EFHairJitter", "_EFHairHalo", "_EFHairStrandBreakup", "_EFHairStrandBlur", "_EFHairStrandLines", "_EFHairStrandFreq", "_EFHairRootAO", "_EFHairRootLift", "_EFHairRootInner", "_EFHairRootOuter", "_EFHairRootColor", "_EFSpecular", "_EFSpecRampFresnel", "_EFEnvironment", "_GSAAStrength", "_ReflectionCubeTex", "_ReflectionCubeOverride", "_EFRimColor", "_EFRimStrength", "_EFRimWidth", "_EFRimAlbedo", "_EFLightRim", "_EFRimDepth", "_EFRimDepthWidth", "_EFRimDepthGap", "_EFEyeOverlay", "_EFEyeShadowColor", "_EFEyeScatterColor", "_EFIris", "_EFIrisDepth", "_EFPupilLayer", "_EFPupilDepth", "_EFIrisRefraction", "_EFIrisScale", "_EFIrisRim", "_EFIrisEdgeFade", "_EFIrisVertical", "_EFIrisMaxShift", "_EFIrisEdge", "_EFIrisEdgeSoftness", "_EFIrisGaze", "_EFCorneaBulge", "_EFCorneaNormalRange", "_EFCorneaIOR", "_EFIrisSparkleTex", "_EFIrisSparkle", "_EFIrisGlowStrength", "_EFIrisSparkleScale", "_EFIrisGlowColor", "_EFIrisFrame", "_EFIrisWorldRadius", "_EFIrisDish", "_EFIrisCenterL", "_EFIrisCenterR", "_EFIrisRadius", "_EFPupil", "_EFPupilCenterL", "_EFPupilCenterR", "_EFPupilRadius", "_EFPupilYRatio", "_EFPupilScale", "_EFPupilOffset", "_SkinLUT|Shadow LUT (no LUT: Shadow Tone)", "_SkinLUT|Shadow LUT (no LUT: Shadow Tone)", "_ShadowStrengthMask|Face SDF (lilToon format)", "_RimColorTex|Rim Mask (R)", "_EFEyeMask|Eye Mask (R: shades as Eye)", "_EFFaceHighlight|Highlight (R: nose tip, lips)", "_EFFaceHighlightShift|Highlight View Shift", "_PBRMap|PBR Map (R metal, G refl., B AO, A smooth)", "_EFReflectivity|Reflectivity (F0 = 0.04 x value)", "_EFOcclusion|Occlusion (map B)", "_DiffuseRamp|Ramp (RGB hue, A lit weight)", "_ShadowBorder|Border (no ramp)", "_ShadowBlur|Blur (no ramp)", "_CharShadowReceive|Character Shadow", "_ShadowReceive|Receive Scene Shadow", "_SSAOStrength|Ambient Strength", "_SSAOLitStrength|Lit Strength", "_SSAOMask|Mask (white = SSAO on)", "_HairMask|Hair Mask (R highlight, G strand noise, B AO)", "_SpecRamp|Band Colour Ramp (U band strength, V side)", "_SkinLUT|Shadow LUT (optional: warm hair, as skin)", "_EFHairShift|Shift", "_EFHairExponent|Sharpness", "_EFHairSpecular|Strength", "_EFHairTopCut|Top Cut", "_EFHairShift2|Shift", "_EFHairExponent2|Sharpness", "_EFHairSpecular2|Strength", "_EFHairColor2|Colour (A: 0 hair colour, 1 this colour)", "_EFHairStrandBlur|Blur (mip levels)", "_EFHairStrandFreq|Density (per UV unit)", "_EFHairRootLift|Lift (m)", "_EFHairRootInner|Inner (m)", "_EFHairRootOuter|Outer (m)", "_EFHairRootColor|Colour", "_SpecRamp|Specular Ramp", "_GSAAStrength|GSAA (at least 0.5)", "_ReflectionCubeTex|Cubemap Fallback (x ambient)")) return;
            bool mixedPart = Prop("_EFPart") != null && Prop("_EFPart").hasMixedValue;
            int part = Mathf.RoundToInt(Float("_EFPart"));
            bool eye = mixedPart || part == FairyToonMaterialSetup.PartEye;
            bool skin = mixedPart || part == FairyToonMaterialSetup.PartSkin;
            bool face = mixedPart || part == FairyToonMaterialSetup.PartFace;
            bool hair = mixedPart || part == FairyToonMaterialSetup.PartHair;
            bool other = mixedPart || (part != FairyToonMaterialSetup.PartEye && part != FairyToonMaterialSetup.PartFace);
            bool notHair = mixedPart || part != FairyToonMaterialSetup.PartHair;
            if (BeginSection("EFSurface", "ft.EFSurface", null, true, PropertyBlock.EndfieldSurface))
            {
                P("_EFPart");
                if (eye)
                {
                    SubHeader("Eye");
                    P("_EFEyeLight");
                    EyeOverlay();
                    Iris();
                    Help("Eyes ignore shadows and the light direction; they only dim a little when the light " +
                    "is behind the face. Use MatCap for the cornea highlight. Head vectors come from FairyToonFaceShadow.",
                    MessageType.None);
                    SubHeader("Surface");
                }
                if (skin)
                {
                    SubHeader("Skin");
                    Tex("_SkinLUT", null, "Shadow LUT (no LUT: Shadow Tone)");
                    P("_EFSSSColor");
                    P("_EFSSSStrength");
                    SubHeader("Surface");
                }
                if (face)
                {
                    SubHeader("Face");
                    Tex("_SkinLUT", null, "Shadow LUT (no LUT: Shadow Tone)");
                    P("_EFSSSColor");
                    P("_EFSSSStrength");
                    Tex("_ShadowStrengthMask", null, "Face SDF (lilToon format)");
                        P("_EFFaceSDFScale");
                        Tex("_RimColorTex", null, "Rim Mask (R)");
                        Tex("_EFEyeMask", null, "Eye Mask (R: shades as Eye)");
                        P("_EFEyeLight");
                        EyeOverlay();
                        Iris();
                        Tex("_EFFaceHighlight", "_EFFaceHighlightColor", "Highlight (R: nose tip, lips)");
                        P("_EFFaceHighlightShift", "Highlight View Shift");
                        Help("Face SDF: R / G thresholds for light from either side, B blend back to the mesh " +
                        "shading (neck), as in lilToon. Head vectors come from FairyToonFaceShadow on the avatar root. " +
                        "SDF Light Scale: 0.5 for Endfield-style maps (the shadow sweeps over 0-180 degrees), 1 for maps made " +
                        "for lilToon (60-120 degrees).", MessageType.None);
                    SubHeader("Surface");
                }
                if (other) Iris();
                var pbr = Prop("_PBRMap");
                Tex("_PBRMap", null, "PBR Map (R metal, G refl., B AO, A smooth)");
                bool map = pbr != null && (pbr.hasMixedValue || pbr.textureValue != null);
                if (!map) P("_EFMetallic");
                P("_EFSmoothness", map ? "Smoothness (x map A)" : "Smoothness");
                P("_EFReflectivity", "Reflectivity (F0 = 0.04 x value)");
                P("_EFOcclusion", "Occlusion (map B)");
                Help("Sun / shade state, top fill light, light override, terminator offset, directional ambient " +
                "and small lights (FairyToonCharacterLight) come from FairyToonCharacter on the avatar root.", MessageType.None);
                EndSection();
            }

            if (BeginSection("EFDiffuse", "ft.EFDiffuse", null, true, PropertyBlock.EndfieldDiffuse))
            {
                var ramp = Prop("_DiffuseRamp");
                Tex("_DiffuseRamp", null, "Ramp (RGB hue, A lit weight)");
                if (ramp != null && !ramp.hasMixedValue && ramp.textureValue == null)
                {
                    P("_ShadowBorder", "Border (no ramp)");
                    P("_ShadowBlur", "Blur (no ramp)");
                }
                P("_EFShadowDarkness");
                P("_EFShadowDarker");
                P("_EFShadowSaturation");
                P("_EFBacklight");
                P("_CharShadowReceive", "Character Shadow");
                if (face)
                Help("The face's SDF area ignores the character shadow; only the SDF's mesh blend (B: neck, " +
                "ears) takes it.", MessageType.None);
                P("_ShadowReceive", "Receive Scene Shadow");
                P("_BackfaceForceShadow");

                SubHeader("SSAO (FairyToon)");
                P("_SSAOStrength", "Ambient Strength");
                P("_SSAOLitStrength", "Lit Strength");
                Tex("_SSAOMask", null, "Mask (white = SSAO on)");
                EndSection();
            }

            if (hair && BeginSection("EFHair", "ft.EFHair", null, true, PropertyBlock.EndfieldHair))
            {
                Tex("_HairMask", null, "Hair Mask (R highlight, G strand noise, B AO)");
                Tex("_SpecRamp", null, "Band Colour Ramp (U band strength, V side)");
                Tex("_SkinLUT", null, "Shadow LUT (optional: warm hair, as skin)");
                P("_EFHairCentre");
                P("_EFHairBandOffset");
                P("_EFHairLightFollow");
                P("_EFHairFlatten");
                SubHeader("Primary");
                P("_EFHairShift", "Shift");
                P("_EFHairExponent", "Sharpness");
                P("_EFHairSpecular", "Strength");
                P("_EFHairTopCut", "Top Cut");
                SubHeader("Secondary");
                P("_EFHairShift2", "Shift");
                P("_EFHairExponent2", "Sharpness");
                P("_EFHairSpecular2", "Strength");
                P("_EFHairColor2", "Colour (A: 0 hair colour, 1 this colour)");
                SubHeader("Strands");
                P("_EFHairJitter");
                P("_EFHairHalo");
                P("_EFHairStrandBreakup");
                var breakup = Prop("_EFHairStrandBreakup");
                if (breakup != null && (breakup.hasMixedValue || Number(breakup) > 0f)) P("_EFHairStrandBlur", "Blur (mip levels)");
                P("_EFHairStrandLines");
                var lines = Prop("_EFHairStrandLines");
                if (lines != null && (lines.hasMixedValue || Number(lines) > 0f)) P("_EFHairStrandFreq", "Density (per UV unit)");
                P("_EFHairRootAO");
                var rootAO = Prop("_EFHairRootAO");
                if (rootAO != null && (rootAO.hasMixedValue || Number(rootAO) > 0f))
                {
                    P("_EFHairRootLift", "Lift (m)");
                    P("_EFHairRootInner", "Inner (m)");
                    P("_EFHairRootOuter", "Outer (m)");
                    P("_EFHairRootColor", "Colour");
                }
                Help("Kajiya-Kay on a sphere around the head, so the band stays one horizontal ring tied to " +
                "the head. Head frame from FairyToonFaceShadow on the avatar root; Head Centre Height lifts the sphere " +
                "centre above the head bone. Root Occlusion multiplies hair near that centre (roots, inner layers) " +
                "by Root Colour, fading out between Inner and Outer; keep the colour warm or blonde turns olive.",MessageType.None);
                EndSection();
            }

            if (notHair && BeginSection("EFSpecular", "ft.EFSpecular", null, true, PropertyBlock.EndfieldSpecular))
            {
                P("_EFSpecular");
                Tex("_SpecRamp", null, "Specular Ramp");
                P("_EFSpecRampFresnel");
                P("_EFEnvironment");
                P("_GSAAStrength", "GSAA (at least 0.5)");
                Tex("_ReflectionCubeTex", null, "Cubemap Fallback (x ambient)");
                P("_ReflectionCubeOverride");

                EndSection();
            }

            if (BeginSection("EFRim", "ft.EFRim", null, false, PropertyBlock.EndfieldRim))
            {
                P("_EFRimColor");
                P("_EFRimStrength");
                P("_EFRimWidth");
                P("_EFRimAlbedo");
                P("_EFLightRim");
                P("_EFRimDepth");
                var rimDepth = Prop("_EFRimDepth");
                if (rimDepth != null && (rimDepth.hasMixedValue || Number(rimDepth) > 0f))
                {
                    P("_EFRimDepthWidth");
                    P("_EFRimDepthGap");
                }
                EndSection();
            }
        }

        private void NormalMaps()
        {

            bool firstNormal = SearchAllows("_UseBumpMap", "_BumpMap", "_BumpScale", "_BumpMap|Normal Map", "_Bump2ndMap|Normal Map", "_Bump2ndScale", "_Bump2ndScaleMask|Scale Mask");
            bool secondNormal = SearchAllows("_UseBump2ndMap", "_Bump2ndMap", "_Bump2ndScale", "_Bump2ndMap_UVMode", "_Bump2ndScaleMask", "|ft.Tiling", "|ft.Offset");
            if (!firstNormal && !secondNormal) return;
            if (firstNormal && BeginSection("Normal", "sNormalMapSetting", Prop("_UseBumpMap"), false, PropertyBlock.NormalMap1st))
            {
                Tex("_BumpMap", "_BumpScale", "Normal Map");
                ScaleOffset("_BumpMap");
                if (Prop("_BumpMap").textureValue == null && !Prop("_BumpMap").hasMixedValue)
                Help("No normal map assigned: the keyword stays off.", MessageType.None);
                EndSection();
            }

            if (secondNormal && BeginSection("Normal2nd", "sNormalMap2nd", Prop("_UseBump2ndMap"), false, PropertyBlock.NormalMap2nd))
            {
            Tex("_Bump2ndMap", "_Bump2ndScale", "Normal Map");
            P("_Bump2ndMap_UVMode");
            Tex("_Bump2ndScaleMask", null, "Scale Mask");
            ScaleOffset("_Bump2ndMap");
            ScaleOffset("_Bump2ndScaleMask");

            if (Prop("_Bump2ndMap").textureValue == null && !Prop("_Bump2ndMap").hasMixedValue)
            Help("No second normal map assigned: the keyword stays off.", MessageType.None);
            EndSection();
            }
        }

        private void Anisotropy()
        {

            if (!SearchAllows("_UseAnisotropy", "_AnisotropyTangentMap", "_AnisotropyScale", "_AnisotropyScaleMask", "_AnisotropyTangentWidth", "_AnisotropyBitangentWidth", "_AnisotropyShift", "_AnisotropyShiftNoiseScale", "_AnisotropySpecularStrength", "_Anisotropy2ndTangentWidth", "_Anisotropy2ndBitangentWidth", "_Anisotropy2ndShift", "_Anisotropy2ndShiftNoiseScale", "_Anisotropy2ndSpecularStrength", "_AnisotropyShiftNoiseMask", "_Anisotropy2Reflection", "_Anisotropy2MatCap", "_Anisotropy2MatCap2nd", "_AnisotropyTangentMap|Tangent Map", "_AnisotropyScaleMask|Scale Mask", "_AnisotropyShiftNoiseMask|Shift Noise")) return;
            if (!BeginSection("Anisotropy", "sAnisotropy", Prop("_UseAnisotropy"), false, PropertyBlock.Anisotropy)) return;
            Tex("_AnisotropyTangentMap", "_AnisotropyScale", "Tangent Map");
            Tex("_AnisotropyScaleMask", null, "Scale Mask");
            ScaleOffset("_AnisotropyTangentMap");
            ScaleOffset("_AnisotropyScaleMask");
            SubHeader("1st Highlight");
            P("_AnisotropyTangentWidth");
            P("_AnisotropyBitangentWidth");
            P("_AnisotropyShift");
            P("_AnisotropyShiftNoiseScale");
            P("_AnisotropySpecularStrength");
            SubHeader("2nd Highlight");
            P("_Anisotropy2ndTangentWidth");
            P("_Anisotropy2ndBitangentWidth");
            P("_Anisotropy2ndShift");
            P("_Anisotropy2ndShiftNoiseScale");
            P("_Anisotropy2ndSpecularStrength");
            Tex("_AnisotropyShiftNoiseMask", null, "Shift Noise");
            ScaleOffset("_AnisotropyShiftNoiseMask");
            P("_Anisotropy2Reflection");
            P("_Anisotropy2MatCap");
            P("_Anisotropy2MatCap2nd");

            EndSection();
        }

        private void Backlight()
        {

            if (!SearchAllows("_UseBacklight", "_BacklightColorTex", "_BacklightColor", "_BacklightBorder", "_BacklightBlur", "_BacklightDirectivity", "_BacklightViewStrength", "_BacklightMainStrength", "_BacklightNormalStrength", "_BacklightReceiveShadow", "_BacklightBackfaceMask", "_BacklightColorTex|Color")) return;
            if (!BeginSection("Backlight", "sBacklightSetting", Prop("_UseBacklight"), false, PropertyBlock.Backlight)) return;
            Tex("_BacklightColorTex", "_BacklightColor", "Color");
            ScaleOffset("_BacklightColorTex");
            P("_BacklightBorder");
            P("_BacklightBlur");
            P("_BacklightDirectivity");
            P("_BacklightViewStrength");
            P("_BacklightMainStrength");
            P("_BacklightNormalStrength");
            P("_BacklightReceiveShadow");
            P("_BacklightBackfaceMask");

            EndSection();
        }

        private void SheerFabric()
        {

            if (!SearchAllows("_SheerAmount", "_SheerColor", "_SheerPower", "_SheerAlpha", "_SheerAmount|Sheer|Thin fabric such as stockings: the skin colour shows where the fabric faces the view.", "_SheerColor|Skin Color|Skin seen through the fabric (already dimmed by it).", "_SheerPower|Edge Falloff|Higher keeps the skin to the part facing the view; the edges show the fabric colour.", "_SheerAlpha|Min Alpha|Transparent materials: alpha where the fabric faces the view (most see-through); the edges stay opaque.")) return;
            if (!BeginSection("Sheer", "ft.Sheer", null, false, PropertyBlock.Sheer)) return;
            P("_SheerAmount", "Sheer", "Thin fabric such as stockings: the skin colour shows where the fabric faces the view.");
            var amount = Prop("_SheerAmount");
            if (amount != null && (Number(amount) > 0f || amount.hasMixedValue))
            {
                P("_SheerColor", "Skin Color", "Skin seen through the fabric (already dimmed by it).");
                P("_SheerPower", "Edge Falloff", "Higher keeps the skin to the part facing the view; the edges show the fabric colour.");
                P("_SheerAlpha", "Min Alpha", "Transparent materials: alpha where the fabric faces the view (most see-through); the edges stay opaque.");
            }
            EndSection();
        }

        private void Wetness(bool endfield)
        {

            if (!SearchAllows("_UseWet", "_WetPorosity", "_WetDarken", "_WetSaturation", "_WetHaze", "_WetSpecular", "_WetEdgeDarken", "_WetEdgeNoise", "_WetStreakAmount", "_WetStreakScale", "_WetStreakBump", "_WetStreakAxis", "_WetStreakDarken", "_WetStreakFlow", "_WetFilm", "_WetGlintAmount", "_WetSmoothness", "_WetFilmStretch", "_WetDropAmount", "_WetDropScale", "_WetPorosity|Porosity|Higher values soak and darken more; use low values for skin and metal.", "_WetDarken|Darken|Overall wet darkening, scaled by porosity.", "_WetSaturation|Saturation|Colour saturation of the wet area.", "_WetHaze|Dry Haze Loss|How much of the dry MatCap / rim sheen soaked fabric loses (what makes wet fabric read darker).", "_WetSpecular|Wet Specular|lilToon mode multiplier for the glints and droplets.", "_WetEdgeDarken|Front Darken|Dark band along the soaking edge.", "_WetEdgeNoise|Front Noise (m)|UV-stable height variation along the soaking edge.", "_WetStreakAmount|Coverage|Share of trail columns that are wet; columns cluster in groups.", "_WetStreakScale|Column Spacing (m)|Spacing of the trails around each limb, in metres.", "_WetStreakBump|Width|Trail width multiplier (1 = 1.5-6 mm).", "_WetStreakAxis|Wander|How much the trails meander as they run down (0 = straight).", "_WetStreakDarken|Darken / Opacity|How much darker and less sheer a soaked trail is.", "_WetStreakFlow|Fresh Flow (m/s)|Trails keep growing downwards for a few seconds after leaving water; rain keeps them running.", "_WetFilm|Glint Strength|Brightness of glints and droplets, in both shading modes.", "_WetGlintAmount|Glint Amount|Share of trail stretches still holding free water.", "_WetSmoothness|Glint Sharpness|Higher = thinner, sharper glints.", "_WetFilmStretch|Glint Stretch|Stretches glints along the limb.", "_WetDropAmount|Droplet Chance|Chance of a droplet per 9 mm of trail.", "_WetDropScale|Droplet Size (m)|Largest droplet radius in metres; the smallest are 40% of it.")) return;
            var toggle = Prop("_UseWet");
            if (toggle == null || !BeginSection("Wetness", "ft.Wetness", toggle, false, PropertyBlock.Wetness)) return;
            P("_WetPorosity", "Porosity", "Higher values soak and darken more; use low values for skin and metal.");
            P("_WetDarken", "Darken", "Overall wet darkening, scaled by porosity.");
            P("_WetSaturation", "Saturation", "Colour saturation of the wet area.");
            P("_WetHaze", "Dry Haze Loss", "How much of the dry MatCap / rim sheen soaked fabric loses (what makes wet fabric read darker).");
            if (!endfield) P("_WetSpecular", "Wet Specular", "lilToon mode multiplier for the glints and droplets.");
            P("_WetEdgeDarken", "Front Darken", "Dark band along the soaking edge.");
            P("_WetEdgeNoise", "Front Noise (m)", "UV-stable height variation along the soaking edge.");
            string detailsKey = PrefsPrefix + "Foldout.WetDetails";
            bool details = EditorGUILayout.Foldout(EditorPrefs.GetBool(detailsKey, false), FairyToonL10n.Text("Wet details"), true);
            EditorPrefs.SetBool(detailsKey, details);
            if (details)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField(FairyToonL10n.Text("Trails (soaked streaks along each limb bone)"), EditorStyles.miniBoldLabel);
                    P("_WetStreakAmount", "Coverage", "Share of trail columns that are wet; columns cluster in groups.");
                    P("_WetStreakScale", "Column Spacing (m)", "Spacing of the trails around each limb, in metres.");
                    P("_WetStreakBump", "Width", "Trail width multiplier (1 = 1.5-6 mm).");
                    P("_WetStreakAxis", "Wander", "How much the trails meander as they run down (0 = straight).");
                    P("_WetStreakDarken", "Darken / Opacity", "How much darker and less sheer a soaked trail is.");
                    P("_WetStreakFlow", "Fresh Flow (m/s)", "Trails keep growing downwards for a few seconds after leaving water; rain keeps them running.");
                    EditorGUILayout.LabelField(FairyToonL10n.Text("Glints (free water on the trails)"), EditorStyles.miniBoldLabel);
                    P("_WetFilm", "Glint Strength", "Brightness of glints and droplets, in both shading modes.");
                    P("_WetGlintAmount", "Glint Amount", "Share of trail stretches still holding free water.");
                    P("_WetSmoothness", "Glint Sharpness", "Higher = thinner, sharper glints.");
                    P("_WetFilmStretch", "Glint Stretch", "Stretches glints along the limb.");
                    EditorGUILayout.LabelField(FairyToonL10n.Text("Droplets"), EditorStyles.miniBoldLabel);
                    P("_WetDropAmount", "Droplet Chance", "Chance of a droplet per 9 mm of trail.");
                    P("_WetDropScale", "Droplet Size (m)", "Largest droplet radius in metres; the smallest are 40% of it.");
                }
            }
            EndSection();
        }

        private void Reflection()
        {

            if (!SearchAllows("_UseReflection", "_SmoothnessTex", "_Smoothness", "_MetallicGlossMap", "_Metallic", "_Reflectance", "_GSAAStrength", "_ApplySpecular", "_SpecularToon", "_SpecularBorder", "_SpecularBlur", "_SpecularNormalStrength", "_ApplyReflection", "_ReflectionNormalStrength", "_ReflectionCubeTex", "_ReflectionCubeColor", "_ReflectionCubeEnableLighting", "_ReflectionCubeOverride", "_ReflectionColorTex", "_ReflectionColor", "_ReflectionBlendMode", "_ReflectionApplyTransparency", "_SmoothnessTex|Smoothness", "_MetallicGlossMap|Metallic", "_ApplySpecular|Specular", "_SpecularToon|Toon", "_SpecularBorder|Border", "_SpecularBlur|Blur", "_SpecularNormalStrength|Normal Strength", "_ApplyReflection|sApplyReflection", "_ReflectionNormalStrength|Normal Strength", "_ReflectionCubeTex|Cubemap Fallback", "_ReflectionCubeEnableLighting|Cubemap Enable Lighting", "_ReflectionColorTex|Color")) return;
            if (!BeginSection("Reflection", "sReflection", Prop("_UseReflection"), false, PropertyBlock.Reflection)) return;
            Tex("_SmoothnessTex", "_Smoothness", "Smoothness");
            ScaleOffset("_SmoothnessTex");
            Tex("_MetallicGlossMap", "_Metallic", "Metallic");
            ScaleOffset("_MetallicGlossMap");
            P("_Reflectance");
            P("_GSAAStrength");

            ToggleRow("_ApplySpecular", "Specular");
            var applySpecular = Prop("_ApplySpecular");
            if (applySpecular != null && (applySpecular.hasMixedValue || Number(applySpecular) > 0.5f))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    P("_SpecularToon", "Toon");
                    P("_SpecularBorder", "Border");
                    P("_SpecularBlur", "Blur");
                    P("_SpecularNormalStrength", "Normal Strength");
                }
            }
            ToggleRow("_ApplyReflection", "sApplyReflection");
            var applyReflection = Prop("_ApplyReflection");
            if (applyReflection != null && (applyReflection.hasMixedValue || Number(applyReflection) > 0.5f))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    P("_ReflectionNormalStrength", "Normal Strength");
                    Tex("_ReflectionCubeTex", "_ReflectionCubeColor", "Cubemap Fallback");
                    P("_ReflectionCubeEnableLighting", "Cubemap Enable Lighting");
                    P("_ReflectionCubeOverride");
                }
            }
            SubHeader("Color");
            Tex("_ReflectionColorTex", "_ReflectionColor", "Color");
            ScaleOffset("_ReflectionColorTex");
            P("_ReflectionBlendMode");
            P("_ReflectionApplyTransparency");

            EndSection();
        }

        private void MatCap()
        {

            bool firstCap = PrefixMatches("_MatCap", "_MatCap2nd") || SearchAllows("_UseMatCap");
            bool secondCap = PrefixMatches("_MatCap2nd") || SearchAllows("_UseMatCap2nd");
            if (!firstCap && !secondCap) return;
            if (firstCap && BeginSection("MatCap", "sMatCap", Prop("_UseMatCap"), false, PropertyBlock.MatCap1st, false))
            {
                MatCapLayer("");
                EndSection();
            }
            // (2nd works without the 1st: either one turns on _FT_MATCAP)
            if (secondCap && BeginSection("MatCap2nd", "sMatCap2nd", Prop("_UseMatCap2nd"), false, PropertyBlock.MatCap2nd, false))
            {
                MatCapLayer("2nd");
                EndSection();
            }
        }

        // (layer "" = 1st, "2nd" = 2nd; the property sets are identical apart from the infix)
        private void MatCapLayer(string layer)
        {
            string n = "_MatCap" + layer;
            Tex(n + "Tex", n + "Color", "sMatCap");
            ColorAlpha(n + "Color");
            P(n + "Blend", "sMatCapBlend");
            P(n + "MainStrength", "sStrengthFromMain");
            P(n + "NormalStrength", "sNormalStrength");
            Tex(n + "BlendMask", null, "sMask");
            ScaleOffset(n + "BlendMask");
            P(n + "EnableLighting", "sEnableLighting");
            P(n + "ShadowMask", "sShadowMask");
            P(n + "BackfaceMask", "sBackfaceMask");
            P(n + "Lod", "sBlur");
            P(n + "BlendMode", "sBlendMode");
            if (PrimaryMode() != FairyToonMaterialSetup.Opaque) P(n + "ApplyTransparency", "sApplyTransparency");
            P(n + "CustomNormal", "sCustomNormal");
            if (Prop(n + "CustomNormal") != null && (Number(Prop(n + "CustomNormal")) > 0.5f || Prop(n + "CustomNormal").hasMixedValue))
            {
                Tex(n + "BumpMap", n + "BumpScale", "sNormalMap");
                ScaleOffset(n + "BumpMap");
            }
            string uvKey = PrefsPrefix + "Foldout.MatCapUV" + layer;
            bool uvOpen = EditorGUILayout.Foldout(EditorPrefs.GetBool(uvKey, false), "UV", true);
            if (uvOpen != EditorPrefs.GetBool(uvKey, false)) EditorPrefs.SetBool(uvKey, uvOpen);
            if (uvOpen || Searching)
            {
                ScaleOffset(n + "Tex");
                P(n + "ZRotCancel", "sZRotCancel");
                P(n + "Perspective", "sFixPerspective");
            }
        }

        private void RimLight()
        {

            if (!SearchAllows("_UseRim", "_RimColorTex", "_RimColor", "_RimBorder", "_RimBlur", "_RimFresnelPower", "_RimShadowMask", "_RimMainStrength", "_RimNormalStrength", "_RimEnableLighting", "_RimBackfaceMask", "_RimApplyTransparency", "_RimBlendMode", "_RimDirStrength", "_RimDirRange", "_RimIndirRange", "_RimIndirColor", "_RimIndirBorder", "_RimIndirBlur", "_RimColorTex|Color")) return;
            if (!BeginSection("Rim", "sRimLightSetting", Prop("_UseRim"), false, PropertyBlock.RimLight)) return;
            Tex("_RimColorTex", "_RimColor", "Color / Mask");
            ColorAlpha("_RimColor");
            P("_RimMainStrength", "sStrengthFromMain");
            P("_RimEnableLighting", "sEnableLighting");
            P("_RimShadowMask", "sShadowMask");
            P("_RimBackfaceMask", "sBackfaceMask");
            P("_RimBlendMode", "sBlendMode");
            EditorGUILayout.Space(2f);
            P("_RimDirStrength", "sLightDirection");
            InvBorder("_RimBorder");
            P("_RimBlur", "sBlur");
            P("_RimNormalStrength", "sNormalStrength");
            P("_RimFresnelPower", "sFresnel");
            if (Float("_RimDirStrength") > 0f || Prop("_RimDirStrength").hasMixedValue)
            {
                P("_RimDirRange");
                P("_RimIndirRange");
                P("_RimIndirColor");
                InvBorder("_RimIndirBorder");
                P("_RimIndirBlur");
            }
            if (PrimaryMode() != FairyToonMaterialSetup.Opaque)
                P("_RimApplyTransparency", "sApplyTransparency");

            EndSection();
        }

        private void RimShade()
        {

            if (!SearchAllows("_UseRimShade", "_RimShadeMask", "_RimShadeColor", "_RimShadeBorder", "_RimShadeBlur", "_RimShadeFresnelPower", "_RimShadeNormalStrength", "_RimShadeMask|Color / Mask")) return;
            if (!BeginSection("RimShade", "sRimShadeSetting", Prop("_UseRimShade"), false, PropertyBlock.RimShade)) return;
            Tex("_RimShadeMask", "_RimShadeColor", "Color / Mask");
            P("_RimShadeNormalStrength", "sNormalStrength");
            P("_RimShadeBorder", "sBorder");
            P("_RimShadeBlur", "sBlur");
            P("_RimShadeFresnelPower", "sFresnelPower");
            EndSection();
        }

        private void Glitter()
        {

            if (!SearchAllows("_UseGlitter", "_GlitterUVMode", "_GlitterColorTex", "_GlitterColor", "_GlitterColorTex_UVMode", "_GlitterParams1", "_GlitterParams2", "_GlitterScaleRandomize", "_GlitterSensitivity", "_GlitterPostContrast", "_GlitterMainStrength", "_GlitterNormalStrength", "_GlitterEnableLighting", "_GlitterShadowMask", "_GlitterBackfaceMask", "_GlitterApplyTransparency", "_GlitterVRParallaxStrength", "_GlitterApplyShape", "_GlitterShapeTex", "_GlitterAtras", "_GlitterAngleRandomize", "_GlitterColorTex|Color", "_GlitterParams1|Tiling XY / Size Squared Z / Contrast W", "_GlitterParams2|Speed X / Angle Y / Light Direction Z / Color Randomness W", "_GlitterApplyShape|Particle Shape", "_GlitterShapeTex|Shape", "_GlitterAtras|Shape Atlas (columns X / rows Y)")) return;
            if (!BeginSection("Glitter", "sGlitterSetting", Prop("_UseGlitter"), false, PropertyBlock.Glitter)) return;
            P("_GlitterUVMode");
            Tex("_GlitterColorTex", "_GlitterColor", "Color");
            P("_GlitterColorTex_UVMode");
            P("_GlitterParams1", "Tiling XY / Size Squared Z / Contrast W");
            P("_GlitterParams2", "Speed X / Angle Y / Light Direction Z / Color Randomness W");
            P("_GlitterScaleRandomize");
            P("_GlitterSensitivity");
            P("_GlitterPostContrast");
            ScaleOffset("_GlitterColorTex");
            P("_GlitterMainStrength");
            P("_GlitterNormalStrength");
            P("_GlitterEnableLighting");
            P("_GlitterShadowMask");
            P("_GlitterBackfaceMask");
            P("_GlitterApplyTransparency");
            P("_GlitterVRParallaxStrength");
            ToggleRow("_GlitterApplyShape", "Particle Shape");
            if (Float("_GlitterApplyShape") > 0.5f || Prop("_GlitterApplyShape").hasMixedValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    Tex("_GlitterShapeTex", null, "Shape");
                    ScaleOffset("_GlitterShapeTex");
                    P("_GlitterAtras", "Shape Atlas (columns X / rows Y)");
                    P("_GlitterAngleRandomize");
                }
            }

            EndSection();
        }

        private void Outline()
        {

            if (!SearchAllows("_UseOutline", "_ShadingModel", "_EFOutlineColor", "_EFOutlineSaturation", "_EFOutlineEdge", "_EFOutlineFlatten", "_EFOutlinePull", "_OutlineTex", "_OutlineColor", "_OutlineMode", "_OutlineWidthMask", "_OutlinePixelWidth", "_OutlineMinPixels", "_OutlineFadeStart", "_OutlineFadeEnd", "_OutlineWidth", "_OutlineFixWidth", "_OutlineVertexR2Width", "_OutlineCornerTaper", "_OutlineZBias", "_OutlineEnableLighting", "_OutlineCull", "_EFOutlineColor|Tint (x local shadow colour)", "_EFOutlineSaturation|Saturation", "_EFOutlineEdge|Edge Tint (A strength)", "_EFOutlineFlatten|Flatten (towards mid grey)", "_EFOutlinePull|Tint Pull (A amount)", "_OutlineTex|Color", "_OutlineWidthMask|Width (px at 1080p)", "_OutlineWidthMask|Width (cm)")) return;
            if (!BeginSection("Outline", "sOutlineSetting", Prop("_UseOutline"), false, PropertyBlock.Outline)) return;
            bool mixedShade = Prop("_ShadingModel") != null && Prop("_ShadingModel").hasMixedValue;
            bool endfield = mixedShade || Float("_ShadingModel") > 0.5f;
            bool toonOutline = mixedShade || Float("_ShadingModel") <= 0.5f;
            if (endfield)
            {
                P("_EFOutlineColor", "Tint (x local shadow colour)");
                P("_EFOutlineSaturation", "Saturation");
                P("_EFOutlineEdge", "Edge Tint (A strength)");
                P("_EFOutlineFlatten", "Flatten (towards mid grey)");
                P("_EFOutlinePull", "Tint Pull (A amount)");
            }
            if (toonOutline)
            {
                Tex("_OutlineTex", "_OutlineColor", "sColor");
                ColorAlpha("_OutlineColor");
                P("_OutlineEnableLighting", "sEnableLighting");
            }
            bool mixedOutline = Prop("_OutlineMode") != null && Prop("_OutlineMode").hasMixedValue;
            bool screenWidth = mixedOutline || Float("_OutlineMode") > 0.5f;
            if (screenWidth)
                Tex("_OutlineWidthMask", "_OutlinePixelWidth", "sMaskWidth");
            if (mixedOutline || Float("_OutlineMode") <= 0.5f)
                Tex("_OutlineWidthMask", "_OutlineWidth", "sMaskWidth");
            P("_OutlineFixWidth", "sFixWidth");
            P("_OutlineVertexR2Width", "sVertexColor");
            P("_OutlineZBias", "sZBias");
            if (toonOutline) ScaleOffset("_OutlineTex");
            P("_OutlineCull");
            P("_OutlineMode");
            if (screenWidth)
            {
                P("_OutlineMinPixels");
                P("_OutlineFadeStart");
                P("_OutlineFadeEnd");
            }
            var vertexWidth = Prop("_OutlineVertexR2Width");
            if (vertexWidth != null && (vertexWidth.hasMixedValue || Mathf.Approximately(Number(vertexWidth), 3f)))
            {
                P("_OutlineCornerTaper");
                Help("Reads a tangent-space smoothed normal from UV7 (W: coherence, low at hard corners). " +
                "Bake it with Assets > FairyToon > Bake Smooth Normals (UV7) on the model.", MessageType.None);
            }

            EndSection();
        }

        private void Refraction()
        {

            if (!SearchAllows("_RefractionStrength", "_RefractionFresnelPower", "_RefractionColorFromMain", "_RefractionColor", "_RefractionBlur", "_SmoothnessTex", "_Smoothness", "_RefractionStrength|Strength", "_RefractionFresnelPower|Fresnel Power", "_RefractionColorFromMain|sColorFromMain", "_RefractionColor|Color", "_RefractionBlur|Blur", "_SmoothnessTex|Blur Smoothness")) return;
            if (!BeginSection("Refraction", "sRefractionSetting", null, true, PropertyBlock.Refraction)) return;
            EditorGUILayout.LabelField(FairyToonL10n.Text("sRefraction"));
            P("_RefractionStrength", "sStrength");
            P("_RefractionFresnelPower", "sFresnel");
            P("_RefractionColorFromMain", "sColorFromMain");
            P("_RefractionColor", "Color");
            P("_RefractionBlur", "Blur");
            if (Float("_RefractionBlur") > 0.5f || Prop("_RefractionBlur")?.hasMixedValue == true)
            {
                Tex("_SmoothnessTex", "_Smoothness", "Blur Smoothness");
                ScaleOffset("_SmoothnessTex");
            }
            EndSection();
        }

        private void Fur()
        {
            var furMode = Prop("_FurMode");
            bool cutoutLength = furMode != null && (furMode.hasMixedValue || Number(furMode) > 1.5f);
            bool furRows = SearchAllows("_FurMode|ft.FurMode", "_FurNoiseMask", "_FurMask", "_FurLengthMask", "_FurVectorTex", "_FurVectorScale", "_FurVector", "_VertexColor2FurVector", "_FurGravity", "_FurRandomize", "_FurAO", "_FurLayerNum", "_FurRootOffset", "_FurRimColor", "_FurRimFresnelPower", "_FurRimAntiLight", "_FurVectorTex|sVectorTex", "_FurLengthMask|sLengthMask", "_FurVector|sFurVector", "_VertexColor2FurVector|sVertexColor2Vector", "_FurGravity|sGravity", "_FurRandomize|sRandomize", "_FurNoiseMask|sNoise", "_FurMask|sMask", "_FurAO|sAmbientOcclusion", "_FurLayerNum|sLayerNum", "_FurRootOffset|sRootWidth", "_FurCutoutLength|sCutoutLength", "_FurRimColor|Color", "_FurRimFresnelPower|sFresnelPower", "_FurRimAntiLight|Anti Light");
            if (!furRows && !(cutoutLength && SearchAllows("_FurCutoutLength"))) return;
            if (!BeginSection("Fur", "sFurSetting", null, true, PropertyBlock.Fur)) return;
            FurMode();
            Help("Fur does not render Outline or See-through Hair. Their settings are kept when switching modes.", MessageType.None);
            Tex("_FurVectorTex", "_FurVectorScale", "sNormalMap");
            Tex("_FurLengthMask", null, "sLengthMask");
            FurVectorAndLength();
            if (cutoutLength)
                P("_FurCutoutLength", "sCutoutLength");
            P("_VertexColor2FurVector", "sVertexColor2Vector");
            P("_FurGravity", "sGravity");
            P("_FurRandomize", "sRandomize");
            Tex("_FurNoiseMask", null, "sNoise");
            ScaleOffset("_FurNoiseMask");
            Tex("_FurMask", null, "sMask");
            P("_FurAO", "sAmbientOcclusion");
            P("_FurLayerNum", "sLayerNum");
            MinusRange("_FurRootOffset", "sRootWidth");
            SubHeader("sRimLight");
            P("_FurRimColor", "Color");
            P("_FurRimFresnelPower", "Fresnel Power");
            P("_FurRimAntiLight", "Anti Light");
            EndSection();
        }

        private void Gem()
        {

            if (!SearchAllows("_RefractionStrength", "_RefractionFresnelPower", "_GemChromaticAberration", "_GemEnvContrast", "_GemEnvColor", "_GemParticleLoop", "_GemParticleColor", "_GemVRParallaxStrength", "_SmoothnessTex", "_Smoothness", "_Reflectance", "_ReflectionCubeOverride", "_ReflectionCubeTex", "_RefractionStrength|Refraction Strength", "_RefractionFresnelPower|Refraction Fresnel Power", "_GemChromaticAberration|Chromatic Aberration", "_GemEnvContrast|Environment Contrast", "_GemEnvColor|Environment Color", "_GemParticleLoop|Particle Loop", "_GemParticleColor|Particle Color", "_GemVRParallaxStrength|VR Parallax Strength", "_SmoothnessTex|Smoothness", "_Reflectance|Reflectance", "_ReflectionCubeOverride|Override Cubemap", "_ReflectionCubeTex|Reflection Cubemap")) return;
            if (!BeginSection("Gem", "sGemSetting", null, true, PropertyBlock.Gem)) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(FairyToonL10n.Text("sRefraction"));
            P("_RefractionStrength", "sStrength");
            P("_RefractionFresnelPower", "sFresnel");
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(FairyToonL10n.Text("sGem"));
            P("_GemChromaticAberration", "sChromaticAberration");
            P("_GemEnvContrast", "sContrast");
            P("_GemEnvColor", "sEnvironmentColor");
            P("_GemParticleLoop", "sParticleLoop");
            P("_GemParticleColor", "sColor");
            P("_GemVRParallaxStrength", "sVRParallaxStrength");
            Tex("_SmoothnessTex", "_Smoothness", "sSmoothness");
            P("_Reflectance", "sReflectance");
            Tex("_ReflectionCubeTex", "_ReflectionCubeColor", "sCubemapFallback");
            P("_ReflectionCubeOverride", "sOverride");
            P("_ReflectionCubeEnableLighting", "sFallbackLighting");
            EditorGUILayout.EndVertical();
            EndSection();
        }

        private void SeeThrough()
        {

            if (!SearchAllows("_SeeThroughRole", "_SeeThroughMask", "_SeeThroughMaskChannel", "_SeeThroughMaskCutoff", "_TransparentMode", "_SeeThroughOpacity", "_SeeThroughEarlyZ", "_SeeThroughFade", "_SeeThroughFadeSideAlpha", "_SeeThroughFadeHorizontalMin", "_SeeThroughFadeHorizontalMax", "_SeeThroughFadeVerticalMin", "_SeeThroughFadeVerticalMax", "_UseFakeShadow", "_FakeShadowColor", "_FakeShadowVector", "_SeeThroughRole|Role", "_SeeThroughMask|Eye / Brow Mask", "_SeeThroughMaskCutoff|Cutoff", "_SeeThroughOpacity|Opacity", "_SeeThroughEarlyZ|Early Z (front layer only)", "_SeeThroughFade|View Angle Fade", "_SeeThroughFadeSideAlpha|Side Opacity", "_SeeThroughFadeHorizontalMin|Horizontal Start (°)", "_SeeThroughFadeHorizontalMax|Horizontal End (°)", "_SeeThroughFadeVerticalMin|Vertical Start (°)", "_SeeThroughFadeVerticalMax|Vertical End (°)", "_UseFakeShadow|Fake Shadow", "_FakeShadowColor|Colour (multiply)", "_FakeShadowVector|Direction (xyz) / Offset (w, m)")) return;
            var role = Prop("_SeeThroughRole");
            if (role == null || !BeginSection("SeeThrough", "ft.SeeThrough", null, false, PropertyBlock.SeeThrough)) return;
            P("_SeeThroughRole", "Role");
            int r = role.hasMixedValue ? -1 : Mathf.RoundToInt(Number(role));
            if (r == FairyToonMaterialSetup.RoleNone)
            Help("Face: the eye / brow mask marks where the fringe turns see-through. Hair: the fringe drawn over that mask. Needs the FairyToon renderer feature and, for the fade, FairyToonSeeThroughHair on the avatar.", MessageType.None);
            if (r == FairyToonMaterialSetup.RoleFace || r < 0)
            {
                SubHeader("Face");
                Tex("_SeeThroughMask", "_SeeThroughMaskChannel", "Eye / Brow Mask");
                P("_SeeThroughMaskCutoff", "Cutoff");
                if (r == FairyToonMaterialSetup.RoleFace && m_Editor.targets.Length == 1) PaintButton((Material)m_Editor.target);
                var coverage = Prop("_TransparentMode");
                if (coverage != null && (coverage.hasMixedValue || Mathf.RoundToInt(Number(coverage)) == FairyToonMaterialSetup.Transparent))
                Help("A Transparent face draws after the hair, too late to cut the fringe: use Opaque or Cutout.", MessageType.Warning);
            }
            if (r == FairyToonMaterialSetup.RoleHair || r < 0)
            {
                SubHeader("Hair over the mask");
                P("_SeeThroughOpacity", "Opacity");
                P("_SeeThroughEarlyZ", "Early Z (front layer only)");
                P("_SeeThroughFade", "View Angle Fade");
                var fade = Prop("_SeeThroughFade");
                if (fade != null && (fade.hasMixedValue || Number(fade) > 0.5f))
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        P("_SeeThroughFadeSideAlpha", "Side Opacity");
                        P("_SeeThroughFadeHorizontalMin", "Horizontal Start (°)");
                        P("_SeeThroughFadeHorizontalMax", "Horizontal End (°)");
                        P("_SeeThroughFadeVerticalMin", "Vertical Start (°)");
                        P("_SeeThroughFadeVerticalMax", "Vertical End (°)");

                    }
                }
                SubHeader("Fake shadow on the face");
                P("_UseFakeShadow", "Fake Shadow");
                var fakeShadow = Prop("_UseFakeShadow");
                if (fakeShadow != null && (fakeShadow.hasMixedValue || Number(fakeShadow) > 0.5f))
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        P("_FakeShadowColor", "Colour (multiply)");
                        P("_FakeShadowVector", "Direction (xyz) / Offset (w, m)");
                    }
                }
            }
            if (r != FairyToonMaterialSetup.RoleNone && !FairyToonSeeThroughSetup.FeatureInstalled())
            {
                Help("The active URP renderer has no FairyToon renderer feature: the see-through fringe, early Z and fake shadow are not drawn.", MessageType.Warning);
                if (GUILayout.Button(FairyToonL10n.Text("Add FairyToon Feature to the URP Renderer"))) FairyToonSeeThroughSetup.InstallFeature();
            }
            EndSection();
        }

        private static void PaintButton(Material material)
        {
            var renderers = FairyToonSeeThroughSetup.RenderersUsing(material);
            using (new EditorGUI.DisabledScope(renderers.Count == 0))
            {
                if (GUILayout.Button(FairyToonL10n.Text(renderers.Count == 0 ? "Paint Mask (no renderer in the open scenes uses this material)" : "Paint Mask in the Scene View")))
                {
                    var (renderer, slot) = renderers[0];
                    FairyToonMaskPainter.Open(renderer, slot, "_SeeThroughMask", Mathf.RoundToInt(material.GetFloat("_SeeThroughMaskChannel")));
                }
            }
        }

        private void Stencil()
        {
            bool body = SearchAllows("_StencilRef", "_StencilReadMask", "_StencilWriteMask", "_StencilComp", "_StencilPass", "_StencilFail", "_StencilZFail");
            bool fur = SelectedRenderingMode(FairyToonMaterialSetup.Fur) && SearchAllows("_FurStencilRef", "_FurStencilReadMask", "_FurStencilWriteMask", "_FurStencilComp", "_FurStencilPass", "_FurStencilFail", "_FurStencilZFail");
            if (!body && !fur) return;
            if (!BeginSection("Stencil", "sStencilSetting", null, false, PropertyBlock.Stencil)) return;
            var role = Prop("_SeeThroughRole");
            bool managed = !SelectedSpecialMode()
            && role != null && (role.hasMixedValue || Number(role) > 0.5f);
            if (managed) Help("Set by the See-through Hair role.", MessageType.None);
            using var locked = new EditorGUI.DisabledScope(managed);
            P("_StencilRef");
            P("_StencilReadMask");
            P("_StencilWriteMask");
            P("_StencilComp");
            P("_StencilPass");
            P("_StencilFail");
            P("_StencilZFail");
            if (SelectedRenderingMode(FairyToonMaterialSetup.Fur))
            {
                SubHeader("Fur Stencil");
                P("_FurStencilRef");
                P("_FurStencilReadMask");
                P("_FurStencilWriteMask");
                P("_FurStencilComp");
                P("_FurStencilPass");
                P("_FurStencilFail");
                P("_FurStencilZFail");
            }
            EndSection();
        }

        private void Rendering()
        {
            bool queue = SearchAllows("|sRenderingReset", "|Render Queue", "|Enable GPU Instancing", "|Double Sided Global Illumination",
                "|" + L10n.Tr("Render Queue"), "|" + L10n.Tr("Enable GPU Instancing"), "|" + L10n.Tr("Double Sided Global Illumination"),
                "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha", "_ZWrite", "_ZTest", "_OffsetFactor", "_OffsetUnits", "_Cull");
            bool fur = SelectedRenderingMode(FairyToonMaterialSetup.Fur) && SearchAllows("_FurCull", "_FurZClip", "_FurZTest", "_FurColorMask", "_FurOffsetFactor", "_FurOffsetUnits", "_FurBlendOp", "_FurBlendOpAlpha", "_FurSrcBlend", "_FurDstBlend", "_FurSrcBlendAlpha", "_FurDstBlendAlpha", "_FurZWrite", "_FurAlphaToMask");
            if (!queue && !fur) return;
            if (!BeginSection("Rendering", "sRenderingSetting", null, false, PropertyBlock.Rendering)) return;
            if ((!Searching || LabelOnly("sRenderingReset")) && GUILayout.Button(FairyToonL10n.Text("sRenderingReset")))
            {
                Undo.RecordObjects(m_Editor.targets, "FairyToon Reset Rendering");
                foreach (Object target in m_Editor.targets)
                {
                    var material = target as Material;
                    if (material == null) continue;
                    FairyToonMaterialSetup.ResetRendering(material);
                }
                m_Props = MaterialEditor.GetMaterialProperties(m_Editor.targets);
            }
            EditorGUILayout.BeginVertical(s_Box);
            EditorGUILayout.LabelField(FairyToonL10n.Text("sRendering"));
            P("_Cull");
            P("_ZWrite");
            P("_ZTest");
            P("_OffsetFactor");
            P("_OffsetUnits");
            if (AnyCoverage(FairyToonMaterialSetup.Cutout))
                P("_AlphaToMask", "Alpha To Coverage (hair)");
            // Native controls keep raw versus displayed queue, From Shader, deferred commit, variant lock,
            // and the instancing capability check. Search uses the same English labels Unity draws by default.
            if (UnityRowVisible("Render Queue")) m_Editor.RenderQueueField();
            if (UnityRowVisible("Enable GPU Instancing")) m_Editor.EnableInstancingField();
            if (UnityRowVisible("Double Sided Global Illumination")) m_Editor.DoubleSidedGIField();
            if (SelectedRenderingMode(FairyToonMaterialSetup.Fur))
            {
                SubHeader("Fur Rendering");
                P("_FurCull");
                P("_FurZClip");
                P("_FurZTest");
                P("_FurColorMask");
                P("_FurOffsetFactor");
                P("_FurOffsetUnits");
                P("_FurBlendOp");
                P("_FurBlendOpAlpha");
                SubHeader("Derived (set from Fur Mode)");
                using (new EditorGUI.DisabledScope(true))
                {
                    P("_FurSrcBlend");
                    P("_FurDstBlend");
                    P("_FurSrcBlendAlpha");
                    P("_FurDstBlendAlpha");
                    P("_FurZWrite");
                    P("_FurAlphaToMask");
                }
            }
            SubHeader("Derived (set from Rendering Mode)");
            using (new EditorGUI.DisabledScope(true))
            {
                P("_SrcBlend");
                P("_DstBlend");
                P("_SrcBlendAlpha");
                P("_DstBlendAlpha");
            }

            EditorGUILayout.EndVertical();
            EndSection();
        }

        // ---- Layout helpers ----

        private void RenderingMode()
        {
            if (!RowVisible("_TransparentMode", "sRenderingMode")) return;
            int mode = FairyToonMaterialSetup.GetRenderingMode(m_Editor.target as Material);
            bool mixed = false;
            foreach (Object target in m_Editor.targets)
            mixed |= FairyToonMaterialSetup.GetRenderingMode(target as Material) != mode;
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup(FairyToonL10n.Text("sRenderingMode"), mixed ? -1 : mode, new[]
            {
                FairyToonL10n.Text("sRenderingModeOpaque"),
                FairyToonL10n.Text("sRenderingModeCutout"),
                FairyToonL10n.Text("sRenderingModeTransparent"),
                FairyToonL10n.Text("ft.ModeRefraction"),
                FairyToonL10n.Text("ft.ModeFur"),
                FairyToonL10n.Text("ft.ModeGem")
            });
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            if (!changed) return;
            Undo.RecordObjects(m_Editor.targets, "FairyToon Rendering Mode");
            foreach (Object target in m_Editor.targets)
            {
                var material = target as Material;
                if (!FairyToonMaterialSetup.SetRenderingMode(material, selected))
                {
                    Debug.LogError("FairyToon shader was not found for rendering mode " + selected + ".", material);
                    continue;
                }
                EditorUtility.SetDirty(material);
            }
            // Refresh immediately: the rest of this draw must use the new shader's property descriptors.
            m_Props = MaterialEditor.GetMaterialProperties(m_Editor.targets);
            m_Editor.PropertiesChanged();
            GUIUtility.ExitGUI();
        }

        private void FurMode()
        {
            var property = Prop("_FurMode");
            if (property == null || !RowVisible("_FurMode", "ft.FurMode")) return;
            EditorGUI.showMixedValue = property.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup(FairyToonL10n.Text("ft.FurMode"), property.hasMixedValue ? -1 : Mathf.RoundToInt(Number(property)), new[]
            {
                FairyToonL10n.Text("sRenderingModeTransparent"),
                FairyToonL10n.Text("sRenderingModeCutout"),
                FairyToonL10n.Text("ft.FurTwoPass")
            });
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            if (!changed) return;
            Undo.RecordObjects(m_Editor.targets, "FairyToon Fur Mode");
            foreach (Object target in m_Editor.targets)
            {
                var material = target as Material;
                if (FairyToonMaterialSetup.SetFurMode(material, selected)) EditorUtility.SetDirty(material);
            }
            m_Props = MaterialEditor.GetMaterialProperties(m_Editor.targets);
            m_Editor.PropertiesChanged();
            GUIUtility.ExitGUI();
        }

        private bool AnyCoverage(params int[] modes)
        {
            foreach (Object target in m_Editor.targets)
            {
                int mode = FairyToonMaterialSetup.GetRenderingMode(target as Material);
                for (int i = 0; i < modes.Length; i++)
                    if (mode == modes[i]) return true;
            }
            return false;
        }

        private bool SelectedRenderingMode(int mode)
        {
            foreach (Object target in m_Editor.targets)
            if (FairyToonMaterialSetup.GetRenderingMode(target as Material) == mode) return true;
            return false;
        }

        private bool SelectedSpecialMode() => SelectedRenderingMode(FairyToonMaterialSetup.Refraction)
        || SelectedRenderingMode(FairyToonMaterialSetup.Fur) || SelectedRenderingMode(FairyToonMaterialSetup.Gem);

        /// <summary>Shuriken header with a foldout arrow, optional feature toggle, and lilToon's gear menu.</summary>
        /// <remarks>A section whose feature toggle is off stays closed, as in lilToon. The gear copies this section's properties.</remarks>
        private void BeginScope(Rect rect, MaterialProperty prop)
        {
            MaterialEditor.BeginProperty(rect, prop);
            m_Editor.BeginAnimatedCheck(rect, prop);
        }

        private void EndScope()
        {
            m_Editor.EndAnimatedCheck();
            MaterialEditor.EndProperty();
        }

        private void PropertyGearRow(string name, string label, PropertyBlock block)
        {
            var prop = Prop(name);
            if (prop == null || !RowVisible(name, label)) return;
            Rect row = EditorGUILayout.GetControlRect();
            Rect gear = new Rect(row.xMax - 22f, row.y, 20f, row.height);
            row.width = Mathf.Max(0f, row.width - 24f);
            string shown = FairyToonL10n.Text(label);
            if (!DrawLocalizedEnum(prop, shown, null, row))
                m_Editor.ShaderProperty(row, prop, shown);
            if (GUI.Button(gear, EditorGUIUtility.IconContent("_Popup"), s_Gear))
                FairyToonClipboard.ShowMenu(block, m_Editor, () => m_Props);
        }

        private void BeginGearRow(string titleKey, PropertyBlock block)
        {
            EditorGUILayout.BeginVertical(s_Box);
            Rect rect = GUILayoutUtility.GetRect(16f, 18f, GUILayout.ExpandWidth(true));
            Rect gear = new Rect(rect.xMax - 22f, rect.y, 20f, rect.height);
            GUI.Label(new Rect(rect.x, rect.y, Mathf.Max(0f, gear.x - rect.x - 2f), rect.height), FairyToonL10n.Text(titleKey), s_HeaderLabel);
            if (GUI.Button(gear, EditorGUIUtility.IconContent("_Popup"), s_Gear))
                FairyToonClipboard.ShowMenu(block, m_Editor, () => m_Props);
        }

        private static void EndGearRow() => EditorGUILayout.EndVertical();

        private bool BeginSection(string key, string title, MaterialProperty toggle = null, bool openByDefault = false,
            PropertyBlock? block = null, bool foldout = true)
        {
            string prefsKey = PrefsPrefix + "Foldout." + key;
            bool open = !foldout || EditorPrefs.GetBool(prefsKey, openByDefault);
            string shown = FairyToonL10n.Text(title);
            if (!foldout)
            {
                EditorGUILayout.BeginVertical(s_Box);
                if (toggle != null && !DrawFeatureToggle(shown, toggle, block))
                {
                    EditorGUILayout.EndVertical();
                    return false;
                }
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                m_InnerDepth++;
                return true;
            }
            EditorGUILayout.Space(2f);
            Rect rect = GUILayoutUtility.GetRect(16f, 22f, GUILayout.ExpandWidth(true));
            if (s_Foldout != null) GUI.Box(rect, GUIContent.none, s_Foldout);

            float x = rect.x + 4f;
            Rect arrow = new Rect(x, rect.y + 3f, 14f, 16f);
            x += 16f;
            Rect gear = new Rect(rect.xMax - 22f, rect.y + 1f, 20f, rect.height - 2f);
            GUI.Label(new Rect(x, rect.y, Mathf.Max(0f, gear.x - x - 2f), rect.height), shown, s_HeaderLabel);

            Event e = Event.current;
            if (block.HasValue && GUI.Button(gear, EditorGUIUtility.IconContent("_Popup"), s_Gear))
                FairyToonClipboard.ShowMenu(block.Value, m_Editor, () => m_Props);
            Rect click = rect;
            click.width = Mathf.Max(0f, click.width - 24f);
            if (e.type == EventType.MouseDown && e.button == 0 && click.Contains(e.mousePosition))
            {
                EditorPrefs.SetBool(prefsKey, open = !open);
                e.Use();
            }
            if (e.type == EventType.Repaint) EditorStyles.foldout.Draw(arrow, false, false, open, false);

            if (!open) return false;
            EditorGUILayout.BeginVertical(s_Box);
            if (toggle != null)
            {
                Rect row = EditorGUILayout.GetControlRect();
                Rect innerGear = new Rect(row.xMax - 22f, row.y, 20f, row.height);
                Rect toggleRect = new Rect(row.x + 2f, row.y, 16f, row.height);
                BeginScope(toggleRect, toggle);
                EditorGUI.showMixedValue = toggle.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                bool value = EditorGUI.Toggle(toggleRect, Number(toggle) > 0.5f);
                if (EditorGUI.EndChangeCheck()) SetNumber(toggle, value ? 1f : 0f);
                EditorGUI.showMixedValue = false;
                EndScope();
                GUI.Label(new Rect(toggleRect.xMax + 4f, row.y, innerGear.x - toggleRect.xMax - 8f, row.height), shown);
                if (block.HasValue && GUI.Button(innerGear, EditorGUIUtility.IconContent("_Popup"), s_Gear))
                    FairyToonClipboard.ShowMenu(block.Value, m_Editor, () => m_Props);
                if (!toggle.hasMixedValue && Number(toggle) <= 0.5f)
                {
                    EditorGUILayout.EndVertical();
                    return false;
                }
            }
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            m_InnerDepth++;
            return true;
        }

        private bool DrawFeatureToggle(string shown, MaterialProperty toggle, PropertyBlock? block)
        {
            Rect row = EditorGUILayout.GetControlRect();
            Rect innerGear = new Rect(row.xMax - 22f, row.y, 20f, row.height);
            Rect toggleRect = new Rect(row.x + 2f, row.y, 16f, row.height);
            BeginScope(toggleRect, toggle);
            EditorGUI.showMixedValue = toggle.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            bool value = EditorGUI.Toggle(toggleRect, Number(toggle) > 0.5f);
            if (EditorGUI.EndChangeCheck()) SetNumber(toggle, value ? 1f : 0f);
            EditorGUI.showMixedValue = false;
            EndScope();
            GUI.Label(new Rect(toggleRect.xMax + 4f, row.y, innerGear.x - toggleRect.xMax - 8f, row.height), shown);
            if (block.HasValue && GUI.Button(innerGear, EditorGUIUtility.IconContent("_Popup"), s_Gear))
                FairyToonClipboard.ShowMenu(block.Value, m_Editor, () => m_Props);
            return toggle.hasMixedValue || Number(toggle) > 0.5f;
        }

        private void EndSection()
        {
            if (m_InnerDepth > 0)
            {
                EditorGUILayout.EndVertical();
                m_InnerDepth--;
            }
            EditorGUILayout.EndVertical();
        }

        private void HsvgSliders(string name)
        {
            var property = Prop(name);
            if (property == null) return;
            HsvgAxis(property, 0, "sHue", -0.5f, 0.5f);
            HsvgAxis(property, 1, "sSaturation", 0f, 2f);
            HsvgAxis(property, 2, "sValue", 0f, 2f);
            HsvgAxis(property, 3, "sGamma", 0.01f, 2f);
        }

        private void HsvgAxis(MaterialProperty property, int component, string label, float min, float max)
        {
            if (!RowVisible(property.name, label)) return;
            Vector4 value = property.vectorValue;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            EditorGUI.showMixedValue = property.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            string shown = Event.current.alt ? property.name + "." + "xyzw"[component] : FairyToonL10n.Text(label);
            value[component] = EditorGUI.Slider(row, shown, value[component], min, max);
            if (EditorGUI.EndChangeCheck()) SetDissolveComponents(property, value, 1 << component);
            EditorGUI.showMixedValue = false;
            EndScope();
        }

        private void ScrollAndRotate(string name)
        {
            var property = Prop(name);
            if (property == null || !RowVisible(name, "sScroll")) return;
            Vector4 value = property.vectorValue;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            EditorGUI.BeginChangeCheck();
            Vector2 before = new Vector2(value.x, value.y);
            Vector2 scroll = EditorGUI.Vector2Field(row, FairyToonL10n.Text("sScroll"), before);
            if (EditorGUI.EndChangeCheck())
            {
                int mask = 0;
                if (!Mathf.Approximately(scroll.x, before.x)) { value.x = scroll.x; mask |= 1; }
                if (!Mathf.Approximately(scroll.y, before.y)) { value.y = scroll.y; mask |= 2; }
                if (mask != 0) SetDissolveComponents(property, value, mask);
            }
            EndScope();
            row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            EditorGUI.BeginChangeCheck();
            float rotate = value.w / (Mathf.PI * 2f);
            rotate = EditorGUI.FloatField(row, FairyToonL10n.Text("sRotate"), rotate);
            if (EditorGUI.EndChangeCheck())
            {
                value = property.vectorValue;
                value.w = rotate * Mathf.PI * 2f;
                SetDissolveComponents(property, value, 8);
            }
            EndScope();
            if (GUILayout.Button(FairyToonL10n.Text("sReset")))
                SetDissolveComponents(property, Vector4.zero, 15);
        }

        private void InvBorder(string name)
        {
            var property = Prop(name);
            if (property == null || !RowVisible(name, "sBorder")) return;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            EditorGUI.showMixedValue = property.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            float border = 1f - EditorGUI.Slider(row, FairyToonL10n.Text("sBorder"), 1f - Number(property), 0f, 1f);
            if (EditorGUI.EndChangeCheck()) SetNumber(property, border);
            EditorGUI.showMixedValue = false;
            EndScope();
        }

        private void MinusRange(string name, string label)
        {
            var property = Prop(name);
            if (property == null || !RowVisible(name, label)) return;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            EditorGUI.showMixedValue = property.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            float shown = EditorGUI.Slider(row, FairyToonL10n.Text(label), -Number(property), 0f, 1f);
            if (EditorGUI.EndChangeCheck()) SetNumber(property, -shown);
            EditorGUI.showMixedValue = false;
            EndScope();
        }

        private void FurVectorAndLength()
        {
            var property = Prop("_FurVector");
            if (property == null) return;
            Vector4 value = property.vectorValue;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            EditorGUI.BeginChangeCheck();
            Vector3 before = new Vector3(value.x, value.y, value.z);
            Vector3 direction = EditorGUI.Vector3Field(row, FairyToonL10n.Text("sVectorTex"), before);
            if (EditorGUI.EndChangeCheck())
            {
                int mask = 0;
                if (!Mathf.Approximately(direction.x, before.x)) { value.x = direction.x; mask |= 1; }
                if (!Mathf.Approximately(direction.y, before.y)) { value.y = direction.y; mask |= 2; }
                if (!Mathf.Approximately(direction.z, before.z)) { value.z = direction.z; mask |= 4; }
                if (mask != 0) SetDissolveComponents(property, value, mask);
            }
            EndScope();
            row = EditorGUILayout.GetControlRect();
            BeginScope(row, property);
            EditorGUI.BeginChangeCheck();
            float length = EditorGUI.FloatField(row, FairyToonL10n.Text("sLength"), value.w);
            if (EditorGUI.EndChangeCheck())
            {
                value = property.vectorValue;
                value.w = length;
                SetDissolveComponents(property, value, 8);
            }
            EndScope();
        }

        private void DecalPlacement(string texture, MaterialProperty copy, MaterialProperty flipCopy)
        {
            var tex = Prop(texture);
            if (tex == null) return;
            Vector4 st = tex.textureScaleAndOffset;
            DecodeDecalAxis(st.x, st.z, out float posX, out float scaleX);
            DecodeDecalAxis(st.y, st.w, out float posY, out float scaleY);
            int copyMode = 0;
            if (copy != null && Number(copy) > 0.5f) copyMode = 1;
            if (flipCopy != null && Number(flipCopy) > 0.5f) copyMode = 2;
            if (copyMode > 0 && posX < 0.5f) posX = 1f - posX;
            float nextX = posX;
            float nextY = posY;
            float nextScaleX = scaleX;
            float nextScaleY = scaleY;
            bool changeX = DecalSlider(tex, copyMode > 0 ? tex.name + "_ST.z" : tex.name + "_ST.z", "Position X", posX, copyMode > 0 ? 0.5f : 0f, 1f, ref nextX);
            bool changeY = DecalSlider(tex, tex.name + "_ST.w", "Position Y", posY, 0f, 1f, ref nextY);
            bool changeScaleX = DecalSlider(tex, tex.name + "_ST.x", "Scale X", scaleX, -1f, 1f, ref nextScaleX);
            bool changeScaleY = DecalSlider(tex, tex.name + "_ST.y", "Scale Y", scaleY, -1f, 1f, ref nextScaleY);
            if (!changeX && !changeY && !changeScaleX && !changeScaleY) return;
            Vector4 proposed = DecalVector(st, copyMode, changeX, changeY, changeScaleX, changeScaleY, nextX, nextY, nextScaleX, nextScaleY);
            if (OfferTextureScale(tex, proposed)) return;
            if (tex.targets == null) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                foreach (Object target in tex.targets)
                {
                    var material = target as Material;
                    if (material == null || !material.HasProperty(texture) || Locked(material, texture + "_ST")) continue;
                    Vector4 current = material.GetVector(texture + "_ST");
                    int mode = CopyModeOf(material, copy, flipCopy);
                    Vector4 next = DecalVector(current, mode, changeX, changeY, changeScaleX, changeScaleY, nextX, nextY, nextScaleX, nextScaleY);
                    if (next == current) continue;
                    Undo.RecordObject(material, "Modify Decal");
                    material.SetVector(texture + "_ST", next);
                    FairyToonMaterialSetup.Apply(material);
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
        }

        // A scale edit has to rebuild that axis's offset. Keeping the old offset slides the decal.
        private static Vector4 DecalVector(Vector4 stored, int copyMode, bool changeX, bool changeY, bool changeScaleX, bool changeScaleY, float nextX, float nextY, float nextScaleX, float nextScaleY)
        {
            DecodeDecalAxis(stored.x, stored.z, out float posX, out float scaleX);
            DecodeDecalAxis(stored.y, stored.w, out float posY, out float scaleY);
            if (copyMode > 0 && posX < 0.5f) posX = 1f - posX;
            if (changeX) posX = nextX;
            if (changeY) posY = nextY;
            if (changeScaleX) scaleX = nextScaleX;
            if (changeScaleY) scaleY = nextScaleY;
            Vector4 next = stored;
            if (changeX || changeScaleX) EncodeDecalAxis(posX, scaleX, out next.x, out next.z);
            if (changeY || changeScaleY) EncodeDecalAxis(posY, scaleY, out next.y, out next.w);
            return next;
        }

        private bool DecalSlider(MaterialProperty tex, string alt, string label, float current, float min, float max, ref float value)
        {
            if (Searching && !Hit(label) && !Hit(alt)) return false;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, tex);
            try
            {
                EditorGUI.BeginChangeCheck();
                value = EditorGUI.Slider(row, Event.current.alt ? alt : label, current, min, max);
                return EditorGUI.EndChangeCheck();
            }
            finally { EndScope(); }
        }

        private static void DecodeDecalAxis(float storedScale, float storedOffset, out float pos, out float scale)
        {
            if (storedScale == 0f) { pos = 0.5f; scale = 0.000001f; }
            else { pos = (0.5f - storedOffset) / storedScale; scale = 1f / storedScale; }
            pos = Round6(pos);
            scale = Round6(scale);
        }

        private static void EncodeDecalAxis(float pos, float scale, out float storedScale, out float storedOffset)
        {
            if (scale == 0f) scale = 0.000001f;
            storedScale = 1f / scale;
            storedOffset = (-pos * storedScale) + 0.5f;
        }

        private static float Round6(float value) => Mathf.Floor(value * 1000000f + 0.5f) * 0.000001f;

        private static int CopyModeOf(Material material, MaterialProperty copy, MaterialProperty flipCopy)
        {
            if (flipCopy != null && material.HasProperty(flipCopy.name) && material.GetFloat(flipCopy.name) > 0.5f) return 2;
            if (copy != null && material.HasProperty(copy.name) && material.GetFloat(copy.name) > 0.5f) return 1;
            return 0;
        }

        private static bool OfferTextureScale(MaterialProperty tex, Vector4 value)
        {
            if (tex.applyPropertyCallback == null) return false;
            bool handled = false;
            MaterialProperty.ApplyPropertyCallback original = tex.applyPropertyCallback;
            tex.applyPropertyCallback = (changed, mask, previous) =>
            {
                handled = original(changed, mask, previous);
                return true;
            };
            try { tex.textureScaleAndOffset = value; }
            finally { tex.applyPropertyCallback = original; }
            return handled;
        }

        private static bool Locked(Material material, string name)
        {
            return material.isVariant && material.IsPropertyLockedByAncestor(name);
        }

        private static void WriteAlpha(MaterialProperty prop, float alpha)
        {
            if (prop.applyPropertyCallback != null)
            {
                bool handled = false;
                MaterialProperty.ApplyPropertyCallback original = prop.applyPropertyCallback;
                prop.applyPropertyCallback = (changed, mask, previous) =>
                {
                    handled = original(changed, mask, previous);
                    return true;
                };
                try
                {
                    Color color = prop.colorValue;
                    color.a = alpha;
                    prop.colorValue = color;
                }
                finally { prop.applyPropertyCallback = original; }
                if (handled) return;
            }
            if (prop.targets == null) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.RecordObjects(prop.targets, "Modify Alpha");
            foreach (Object target in prop.targets)
            {
                var material = target as Material;
                if (material == null || !material.HasProperty(prop.name)) continue;
                Color color = material.GetColor(prop.name);
                color.a = alpha;
                material.SetColor(prop.name, color);
            }
            Undo.CollapseUndoOperations(group);
        }

        private static void WriteAlphaMask(MaterialProperty scale, MaterialProperty offset, bool invertChanged, bool transparencyChanged, bool invert, float transparency)
        {
            bool descriptorInvert = scale.floatValue < 0f;
            float descriptorTransparency = offset.floatValue - (descriptorInvert ? 1f : 0f);
            if (invertChanged) descriptorInvert = invert;
            if (transparencyChanged) descriptorTransparency = transparency;
            float callbackScale = descriptorInvert ? -1f : 1f;
            float callbackOffset = descriptorTransparency + (descriptorInvert ? 1f : 0f);
            bool scaleHandled = !invertChanged || OfferFloat(scale, callbackScale);
            bool offsetHandled = !(invertChanged || transparencyChanged) || OfferFloat(offset, callbackOffset);
            if (scaleHandled && offsetHandled) return;
            if (scale.targets == null) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                foreach (Object target in scale.targets)
                {
                    var material = target as Material;
                    if (material == null || !material.HasProperty(scale.name) || !material.HasProperty(offset.name)) continue;
                    float storedScale = material.GetFloat(scale.name);
                    float storedOffset = material.GetFloat(offset.name);
                    bool storedInvert = storedScale < 0f;
                    float storedTransparency = storedOffset - (storedInvert ? 1f : 0f);
                    if (invertChanged) storedInvert = invert;
                    if (transparencyChanged) storedTransparency = transparency;
                    float nextScale = storedInvert ? -1f : 1f;
                    float nextOffset = storedTransparency + (storedInvert ? 1f : 0f);
                    bool writeScale = !scaleHandled && !Mathf.Approximately(nextScale, storedScale) && !Locked(material, scale.name);
                    bool writeOffset = !offsetHandled && !Mathf.Approximately(nextOffset, storedOffset) && !Locked(material, offset.name);
                    if (!writeScale && !writeOffset) continue;
                    Undo.RecordObject(material, "Modify Alpha Mask");
                    if (writeScale) material.SetFloat(scale.name, nextScale);
                    if (writeOffset) material.SetFloat(offset.name, nextOffset);
                    FairyToonMaterialSetup.Apply(material);
                }
            }
            finally { Undo.CollapseUndoOperations(group); }
        }

        private static bool OfferFloat(MaterialProperty prop, float value)
        {
            if (prop.applyPropertyCallback == null) return false;
            bool handled = false;
            MaterialProperty.ApplyPropertyCallback original = prop.applyPropertyCallback;
            prop.applyPropertyCallback = (changed, mask, previous) =>
            {
                handled = original(changed, mask, previous);
                return true;
            };
            try { prop.floatValue = value; }
            finally { prop.applyPropertyCallback = original; }
            return handled;
        }

        private void ColorAlpha(string name)
        {
            var prop = Prop(name);
            if (prop == null || prop.propertyType != ShaderPropertyType.Color) return;
            if (!RowVisible(name, "sAlpha")) return;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, prop);
            EditorGUI.showMixedValue = prop.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            float alpha = EditorGUI.Slider(row, FairyToonL10n.Text("sAlpha"), prop.colorValue.a, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) WriteAlpha(prop, alpha);
            EditorGUI.showMixedValue = false;
            EndScope();
        }

        private void SubHeader(string title)
        {
            string shown = FairyToonL10n.Text(title);
            if (Searching && !Hit(shown)) return;
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(shown, EditorStyles.label);
        }

        private void Help(string message, MessageType type)
        {
            string shown = FairyToonL10n.Text(message);
            if (Searching && !Hit(shown)) return;
            EditorGUILayout.HelpBox(shown, type);
        }

        private MaterialProperty Prop(string name) => FindProperty(name, m_Props, false);

        private float Float(string name) => Number(Prop(name));

        /// <summary>
        /// ShaderLab <c>Int</c> is the legacy float-backed type (<see cref="ShaderPropertyType.Float"/>).
        /// ShaderLab <c>Integer</c> is <see cref="ShaderPropertyType.Int"/> and must use <c>intValue</c>.
        /// <c>floatValue</c> on a real int returns 0 and does not write.
        /// </summary>
        private static float Number(MaterialProperty prop)
        {
            if (prop == null) return 0f;
            return prop.propertyType == ShaderPropertyType.Int ? prop.intValue : prop.floatValue;
        }

        private static void SetNumber(MaterialProperty prop, float value)
        {
            if (prop == null) return;
            if (prop.propertyType == ShaderPropertyType.Int) prop.intValue = Mathf.RoundToInt(value);
            else prop.floatValue = value;
        }

        // Endfield eye overlay (Face and Eye parts): the painted lid shadow on the eye white and the iris scatter.
        private void EyeOverlay()
        {
            Tex("_EFEyeOverlay", null, "Eye Overlay (R lid shadow, G iris scatter)");
            var overlay = Prop("_EFEyeOverlay");
            if (overlay == null || (!overlay.hasMixedValue && overlay.textureValue == null)) return;
            P("_EFEyeShadowColor");
            P("_EFEyeScatterColor");
        }

        private void ToonIris()
        {
            if (!IrisMatches()) return;
            if (!BeginSection("Iris", "ft.Iris", Prop("_EFIris"), false, PropertyBlock.Iris)) return;
            Help("Head vectors come from FairyToonFaceShadow; without it the object axes are used (gaze then ignores head turns).", MessageType.None);
            // The section header owns the Toon toggle; Endfield keeps its existing inline toggle and placement.
            Iris(false);
            EndSection();
        }


        private bool IrisMatches() => SearchAllows("_EFIris", "_EFIrisDepth", "_EFPupilLayer", "_EFPupilDepth", "_EFIrisRefraction", "_EFIrisScale", "_EFIrisRim", "_EFIrisEdgeFade", "_EFIrisVertical", "_EFIrisMaxShift", "_EFIrisEdge", "_EFIrisEdgeSoftness", "_EFIrisGaze", "_EFCorneaBulge", "_EFCorneaNormalRange", "_EFCorneaIOR", "_EFIrisSparkleTex", "_EFIrisSparkle", "_EFIrisGlowStrength", "_EFIrisSparkleScale", "_EFIrisGlowColor", "_EFIrisFrame", "_EFIrisWorldRadius", "_EFIrisDish", "_EFIrisCenterL", "_EFIrisCenterR", "_EFIrisRadius", "_EFPupil", "_EFPupilCenterL", "_EFPupilCenterR", "_EFPupilRadius", "_EFPupilYRatio", "_EFPupilScale", "_EFPupilOffset", "_EFIris|3D Iris|Sink the iris behind a curved cornea and add its reflection and glow.", "_EFIrisDepth|Depth|How far the iris sinks; more makes it slide further when the head or camera turns.", "_EFPupilLayer|Pupil Layer|On: the pupil mesh moves as its own layer, so pupil shape keys (size, star, heart, off) work. Off: one default pupil is painted into the iris (older fold).", "_EFPupilDepth|Pupil Depth|How deep the pupil sits: 0 stays on the eye surface, 1 moves with the iris pattern. Middle values give a layered look and keep the pupil inside the iris at head turns.", "_EFIrisRefraction|Refraction|How much the cornea bends the view; also magnifies the iris centre face-on.", "_EFIrisScale|Art Scale|Zoom of the iris art inside the eye; lower values enlarge the pupil and show less of the iris rim.", "_EFIrisRim|Fixed Rim|Radius (iris units) from which the painted rim stays on the eye surface; set just inside the dark ring. 1.5 = off (the whole iris sinks).", "_EFIrisEdgeFade|Edge Fade|Fades the parallax to zero toward Fixed Rim (or the iris edge) so the rim never tears and the pattern never runs off the art. The band widens by itself at steep angles so the pattern never folds. 0 = off (hard switch at Fixed Rim).", "_EFIrisVertical|Vertical Strength|Scales up/down parallax; tall anime eyes often look better with less vertical shift.", "_EFIrisMaxShift|Max Shift|Soft limit on how far the pattern can shift, in iris radii, for steep viewing angles. 2 = off.", "_EFIrisEdge|Pattern Edge|Maximum lookup radius inside the painted pattern; keep it inside the painted dark ring and clear of painted highlight dots.", "_EFIrisEdgeSoftness|Edge Softness|How the pattern behaves near Pattern Edge: 0 repeats the edge colour (fibre streaks), higher squeezes the outer pattern toward the edge instead (no streaks); the inner part is untouched below (1 - softness) x Pattern Edge.", "_EFIrisGaze|Gaze Frame|1 uses head forward, centring the iris when the face looks at the camera; 0 uses the mesh surface.", "_EFCorneaBulge|Cornea Bulge|Height of the clear dome over the iris; more curves the view and reflection further.", "_EFCorneaNormalRange|Normal Range|Softens the dome's curve into the flat rim; higher values make a wider, flatter transition.", "_EFCorneaIOR|IOR|How strongly the cornea bends the matcap reflection; higher bends it more.", "_EFIrisSparkleTex|Texture (RGB light, A glow)|Reflection picture over the iris; RGB supplies light and alpha supplies the coloured glow.", "_EFIrisSparkle|Reflection Strength|Brightness of the matcap reflection; 0 removes the window panes without changing glow.", "_EFIrisGlowStrength|Glow Strength|Strength of the coloured glow; 0 removes glow without changing the reflection.", "_EFIrisSparkleScale|Matcap Scale|Size of the reflection pattern; larger values make the window panes smaller.", "_EFIrisGlowColor|Glow Colour|Colour and brightness of the glow screened over the iris.", "_EFIrisFrame|Frame (linear data, baked from UV islands)|Baked eye-island data that keeps iris motion aligned to this avatar's mesh.", "_EFIrisWorldRadius|Iris World Radius|World size of one iris radius in metres, measured from the mesh. Re-measure after rescaling.", "_EFIrisDish|Iris Dish|How much the iris mesh surface rises from centre to rim, in iris radii; keeps the moved pupil on it.", "_EFIrisCenterL|Iris Centre L|Centre of the left iris in the texture; shifts where its art is sampled.", "_EFIrisCenterR|Iris Centre R|Centre of the right iris in the texture; shifts where its art is sampled.", "_EFIrisRadius|Iris Radius|Size of each iris island in the texture; sets the reach of the art lookup.", "_EFPupil|Pupil Fold|How much of the separate pupil is folded into the iris picture.", "_EFPupilCenterL|Pupil Centre L|Centre of the left pupil island in the texture; aligns its folded picture.", "_EFPupilCenterR|Pupil Centre R|Centre of the right pupil island in the texture; aligns its folded picture.", "_EFPupilRadius|Pupil X Radius|Width of the pupil island in the texture; sets how much pupil art is sampled.", "_EFPupilYRatio|Pupil Y / X|Pupil height / width; shapes the fold and reserves vertical extent for the layer travel limit.", "_EFPupilScale|Pupil Scale in Iris Units|Pupil size in iris units; sizes the fold and reserves extent for the layer travel limit.", "_EFPupilOffset|Pupil Offset (left iris units)|Rest pupil centre in left iris units, used by both fold and layer; the right eye mirrors the horizontal offset.");
        private void Iris(bool showToggle = true)
        {

            if (!IrisMatches()) return;

            if (showToggle) P("_EFIris", "3D Iris", "Sink the iris behind a curved cornea and add its reflection and glow.");
            var iris = Prop("_EFIris");
            if (iris == null || (!iris.hasMixedValue && Number(iris) <= 0.5f)) return;
            EditorGUI.indentLevel++;
            // Put the visible look first; measured island data should not get in the way of hand tuning.
            SubHeader("Iris");
            P("_EFIrisDepth", "Depth", "How far the iris sinks; more makes it slide further when the head or camera turns.");
            P("_EFPupilLayer", "Pupil Layer", "On: the pupil mesh moves as its own layer, so pupil shape keys (size, star, heart, off) work. Off: one default pupil is painted into the iris (older fold).");
            P("_EFPupilDepth", "Pupil Depth", "How deep the pupil sits: 0 stays on the eye surface, 1 moves with the iris pattern. Middle values give a layered look and keep the pupil inside the iris at head turns.");
            P("_EFIrisRefraction", "Refraction", "How much the cornea bends the view; also magnifies the iris centre face-on.");
            P("_EFIrisScale", "Art Scale", "Zoom of the iris art inside the eye; lower values enlarge the pupil and show less of the iris rim.");
            P("_EFIrisRim", "Fixed Rim", "Radius (iris units) from which the painted rim stays on the eye surface; set just inside the dark ring. 1.5 = off (the whole iris sinks).");
            P("_EFIrisEdgeFade", "Edge Fade", "Fades the parallax to zero toward Fixed Rim (or the iris edge) so the rim never tears and the pattern never runs off the art. The band widens by itself at steep angles so the pattern never folds. 0 = off (hard switch at Fixed Rim).");
            P("_EFIrisVertical", "Vertical Strength", "Scales up/down parallax; tall anime eyes often look better with less vertical shift.");
            P("_EFIrisMaxShift", "Max Shift", "Soft limit on how far the pattern can shift, in iris radii, for steep viewing angles. 2 = off.");
            P("_EFIrisEdge", "Pattern Edge", "Maximum lookup radius inside the painted pattern; keep it inside the painted dark ring and clear of painted highlight dots.");
            P("_EFIrisEdgeSoftness", "Edge Softness", "How the pattern behaves near Pattern Edge: 0 repeats the edge colour (fibre streaks), higher squeezes the outer pattern toward the edge instead (no streaks); the inner part is untouched below (1 - softness) x Pattern Edge.");
            P("_EFIrisGaze", "Gaze Frame", "1 uses head forward, centring the iris when the face looks at the camera; 0 uses the mesh surface.");
            SubHeader("Cornea");
            P("_EFCorneaBulge", "Cornea Bulge", "Height of the clear dome over the iris; more curves the view and reflection further.");
            P("_EFCorneaNormalRange", "Normal Range", "Softens the dome's curve into the flat rim; higher values make a wider, flatter transition.");
            P("_EFCorneaIOR", "IOR", "How strongly the cornea bends the matcap reflection; higher bends it more.");
            SubHeader("Cornea matcap");
            P("_EFIrisSparkleTex", "Texture (RGB light, A glow)", "Reflection picture over the iris; RGB supplies light and alpha supplies the coloured glow.");
            P("_EFIrisSparkle", "Reflection Strength", "Brightness of the matcap reflection; 0 removes the window panes without changing glow.");
            P("_EFIrisGlowStrength", "Glow Strength", "Strength of the coloured glow; 0 removes glow without changing the reflection.");
            P("_EFIrisSparkleScale", "Matcap Scale", "Size of the reflection pattern; larger values make the window panes smaller.");
            P("_EFIrisGlowColor", "Glow Colour", "Colour and brightness of the glow screened over the iris.");
            if (BeginSection("IrisAvatarData", "ft.IrisAvatar", null, false, PropertyBlock.IrisAvatar))
            {
                P("_EFIrisFrame", "Frame (linear data, baked from UV islands)", "Baked eye-island data that keeps iris motion aligned to this avatar's mesh.");
                P("_EFIrisWorldRadius", "Iris World Radius", "World size of one iris radius in metres, measured from the mesh. Re-measure after rescaling.");
                P("_EFIrisDish", "Iris Dish", "How much the iris mesh surface rises from centre to rim, in iris radii; keeps the moved pupil on it.");
                P("_EFIrisCenterL", "Iris Centre L", "Centre of the left iris in the texture; shifts where its art is sampled.");
                P("_EFIrisCenterR", "Iris Centre R", "Centre of the right iris in the texture; shifts where its art is sampled.");
                P("_EFIrisRadius", "Iris Radius", "Size of each iris island in the texture; sets the reach of the art lookup.");
                SubHeader("Pupil fold");
                Help("Measured per avatar. Fold uses pupil centres, radius, Y ratio, scale, offset and Pupil Fold. Layer uses offset, scale and Y ratio for its travel limit.", MessageType.None);
                P("_EFPupil", "Pupil Fold", "How much of the separate pupil is folded into the iris picture.");
                P("_EFPupilCenterL", "Pupil Centre L", "Centre of the left pupil island in the texture; aligns its folded picture.");
                P("_EFPupilCenterR", "Pupil Centre R", "Centre of the right pupil island in the texture; aligns its folded picture.");
                P("_EFPupilRadius", "Pupil X Radius", "Width of the pupil island in the texture; sets how much pupil art is sampled.");
                P("_EFPupilYRatio", "Pupil Y / X", "Pupil height / width; shapes the fold and reserves vertical extent for the layer travel limit.");
                P("_EFPupilScale", "Pupil Scale in Iris Units", "Pupil size in iris units; sizes the fold and reserves extent for the layer travel limit.");
                P("_EFPupilOffset", "Pupil Offset (left iris units)", "Rest pupil centre in left iris units, used by both fold and layer; the right eye mirrors the horizontal offset.");
                EndSection();
            }
            EditorGUI.indentLevel--;
        }

        private void P(string name, string label = null, string tooltip = null)
        {
            var prop = Prop(name);
            if (prop == null) return;
            string shown = label != null ? FairyToonL10n.Text(label) : FairyToonL10n.Property(name, prop.displayName);
            if (!RowVisible(name, shown, tooltip)) return;
            string tip = string.IsNullOrEmpty(tooltip) ? null : FairyToonL10n.Text(tooltip);
            if (DrawLocalizedEnum(prop, shown, tip)) return;
            if (prop.propertyType == ShaderPropertyType.Color)
            {
                Rect row = EditorGUILayout.GetControlRect();
                BeginScope(row, prop);
                EditorGUI.showMixedValue = prop.hasMixedValue;
                if (IsGammaHdr(name) || IsBuiltinHdr(name))
                {
                    Color color = prop.colorValue;
                    if (FairyToonHDRDrawer.Draw(row, shown, ref color)) prop.colorValue = color;
                    EditorGUI.showMixedValue = false;
                    EndScope();
                }
                else
                {
                    EditorGUI.showMixedValue = false;
                    EndScope();
                    m_Editor.ColorProperty(prop, shown);
                }
                return;
            }
            m_Editor.ShaderProperty(prop, new GUIContent(shown, tip));
        }

        /// <summary>Enum popups use the catalog for each option, the same way lilToon builds sAlphaMaskModes.</summary>
        private bool DrawLocalizedEnum(MaterialProperty prop, string shown, string tip, Rect rect = default)
        {
            if (m_Editor.target is not Material material || material.shader == null) return false;
            int index = material.shader.FindPropertyIndex(prop.name);
            if (index < 0) return false;
            string raw = null;
            foreach (string attribute in material.shader.GetPropertyAttributes(index))
            {
                if (attribute.StartsWith("Enum(", StringComparison.Ordinal) && attribute.EndsWith(")", StringComparison.Ordinal))
                {
                    raw = attribute.Substring(5, attribute.Length - 6);
                    break;
                }
            }
            if (string.IsNullOrEmpty(raw)) return false;
            if (!TryEnumOptions(raw, out string[] names, out int[] values)) return false;
            var labels = new string[names.Length];
            for (int i = 0; i < names.Length; i++) labels[i] = EnumOption(prop.name, names[i]);
            int current = prop.hasMixedValue ? -1 : Mathf.RoundToInt(Number(prop));
            int selected = -1;
            for (int i = 0; i < values.Length; i++) if (values[i] == current) selected = i;
            var content = new GUIContent(shown, tip);
            if (rect.width <= 0f || rect.height <= 0f) rect = EditorGUILayout.GetControlRect();
            BeginScope(rect, prop);
            EditorGUI.showMixedValue = prop.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            var options = new GUIContent[labels.Length];
            for (int i = 0; i < labels.Length; i++) options[i] = new GUIContent(labels[i]);
            // Popup(Rect, GUIContent, int, string[]) is internal in Unity 6000.6. The GUIContent[] overload is public.
            int next = EditorGUI.Popup(rect, content, prop.hasMixedValue ? -1 : selected, options);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            if (changed && next >= 0 && next < values.Length) SetNumber(prop, values[next]);
            EndScope();
            return true;
        }

        private static string EnumOption(string property, string raw)
        {
            bool blend = property.IndexOf("BlendMode", StringComparison.Ordinal) >= 0;
            if (blend && raw == "Normal") return FairyToonL10n.Text("sBlendModeNormal");
            if (blend && raw == "Add") return FairyToonL10n.Text("sBlendModeAdd");
            if (blend && raw == "Screen") return FairyToonL10n.Text("sBlendModeScreen");
            if (blend && raw == "Multiply") return FairyToonL10n.Text("sBlendModeMul");
            return FairyToonL10n.Text(raw);
        }

        private static bool TryEnumOptions(string raw, out string[] names, out int[] values)
        {
            names = null;
            values = null;
            if (!raw.Contains(","))
            {
                Type type = Type.GetType(raw)
                    ?? Type.GetType(raw + ", UnityEngine.CoreModule")
                    ?? Type.GetType(raw + ", UnityEngine")
                    ?? typeof(CullMode).Assembly.GetType(raw);
                if (type == null || !type.IsEnum) return false;
                Array members = Enum.GetValues(type);
                names = Enum.GetNames(type);
                values = new int[members.Length];
                for (int i = 0; i < members.Length; i++) values[i] = Convert.ToInt32(members.GetValue(i));
                return names.Length > 0;
            }
            string[] parts = raw.Split(',');
            if ((parts.Length % 2) != 0) return false;
            int count = parts.Length / 2;
            names = new string[count];
            values = new int[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = parts[i * 2].Trim();
                if (!int.TryParse(parts[i * 2 + 1].Trim(), out values[i])) return false;
            }
            return count > 0;
        }

        private void ToggleRow(string name, string label)
        {
            var prop = Prop(name);
            if (prop == null) return;
            string shown = FairyToonL10n.Text(label);
            if (!RowVisible(name, shown, null)) return;
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, prop);
            EditorGUI.showMixedValue = prop.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            bool value = EditorGUI.ToggleLeft(row, new GUIContent(shown), Number(prop) > 0.5f);
            if (EditorGUI.EndChangeCheck()) SetNumber(prop, value ? 1f : 0f);
            EditorGUI.showMixedValue = false;
            EndScope();
        }

        /// <summary>lilToon-style single line: texture slot plus its colour or value.</summary>
        private void Tex(string texture, string extra, string label)
        {
            var tex = Prop(texture);
            if (tex == null) return;
            string shown = FairyToonL10n.Text(label);
            if (!RowVisible(texture, shown) && (extra == null || !RowVisible(extra, shown))) return;
            var other = extra != null ? Prop(extra) : null;
            // These lilToon colours are gamma-authored even above 1; HDR editing must not add the native HDR flag.
            // TexturePropertySingleLine ignores custom drawers, so any [FairyToonHDR] colour takes the HDR picker here.
            var content = new GUIContent(shown);
            bool colorExtra = other != null && other.propertyType == ShaderPropertyType.Color;
            Rect row = m_Editor.TexturePropertySingleLine(content, tex, colorExtra ? null : other);
            if (!colorExtra) return;
            bool hdr = IsGammaHdr(extra) || IsBuiltinHdr(extra);
            Rect slot = row;
            slot.xMin = row.xMax - (hdr ? 108f : 54f);
            BeginScope(slot, other);
            EditorGUI.showMixedValue = other.hasMixedValue;
            if (hdr)
            {
                Color color = other.colorValue;
                if (FairyToonHDRDrawer.Draw(slot, null, ref color)) other.colorValue = color;
            }
            else
            {
                var swatch = new Rect(slot.xMax - 50f, slot.y, 50f, slot.height);
                EditorGUI.BeginChangeCheck();
                Color color = EditorGUI.ColorField(swatch, GUIContent.none, other.colorValue, true, true, false);
                if (EditorGUI.EndChangeCheck()) other.colorValue = color;
            }
            EditorGUI.showMixedValue = false;
            EndScope();
        }

        private bool IsGammaHdr(string property) => HasAttribute(property, "FairyToonHDR");

        private bool IsBuiltinHdr(string property) => HasAttribute(property, "HDR");

        private bool HasAttribute(string property, string attribute)
        {
            if (string.IsNullOrEmpty(property) || m_Editor.target is not Material material || material.shader == null) return false;
            int index = material.shader.FindPropertyIndex(property);
            return index >= 0 && System.Array.IndexOf(material.shader.GetPropertyAttributes(index), attribute) >= 0;
        }

        private void ScaleOffset(string texture)
        {
            var tex = Prop(texture);
            if (tex == null || (tex.propertyFlags & UnityEngine.Rendering.ShaderPropertyFlags.NoScaleOffset) != 0) return;
            bool named = !Searching || Hit(texture) || Hit(tex.displayName) || Hit(FairyToonL10n.Property(texture, tex.displayName));
            bool showTiling = named || LabelOnly("ft.Tiling");
            bool showOffset = named || LabelOnly("ft.Offset");
            if (!showTiling && !showOffset) return;
            Vector4 st = tex.textureScaleAndOffset;
            if (showTiling) DrawTextureAxes(tex, st, true);
            if (showOffset) DrawTextureAxes(tex, st, false);
        }

        // Native TextureScaleOffsetProperty scopes the row with BeginProperty and BeginAnimatedCheck, and its mixed
        // mask is the scale/offset bits, not the texture reference. Each label gets that scope. Mixed state is read
        // from the materials so a different texture does not mark both rows mixed.
        private void DrawTextureAxes(MaterialProperty tex, Vector4 st, bool scaleRow)
        {
            Rect row = EditorGUILayout.GetControlRect();
            BeginScope(row, tex);
            EditorGUI.showMixedValue = TextureAxesDiffer(tex, scaleRow);
            EditorGUI.BeginChangeCheck();
            Vector2 edited = EditorGUI.Vector2Field(row, new GUIContent(FairyToonL10n.Text(scaleRow ? "ft.Tiling" : "ft.Offset")),
                scaleRow ? new Vector2(st.x, st.y) : new Vector2(st.z, st.w));
            if (EditorGUI.EndChangeCheck())
            {
                Vector4 next = tex.textureScaleAndOffset;
                if (scaleRow) { next.x = edited.x; next.y = edited.y; }
                else { next.z = edited.x; next.w = edited.y; }
                tex.textureScaleAndOffset = next;
            }
            EditorGUI.showMixedValue = false;
            EndScope();
        }

        private static bool TextureAxesDiffer(MaterialProperty tex, bool scale)
        {
            if (tex.targets == null || tex.targets.Length < 2 || !(tex.targets[0] is Material first)) return false;
            Vector2 baseline = scale ? first.GetTextureScale(tex.name) : first.GetTextureOffset(tex.name);
            for (int i = 1; i < tex.targets.Length; i++)
            {
                if (!(tex.targets[i] is Material material)) continue;
                Vector2 value = scale ? material.GetTextureScale(tex.name) : material.GetTextureOffset(tex.name);
                if (value != baseline) return true;
            }
            return false;
        }

        private static void InitStyles()
        {
            if (s_HeaderLabel != null && s_ProSkin == EditorGUIUtility.isProSkin) return;
            s_ProSkin = EditorGUIUtility.isProSkin;
            s_Foldout = new GUIStyle("ShurikenModuleTitle")
            {
                font = EditorStyles.label.font,
                fontSize = EditorStyles.label.fontSize,
                border = new RectOffset(15, 7, 4, 4),
                contentOffset = new Vector2(20f, -2f),
                fixedHeight = 22
            };
            if (s_Foldout.normal.background == null)
            {
                s_Foldout = new GUIStyle(EditorStyles.toolbarButton)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    fixedHeight = 22
                };
            }
            s_HeaderLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Normal };
            s_HeaderLabel.normal.textColor = s_Foldout.normal.textColor;
            s_Box = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 6, 4, 6) };
            s_Gear = new GUIStyle(GUIStyle.none) { alignment = TextAnchor.MiddleCenter };
        }
    }
}
