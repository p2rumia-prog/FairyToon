using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NW.FairyToon.Editor
{
    /// <summary>Paints one channel of any 2D texture slot of a renderer's material directly on the mesh in the Scene view.</summary>
    /// <remarks>Built for the see-through eye / brow mask, and for any other mask (Endfield face region, hair mask).
    /// LMB paints towards Value, Shift+LMB towards 0, Ctrl+LMB fills the UV island under the cursor (Ctrl+Shift clears
    /// it), [ / ] change the radius, Ctrl+Z / Ctrl+Y undo / redo. Nothing is written to disk until Save.</remarks>
    public sealed class FairyToonMaskPainter : EditorWindow
    {
        private const string Prefs = "NW.FairyToon.MaskPainter.";
        private static readonly string[] ChannelNames = { "R", "G", "B", "A" };
        private static readonly int[] Sizes = { 512, 1024, 2048, 4096 };
        private static readonly int PainterHash = "FairyToonMaskPainter".GetHashCode();
        private static readonly Vector4[] Channels = { new Vector4(1, 0, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 0), new Vector4(0, 0, 0, 1) };

        [SerializeField] private Renderer m_Renderer;
        [SerializeField] private int m_Slot;
        [SerializeField] private string m_Property = "_SeeThroughMask";
        [SerializeField] private int m_Channel;
        [SerializeField] private int m_NewSize = 1024;
        [SerializeField] private bool m_NewFillWhite;

        private float m_Radius, m_Hardness, m_Strength, m_Value;
        private bool m_FrontOnly, m_Overlay, m_XRay, m_Isolate;
        private Color m_Tint;

        private FairyToonMaskCanvas m_Canvas;
        private Material m_OverlayMaterial;
        private bool m_HasHit;
        private FairyToonMaskCanvas.Hit m_Hit;
        private Vector3 m_LastStamp;
        private bool m_Stroking;
        private string m_Status;
        private Vector2 m_Scroll;

        [MenuItem("Tools/FairyToon/Mask Painter")]
        public static FairyToonMaskPainter OpenWindow() => GetWindow<FairyToonMaskPainter>("Mask Painter");

        /// <summary>Opens the painter on a renderer slot and texture property, ready to paint.</summary>
        public static FairyToonMaskPainter Open(Renderer renderer, int slot, string property, int channel)
        {
            var window = OpenWindow();
            window.StopPainting();
            window.m_Renderer = renderer;
            window.m_Slot = slot;
            window.m_Property = property;
            window.m_Channel = channel;
            window.Repaint();
            return window;
        }

        /// <summary>True while a canvas is open on this renderer slot (the material inspector uses it).</summary>
        public static bool IsPainting(Material material) =>
            HasOpenInstances<FairyToonMaskPainter>() && GetWindow<FairyToonMaskPainter>(false, null, false).m_Canvas?.Material == material;

        private void OnEnable()
        {
            m_Radius = EditorPrefs.GetFloat(Prefs + "Radius", 0.006f);
            m_Hardness = EditorPrefs.GetFloat(Prefs + "Hardness", 0.6f);
            m_Strength = EditorPrefs.GetFloat(Prefs + "Strength", 1f);
            m_Value = EditorPrefs.GetFloat(Prefs + "Value", 1f);
            m_FrontOnly = EditorPrefs.GetBool(Prefs + "FrontOnly", true);
            m_Overlay = EditorPrefs.GetBool(Prefs + "Overlay", true);
            m_XRay = EditorPrefs.GetBool(Prefs + "XRay", true);
            m_Isolate = EditorPrefs.GetBool(Prefs + "Isolate", false);
            m_Tint = new Color(1f, 0.2f, 0.55f, 0.55f);
            SceneView.duringSceneGui += OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            if (m_Renderer == null && Selection.activeGameObject != null) m_Renderer = Selection.activeGameObject.GetComponentInChildren<Renderer>();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            if (m_Canvas != null && m_Canvas.Dirty
                && EditorUtility.DisplayDialog("Mask Painter", $"Save the changes to {m_Canvas.Source.name}?", "Save", "Discard"))
                m_Canvas.Save();
            StopPainting();
            SavePrefs();
            if (m_OverlayMaterial != null) DestroyImmediate(m_OverlayMaterial);
        }

        private void OnBeforeReload()
        {
            // (no dialog is possible during a domain reload; unsaved strokes are lost, so say so)
            if (m_Canvas != null && m_Canvas.Dirty) Debug.LogWarning($"[FairyToon] Mask Painter: unsaved changes to {m_Canvas.Source.name} were dropped by a script reload.");
            StopPainting();
        }

        private void SavePrefs()
        {
            EditorPrefs.SetFloat(Prefs + "Radius", m_Radius);
            EditorPrefs.SetFloat(Prefs + "Hardness", m_Hardness);
            EditorPrefs.SetFloat(Prefs + "Strength", m_Strength);
            EditorPrefs.SetFloat(Prefs + "Value", m_Value);
            EditorPrefs.SetBool(Prefs + "FrontOnly", m_FrontOnly);
            EditorPrefs.SetBool(Prefs + "Overlay", m_Overlay);
            EditorPrefs.SetBool(Prefs + "XRay", m_XRay);
            EditorPrefs.SetBool(Prefs + "Isolate", m_Isolate);
        }

        // ---- Window ----

        private void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            bool painting = m_Canvas != null;

            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(painting))
            {
                m_Renderer = (Renderer)EditorGUILayout.ObjectField("Renderer", m_Renderer, typeof(Renderer), true);
                if (m_Renderer == null)
                {
                    EditorGUILayout.HelpBox("Pick a renderer (MeshRenderer or SkinnedMeshRenderer) in the scene.", MessageType.Info);
                    EditorGUILayout.EndScrollView();
                    return;
                }
                var materials = m_Renderer.sharedMaterials;
                var slotNames = new string[materials.Length];
                for (int i = 0; i < materials.Length; i++) slotNames[i] = $"{i}: {(materials[i] != null ? materials[i].name : "(none)")}";
                m_Slot = Mathf.Clamp(m_Slot, 0, Mathf.Max(0, materials.Length - 1));
                m_Slot = EditorGUILayout.Popup("Material", m_Slot, slotNames);
                var material = materials.Length > 0 ? materials[m_Slot] : null;
                if (material == null)
                {
                    EditorGUILayout.HelpBox("This slot has no material.", MessageType.Warning);
                    EditorGUILayout.EndScrollView();
                    return;
                }

                var properties = TextureProperties(material.shader);
                int index = Mathf.Max(0, properties.IndexOf(m_Property));
                if (properties.Count == 0)
                {
                    EditorGUILayout.HelpBox("The shader has no 2D texture properties.", MessageType.Warning);
                    EditorGUILayout.EndScrollView();
                    return;
                }
                index = EditorGUILayout.Popup("Texture", index, properties.ConvertAll(p => $"{Label(material.shader, p)}  ({p})").ToArray());
                if (properties[index] != m_Property)
                {
                    m_Property = properties[index];
                    if (m_Property == "_SeeThroughMask" && material.HasProperty("_SeeThroughMaskChannel"))
                        m_Channel = Mathf.Clamp(Mathf.RoundToInt(material.GetFloat("_SeeThroughMaskChannel")), 0, 3);
                }
                m_Channel = GUILayout.Toolbar(m_Channel, ChannelNames);

                var texture = material.GetTexture(m_Property);
                EditorGUILayout.ObjectField("File", texture, typeof(Texture), false);
                if (texture == null || texture is not Texture2D)
                {
                    EditorGUILayout.HelpBox(texture == null ? "No texture in this slot yet." : "Only 2D textures can be painted.", MessageType.Info);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        int size = System.Array.IndexOf(Sizes, m_NewSize);
                        size = EditorGUILayout.Popup(Mathf.Max(0, size), System.Array.ConvertAll(Sizes, s => s.ToString()), GUILayout.Width(70));
                        m_NewSize = Sizes[size];
                        m_NewFillWhite = GUILayout.Toolbar(m_NewFillWhite ? 1 : 0, new[] { "Black", "White" }, GUILayout.Width(110)) == 1;
                        if (GUILayout.Button("Create mask texture"))
                            FairyToonMaskCanvas.CreateTexture(material, m_Property, m_NewSize, m_NewFillWhite ? 1f : 0f);
                    }
                    EditorGUILayout.EndScrollView();
                    return;
                }
            }

            EditorGUILayout.Space();
            var big = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 28 };
            if (!painting)
            {
                if (GUILayout.Button("Start Painting", big)) StartPainting();
            }
            else
            {
                var old = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.55f, 0.7f);
                if (GUILayout.Button(m_Canvas.Dirty ? "Stop Painting (unsaved)" : "Stop Painting", big)) StopPaintingWithPrompt();
                GUI.backgroundColor = old;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            m_Radius = EditorGUILayout.Slider("Radius (cm)", m_Radius * 100f, 0.05f, 20f) / 100f;
            m_Hardness = EditorGUILayout.Slider("Hardness", m_Hardness, 0f, 1f);
            m_Strength = EditorGUILayout.Slider("Strength", m_Strength, 0.01f, 1f);
            m_Value = EditorGUILayout.Slider("Value", m_Value, 0f, 1f);
            m_FrontOnly = EditorGUILayout.Toggle(new GUIContent("Front Faces Only", "Skip surfaces turned away from the brush (thin parts, the other side of the head)."), m_FrontOnly);

            EditorGUILayout.LabelField("View", EditorStyles.boldLabel);
            m_Overlay = EditorGUILayout.Toggle("Mask Overlay", m_Overlay);
            using (new EditorGUI.DisabledScope(!m_Overlay))
            {
                m_Tint = EditorGUILayout.ColorField("Overlay Colour", m_Tint);
                m_XRay = EditorGUILayout.Toggle(new GUIContent("Overlay Through Hair", "Draw the overlay over anything in front of it."), m_XRay);
            }
            bool isolate = EditorGUILayout.Toggle(new GUIContent("Isolate Renderer", "Hide everything else in the Scene view while painting."), m_Isolate);
            if (EditorGUI.EndChangeCheck())
            {
                if (isolate != m_Isolate) { m_Isolate = isolate; ApplyIsolation(); }
                SavePrefs();
                SceneView.RepaintAll();
            }

            if (painting)
            {
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!m_Canvas.CanUndo)) if (GUILayout.Button("Undo")) DoUndo(false);
                    using (new EditorGUI.DisabledScope(!m_Canvas.CanRedo)) if (GUILayout.Button("Redo")) DoUndo(true);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Fill Submesh", "Set the channel to Value over the whole material slot."))) { m_Canvas.PushUndo(); m_Canvas.FillAll(m_Value); SceneView.RepaintAll(); }
                    if (GUILayout.Button(new GUIContent("Clear Submesh", "Set the channel to 0 over the whole material slot."))) { m_Canvas.PushUndo(); m_Canvas.FillAll(0f); SceneView.RepaintAll(); }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!m_Canvas.Dirty))
                    {
                        if (GUILayout.Button("Revert") && EditorUtility.DisplayDialog("Mask Painter", "Drop all unsaved strokes?", "Revert", "Cancel"))
                        { m_Canvas.Revert(); SceneView.RepaintAll(); }
                        if (GUILayout.Button("Save", big)) { m_Status = "Saved " + m_Canvas.Save(); }
                    }
                }
                GUILayout.Label(m_Canvas.Texture, GUILayout.Width(position.width - 20), GUILayout.Height(position.width - 20));
            }

            EditorGUILayout.HelpBox(
                "LMB paint towards Value · Shift+LMB paint towards 0\n" +
                "Ctrl+LMB fill the UV island under the cursor · Ctrl+Shift+LMB clear it\n" +
                "[ / ] radius · Ctrl+Z / Ctrl+Y undo / redo · Alt+drag orbits as usual",
                MessageType.None);
            if (!string.IsNullOrEmpty(m_Status)) EditorGUILayout.HelpBox(m_Status, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void StartPainting()
        {
            try
            {
                m_Canvas = new FairyToonMaskCanvas(m_Renderer, m_Slot, m_Property, m_Channel);
                m_Status = null;
                ApplyIsolation();
                Tools.hidden = true;
                SceneView.RepaintAll();
            }
            catch (System.InvalidOperationException e) { m_Status = e.Message; }
        }

        private void StopPaintingWithPrompt()
        {
            if (m_Canvas != null && m_Canvas.Dirty)
            {
                int choice = EditorUtility.DisplayDialogComplex("Mask Painter", $"Save the changes to {m_Canvas.Source.name}?", "Save", "Cancel", "Discard");
                if (choice == 1) return;
                if (choice == 0) m_Status = "Saved " + m_Canvas.Save();
            }
            StopPainting();
        }

        private void StopPainting()
        {
            if (m_Canvas == null) return;
            m_Canvas.Dispose();
            m_Canvas = null;
            m_Stroking = false;
            Tools.hidden = false;
            if (m_Isolate) SceneVisibilityManager.instance.ExitIsolation();
            SceneView.RepaintAll();
            Repaint();
        }

        private void ApplyIsolation()
        {
            if (m_Canvas == null) return;
            if (m_Isolate) SceneVisibilityManager.instance.Isolate(m_Renderer.gameObject, false);
            else SceneVisibilityManager.instance.ExitIsolation();
        }

        private void DoUndo(bool redo)
        {
            if (redo) m_Canvas.Redo();
            else m_Canvas.Undo();
            SceneView.RepaintAll();
            Repaint();
        }

        // ---- Scene view ----

        private void OnSceneGUI(SceneView view)
        {
            if (m_Canvas == null) return;
            if (m_Renderer == null) { StopPainting(); return; }
            m_Canvas.Channel = m_Channel;
            Event e = Event.current;
            int id = GUIUtility.GetControlID(PainterHash, FocusType.Passive);

            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(id);
                    break;

                case EventType.MouseMove:
                    UpdateHit(e);
                    view.Repaint();
                    break;

                case EventType.MouseDown when e.button == 0 && !e.alt:
                    m_Canvas.RefreshPose();
                    UpdateHit(e);
                    if (!m_HasHit) break;
                    GUIUtility.hotControl = id;
                    m_Canvas.PushUndo();
                    if (e.control) m_Canvas.FillIsland(m_Hit.triangle, e.shift ? 0f : m_Value);
                    else
                    {
                        m_Stroking = true;
                        StampAt(m_Hit, e.shift);
                    }
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    UpdateHit(e);
                    if (m_Stroking && m_HasHit)
                    {
                        // (dabs every quarter radius, interpolated, so fast strokes stay continuous)
                        float step = Mathf.Max(m_Radius * 0.25f, 1e-5f);
                        float distance = Vector3.Distance(m_LastStamp, m_Hit.point);
                        if (distance >= step)
                        {
                            int count = Mathf.Min(64, Mathf.FloorToInt(distance / step));
                            Vector3 from = m_LastStamp;
                            for (int i = 1; i <= count; i++)
                            {
                                var dab = m_Hit;
                                dab.point = Vector3.Lerp(from, m_Hit.point, i / (float)count);
                                StampAt(dab, e.shift);
                            }
                        }
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    m_Stroking = false;
                    e.Use();
                    Repaint();
                    break;

                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.LeftBracket || e.keyCode == KeyCode.RightBracket)
                    {
                        m_Radius = Mathf.Clamp(m_Radius * (e.keyCode == KeyCode.LeftBracket ? 0.85f : 1.18f), 0.0005f, 0.2f);
                        e.Use();
                        Repaint();
                        view.Repaint();
                    }
                    else if ((e.control || e.command) && (e.keyCode == KeyCode.Z || e.keyCode == KeyCode.Y))
                    {
                        DoUndo(e.keyCode == KeyCode.Y || e.shift);
                        e.Use();
                    }
                    break;

                case EventType.Repaint:
                    DrawOverlay(view);
                    DrawCursor();
                    break;
            }
        }

        private void UpdateHit(Event e)
        {
            m_HasHit = m_Canvas.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out m_Hit);
        }

        private void StampAt(FairyToonMaskCanvas.Hit hit, bool erase)
        {
            m_Canvas.Stamp(hit.point, hit.normal, m_Radius, m_Hardness, m_Strength, erase ? 0f : m_Value, m_FrontOnly);
            m_LastStamp = hit.point;
        }

        private void DrawCursor()
        {
            if (!m_HasHit) return;
            Event e = Event.current;
            Color color = e.control ? new Color(0.4f, 0.9f, 1f) : e.shift ? new Color(1f, 0.6f, 0.2f) : Color.white;
            using (new Handles.DrawingScope(color))
            {
                Handles.DrawWireDisc(m_Hit.point, m_Hit.normal, m_Radius);
                Handles.color = new Color(color.r, color.g, color.b, 0.45f);
                Handles.DrawWireDisc(m_Hit.point, m_Hit.normal, m_Radius * m_Hardness);
                Handles.DrawLine(m_Hit.point, m_Hit.point + m_Hit.normal * m_Radius);
            }
        }

        private void DrawOverlay(SceneView view)
        {
            if (!m_Overlay || m_Canvas.PosedMesh == null) return;
            if (m_OverlayMaterial == null)
            {
                var shader = Shader.Find("Hidden/FairyToon/MaskOverlay");
                if (shader == null) return;
                m_OverlayMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            m_OverlayMaterial.SetTexture("_Mask", m_Canvas.Texture);
            m_OverlayMaterial.SetVector("_ChannelMask", Channels[m_Channel]);
            m_OverlayMaterial.SetColor("_Tint", m_Tint);
            m_OverlayMaterial.SetFloat("_ZTest", (float)(m_XRay ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual));
            Graphics.DrawMesh(m_Canvas.PosedMesh, m_Canvas.PosedMatrix, m_OverlayMaterial, 0, view.camera, m_Canvas.SubMesh);
        }

        // ---- Helpers ----

        private static List<string> TextureProperties(Shader shader)
        {
            var list = new List<string>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
                if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture
                    && shader.GetPropertyTextureDimension(i) == UnityEngine.Rendering.TextureDimension.Tex2D
                    && (shader.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) == 0)
                    list.Add(shader.GetPropertyName(i));
            return list;
        }

        private static string Label(Shader shader, string property)
        {
            int i = shader.FindPropertyIndex(property);
            return i < 0 ? property : shader.GetPropertyDescription(i);
        }
    }
}
