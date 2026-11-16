using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NW.FairyToon.Editor
{
    /// <summary>Set-up checklist for <see cref="FairyToonSeeThroughHair"/>: renderer feature, material roles, mask,
    /// with one-click fixes and the Mask Painter.</summary>
    [CustomEditor(typeof(FairyToonSeeThroughHair))]
    public sealed class FairyToonSeeThroughHairEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var component = (FairyToonSeeThroughHair)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Set-up", EditorStyles.boldLabel);

            bool feature = FairyToonSeeThroughSetup.FeatureInstalled();
            Row(feature, feature ? "FairyToon renderer feature on the active URP renderer" : "No FairyToon renderer feature: the fringe is cut but never drawn over the eyes",
                feature ? null : "Add", () => FairyToonSeeThroughSetup.InstallFeature());

            // (roles per slot: a face is often one slot of the body renderer, as on Mayo)
            EditorGUILayout.LabelField("Face slots (the eye / brow mask lives here)", EditorStyles.miniBoldLabel);
            if (component.FaceRenderer == null) EditorGUILayout.HelpBox("Pick the renderer that holds the face.", MessageType.Info);
            else RoleToggles(component.FaceRenderer, FairyToonMaterialSetup.RoleFace, true);
            EditorGUILayout.LabelField("Fringe slots (drawn see-through over the mask)", EditorStyles.miniBoldLabel);
            bool anyHair = false;
            if (component.HairRenderers != null)
                foreach (var renderer in component.HairRenderers)
                    if (renderer != null) { RoleToggles(renderer, FairyToonMaterialSetup.RoleHair, false); anyHair = true; }
            if (!anyHair) EditorGUILayout.HelpBox("Add the fringe renderer(s).", MessageType.Info);

            EditorGUILayout.HelpBox("Opacity, early Z, view-angle fade and fake shadow are material settings: see the fringe material's See-through Hair section.", MessageType.None);
        }

        private static void Row(bool ok, string text, string button, System.Action fix)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var icon = EditorGUIUtility.IconContent(ok ? "TestPassed" : "console.warnicon.sml");
                GUILayout.Label(icon, GUILayout.Width(18), GUILayout.Height(18));
                EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
                if (button != null && GUILayout.Button(button, GUILayout.Width(60))) fix();
            }
        }

        // One row per FairyToon slot: role toggle, and for face slots the mask state with the painter.
        private static void RoleToggles(Renderer renderer, int role, bool face)
        {
            var materials = renderer.sharedMaterials;
            var seen = new HashSet<Material>();
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null || material.shader == null || material.shader.name != FairyToon.ShaderName) continue;
                bool transparent = Mathf.RoundToInt(material.GetFloat("_TransparentMode")) == FairyToonMaterialSetup.Transparent;
                if (face && transparent) continue;
                bool on = Mathf.RoundToInt(material.GetFloat("_SeeThroughRole")) == role;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool shared = !seen.Add(material);
                    using (new EditorGUI.DisabledScope(shared))
                    {
                        bool value = EditorGUILayout.ToggleLeft($"{renderer.name} [{i}] {material.name}", on);
                        if (value != on)
                        {
                            Undo.RecordObject(material, "Set See-through Role");
                            material.SetFloat("_SeeThroughRole", value ? role : FairyToonMaterialSetup.RoleNone);
                            FairyToonMaterialSetup.Apply(material);
                            EditorUtility.SetDirty(material);
                        }
                    }
                    if (face && on)
                    {
                        bool mask = material.GetTexture("_SeeThroughMask") != null;
                        GUILayout.Label(EditorGUIUtility.IconContent(mask ? "TestPassed" : "console.warnicon.sml"), GUILayout.Width(18), GUILayout.Height(18));
                        int slot = i;
                        if (GUILayout.Button(mask ? "Paint" : "Create / Paint", GUILayout.Width(96)))
                            FairyToonMaskPainter.Open(renderer, slot, "_SeeThroughMask", Mathf.RoundToInt(material.GetFloat("_SeeThroughMaskChannel")));
                    }
                }
            }
        }
    }
}
