// Adapted from lilToon 2.4.1 (MIT, Copyright (c) 2020-2024 lilxyzw).
// Clipboard: Assets/lilToon/Editor/lilInspector/lilEditorVariables.cs (copiedProperties, copiedVectorProperties)
// and lilGUIUtility.cs CopyProperties / PasteProperties / ResetProperties
// at commit e698a2f791bd28dd1aabf227c74f14bedeca3587.
// See THIRD_PARTY_NOTICES.md.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NW.FairyToon.Editor
{
    /// <summary>
    /// Accumulating clipboard, same shape as lilToon. <see cref="CopiedProperties"/> is static and keyed by
    /// property name. Copy adds or overwrites that block's names and does not clear the others, so Copy on
    /// MatCap and then Copy on Rim Light leaves both in the dictionary. Paste writes only the destination
    /// block's names that are already in the dictionary, and skips textures unless Paste (with texture).
    /// Texture scale/offset is stored in <see cref="CopiedVectorProperties"/> as <c>name + "_ST"</c> and is
    /// applied only with textures, and only on the first selected material, as lilToon does.
    /// Values are the <see cref="MaterialProperty"/> objects the inspector received in OnGUI, read at paste time.
    /// Copy and paste do not call <c>MaterialEditor.GetMaterialProperties</c>, so a renderer property block or
    /// animation-mode callback on those objects is kept. Texture scale/offset still uses the first target only.
    /// The dictionaries live for the editor session and are empty again after a domain reload, same as lilToon's static fields.
    /// A lilToon inspector does not share this clipboard.
    /// </summary>
    internal static class FairyToonClipboard
    {
        internal static readonly Dictionary<string, MaterialProperty> CopiedProperties = new Dictionary<string, MaterialProperty>();
        internal static readonly Dictionary<string, Vector4> CopiedVectorProperties = new Dictionary<string, Vector4>();

        private static readonly Dictionary<PropertyBlock, string> s_ManualAnchors = new Dictionary<PropertyBlock, string>
        {
            { PropertyBlock.Base, "base/base" },
            { PropertyBlock.Lighting, "base/lighting" },
            { PropertyBlock.MainColor, "color/maincolor" },
            { PropertyBlock.MainColor1st, "color/maincolor" },
            { PropertyBlock.MainColor2nd, "color/maincolor_layer" },
            { PropertyBlock.MainColor3rd, "color/maincolor_layer" },
            { PropertyBlock.AlphaMask, "color/alphamask" },
            { PropertyBlock.Shadow, "color/shadow" },
            { PropertyBlock.RimShade, "color/rimshade" },
            { PropertyBlock.Emission, "color/emission" },
            { PropertyBlock.Emission1st, "color/emission" },
            { PropertyBlock.Emission2nd, "color/emission" },
            { PropertyBlock.NormalMap, "reflections/normal" },
            { PropertyBlock.NormalMap1st, "reflections/normal" },
            { PropertyBlock.NormalMap2nd, "reflections/normal" },
            { PropertyBlock.Anisotropy, "reflections/anisotropy" },
            { PropertyBlock.Backlight, "reflections/backlight" },
            { PropertyBlock.Reflection, "reflections/reflection" },
            { PropertyBlock.MatCaps, "reflections/matcap" },
            { PropertyBlock.MatCap1st, "reflections/matcap" },
            { PropertyBlock.MatCap2nd, "reflections/matcap" },
            { PropertyBlock.RimLight, "reflections/rimlight" },
            { PropertyBlock.Glitter, "reflections/glitter" },
            { PropertyBlock.Gem, "reflections/gem" },
            { PropertyBlock.Outline, "advanced/outline" },
            { PropertyBlock.Dissolve, "advanced/dissolve" },
            { PropertyBlock.Refraction, "advanced/refraction" },
            { PropertyBlock.Fur, "advanced/fur" },
            { PropertyBlock.Stencil, "advanced/stencil" },
            { PropertyBlock.Rendering, "advanced/rendering" }
        };

        internal static void ShowMenu(PropertyBlock block, MaterialEditor editor, System.Func<MaterialProperty[]> currentProperties)
        {
            if (editor == null || currentProperties == null) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sCopy")), false, () => CopyProperties(block, editor, currentProperties()));
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sPaste")), false, () => PasteProperties(block, editor, currentProperties(), false));
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sPasteWithTexture")), false, () => PasteProperties(block, editor, currentProperties(), true));
            menu.AddItem(new GUIContent(FairyToonL10n.Text("sReset")), false, () => ResetProperties(block, editor, currentProperties()));
            if (s_ManualAnchors.TryGetValue(block, out string anchor))
            {
                string url = FairyToonL10n.Text("sManualURL") + anchor;
                menu.AddItem(new GUIContent(FairyToonL10n.Text("sOpenManual")), false, () => Application.OpenURL(url));
            }
            menu.ShowAsContext();
        }

        internal static void CopyProperties(PropertyBlock block, MaterialEditor editor, MaterialProperty[] props)
        {
            var material = editor.target as Material;
            if (material == null || props == null) return;
            for (int i = 0; i < FairyToonPropertyBlocks.All.Length; i++)
            {
                FairyToonPropertyEntry entry = FairyToonPropertyBlocks.All[i];
                if (!Contains(entry, block)) continue;
                MaterialProperty prop = Find(props, entry.Name);
                if (prop == null) continue;
                CopiedProperties[entry.Name] = prop;
                if (!entry.IsTexture) continue;
                string st = entry.Name + "_ST";
                if (material.HasProperty(st))
                    CopiedVectorProperties[st] = material.GetVector(st);
            }
        }

        internal static void PasteProperties(PropertyBlock block, MaterialEditor editor, MaterialProperty[] props, bool shouldCopyTex)
        {
            var material = editor.target as Material;
            if (material == null || props == null) return;
            string undo = shouldCopyTex ? "FairyToon Paste (with textures)" : "FairyToon Paste";
            int group = BeginUndo(editor, undo);
            var coverage = SnapshotCoverage(editor);
            int writes = 0;
            for (int i = 0; i < FairyToonPropertyBlocks.All.Length; i++)
            {
                FairyToonPropertyEntry entry = FairyToonPropertyBlocks.All[i];
                if (!Contains(entry, block)) continue;
                if (!shouldCopyTex && entry.IsTexture) continue;
                if (!CopiedProperties.TryGetValue(entry.Name, out MaterialProperty source) || source == null) continue;
                MaterialProperty dest = Find(props, entry.Name);
                if (dest == null) continue;
                ApplyValue(dest, source);
                writes++;
                if (!entry.IsTexture || !shouldCopyTex) continue;
                string st = entry.Name + "_ST";
                if (CopiedVectorProperties.ContainsKey(st) && material.HasProperty(st))
                {
                    material.SetVector(st, CopiedVectorProperties[st]);
                    writes++;
                }
            }
            FinishEdit(editor, group, coverage, writes);
        }

        internal static void ResetProperties(PropertyBlock block, MaterialEditor editor, MaterialProperty[] props)
        {
            if (props == null) return;
            int group = BeginUndo(editor, "FairyToon Reset");
            var coverage = SnapshotCoverage(editor);
            int writes = 0;
            for (int i = 0; i < props.Length; i++)
            {
                MaterialProperty prop = props[i];
                if (prop == null || !FairyToonPropertyBlocks.Belongs(block, prop.name)) continue;
                if (prop.targets == null || prop.targets.Length == 0 || !(prop.targets[0] is Material material) || material.shader == null)
                    continue;
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
                writes++;
            }
            FinishEdit(editor, group, coverage, writes);
        }

        private static void FinishEdit(MaterialEditor editor, int group, List<CoverageSnapshot> coverage, int writes)
        {
            if (writes == 0)
            {
                Undo.RevertAllDownToGroup(group);
                return;
            }
            // Property writes already go through the inspector descriptors. Apply rewrites blend, queue
            // and passes on the material asset, so skip it while an animation callback owns the write.
            if (!AnimationMode.InAnimationMode())
            {
                MigrateCoverage(coverage);
                ApplyMaterials(editor);
            }
            Undo.CollapseUndoOperations(group);
        }

        private static void ApplyValue(MaterialProperty dest, MaterialProperty source)
        {
            switch (dest.propertyType)
            {
                case ShaderPropertyType.Color:
                    dest.colorValue = source.colorValue;
                    break;
                case ShaderPropertyType.Vector:
                    dest.vectorValue = source.vectorValue;
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    dest.floatValue = source.floatValue;
                    break;
                case ShaderPropertyType.Int:
                    dest.intValue = source.intValue;
                    break;
                case ShaderPropertyType.Texture:
                    dest.textureValue = source.textureValue;
                    break;
                default:
                    throw new InvalidOperationException("Unhandled shader property type " + dest.propertyType);
            }
        }

        private struct CoverageSnapshot
        {
            public Material Material;
            public int Mode;
            public int Queue;
            public int RawQueue;
        }

        private static List<CoverageSnapshot> SnapshotCoverage(MaterialEditor editor)
        {
            var shots = new List<CoverageSnapshot>();
            foreach (UnityEngine.Object target in editor.targets)
            {
                var material = target as Material;
                if (material == null) continue;
                shots.Add(new CoverageSnapshot
                {
                    Material = material,
                    Mode = FairyToonMaterialSetup.GetRenderingMode(material),
                    Queue = material.renderQueue,
                    RawQueue = material.rawRenderQueue
                });
            }
            return shots;
        }

        private static void MigrateCoverage(List<CoverageSnapshot> shots)
        {
            for (int i = 0; i < shots.Count; i++)
            {
                CoverageSnapshot shot = shots[i];
                FairyToonMaterialSetup.MigrateMainCoverageQueue(shot.Material, shot.Mode, shot.Queue, shot.RawQueue);
            }
        }

        private static bool Contains(FairyToonPropertyEntry entry, PropertyBlock block)
        {
            for (int i = 0; i < entry.Blocks.Length; i++)
                if (entry.Blocks[i] == block) return true;
            return false;
        }

        private static MaterialProperty Find(MaterialProperty[] props, string name)
        {
            for (int i = 0; i < props.Length; i++)
                if (props[i] != null && props[i].name == name) return props[i];
            return null;
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
    }
}
