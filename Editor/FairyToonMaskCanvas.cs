using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NW.FairyToon.Editor
{
    /// <summary>The Mask Painter's working copy of one texture channel on one renderer slot: GPU brush, island fill,
    /// undo, live preview and save. The window drives it; scripts can too.</summary>
    /// <remarks>Painting happens in UV space on a render texture: each triangle of the slot's submesh is drawn at its
    /// UV position with its posed world position, so the brush is a world-space sphere and UV seams do not matter.
    /// While open, the renderer shows the render texture through a property block on that slot.</remarks>
    public sealed class FairyToonMaskCanvas : IDisposable
    {
        public struct Hit
        {
            public Vector3 point;
            public Vector3 normal;
            public int triangle;    // index into the submesh's triangle list (0, 1, 2, …)
            public Vector2 uv;
        }

        private const int MaxUndo = 24;
        private const int DilatePixels = 4;
        private static readonly Vector4[] Channels = { new Vector4(1, 0, 0, 0), new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 0), new Vector4(0, 0, 0, 1) };

        private static readonly int SrcId = Shader.PropertyToID("_Src");
        private static readonly int CoverageId = Shader.PropertyToID("_Coverage");
        private static readonly int BrushPosId = Shader.PropertyToID("_BrushPos");
        private static readonly int BrushParamsId = Shader.PropertyToID("_BrushParams");
        private static readonly int BrushNormalId = Shader.PropertyToID("_BrushNormal");
        private static readonly int ChannelMaskId = Shader.PropertyToID("_ChannelMask");
        private static readonly int DilateRadiusId = Shader.PropertyToID("_DilateRadius");

        public Renderer Renderer { get; }
        public int Slot { get; }
        public int SubMesh { get; }
        public Material Material { get; }
        public string Property { get; }
        public Texture2D Source { get; private set; }
        public RenderTexture Texture { get; private set; }
        public int Channel { get; set; }
        public bool Dirty { get; private set; }
        public bool Linear { get; }
        public Mesh PosedMesh => m_Mesh;
        public Matrix4x4 PosedMatrix => m_Matrix;

        private readonly Material m_Paint;
        private readonly Mesh m_Mesh;
        private readonly bool m_OwnsMesh;
        private Matrix4x4 m_Matrix;
        private Vector3[] m_World;
        private Vector2[] m_UV;
        private int[] m_Triangles;
        private int[] m_Island;     // per submesh triangle: island id (built on first use)
        private RenderTexture m_Scratch;
        private readonly List<RenderTexture> m_Undo = new List<RenderTexture>();
        private readonly List<RenderTexture> m_Redo = new List<RenderTexture>();
        private MaterialPropertyBlock m_Block;

        /// <summary>Opens the texture in <paramref name="property"/> of the material in <paramref name="slot"/>.</summary>
        /// <exception cref="InvalidOperationException">No texture is assigned, or the renderer has no mesh.</exception>
        public FairyToonMaskCanvas(Renderer renderer, int slot, string property, int channel)
        {
            Renderer = renderer ? renderer : throw new ArgumentNullException(nameof(renderer));
            var materials = renderer.sharedMaterials;
            if (slot < 0 || slot >= materials.Length || materials[slot] == null) throw new InvalidOperationException("No material in slot " + slot);
            Slot = slot;
            Material = materials[slot];
            Property = property;
            Channel = Mathf.Clamp(channel, 0, 3);
            if (!Material.HasProperty(property)) throw new InvalidOperationException($"{Material.name} has no {property}");
            Source = Material.GetTexture(property) as Texture2D;
            if (Source == null) throw new InvalidOperationException($"{Material.name}.{property} has no 2D texture; create one first.");

            switch (renderer)
            {
                case SkinnedMeshRenderer skinned when skinned.sharedMesh != null:
                    m_Mesh = new Mesh { name = skinned.sharedMesh.name + " (posed)", hideFlags = HideFlags.HideAndDontSave };
                    m_OwnsMesh = true;
                    break;
                case MeshRenderer _ when renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null:
                    m_Mesh = filter.sharedMesh;
                    break;
                default:
                    throw new InvalidOperationException(renderer.name + " has no mesh");
            }
            SubMesh = Mathf.Min(slot, (m_OwnsMesh ? ((SkinnedMeshRenderer)renderer).sharedMesh : m_Mesh).subMeshCount - 1);

            var shader = Shader.Find("Hidden/FairyToon/MaskPaint");
            if (shader == null) throw new InvalidOperationException("Hidden/FairyToon/MaskPaint shader is missing");
            m_Paint = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            string path = AssetDatabase.GetAssetPath(Source);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Linear = importer == null || !importer.sRGBTexture;
            int width = Source.width, height = Source.height;
            if (importer != null) importer.GetSourceTextureWidthAndHeight(out width, out height);
            Texture = NewTarget(width, height, Source.name + " (painting)");
            m_Scratch = NewTarget(width, height, "Mask scratch");
            LoadSource(path);
            RefreshPose();
            BeginPreview();
        }

        /// <summary>Creates a mask PNG next to the material, imported as linear data, and assigns it.</summary>
        public static Texture2D CreateTexture(Material material, string property, int size, float fill, string path = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(material)) ?? "Assets";
                path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{material.name}{property}.png".Replace("\\", "/"));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[size * size];
            byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(fill) * 255f);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(v, v, v, 255);
            texture.SetPixels32(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, size);
                importer.SaveAndReimport();
            }
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            UnityEditor.Undo.RecordObject(material, "Create mask texture");
            material.SetTexture(property, asset);
            EditorUtility.SetDirty(material);
            return asset;
        }

        // ---- Pose and picking ----

        /// <summary>Re-bakes the renderer's current pose for picking and painting. Call when the pose may have changed.</summary>
        public void RefreshPose()
        {
            if (Renderer is SkinnedMeshRenderer skinned)
            {
                skinned.BakeMesh(m_Mesh, false);
                // (BakeMesh leaves the vertices in the renderer's space with or without its scale depending on the
                // setup; pick the matrix whose result matches the renderer's own world bounds)
                Transform t = skinned.transform;
                var withScale = t.localToWorldMatrix;
                var noScale = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                m_Mesh.RecalculateBounds();
                Vector3 target = skinned.bounds.size;
                float ErrorOf(Matrix4x4 m) => (TransformBounds(m, m_Mesh.bounds).size - target).sqrMagnitude;
                m_Matrix = ErrorOf(withScale) <= ErrorOf(noScale) ? withScale : noScale;
            }
            else m_Matrix = Renderer.transform.localToWorldMatrix;

            var local = m_Mesh.vertices;
            m_World = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) m_World[i] = m_Matrix.MultiplyPoint3x4(local[i]);
            m_UV = m_Mesh.uv;
            m_Triangles = m_Mesh.GetTriangles(SubMesh);
        }

        /// <summary>Closest hit on the slot's submesh only, so other submeshes and other objects never block the brush.</summary>
        public bool Raycast(Ray ray, out Hit hit)
        {
            hit = default;
            if (m_UV == null || m_UV.Length != m_World.Length) return false;
            float best = float.MaxValue;
            for (int t = 0; t < m_Triangles.Length; t += 3)
            {
                Vector3 a = m_World[m_Triangles[t]], b = m_World[m_Triangles[t + 1]], c = m_World[m_Triangles[t + 2]];
                if (!IntersectTriangle(ray, a, b, c, out float distance, out float u, out float v) || distance >= best) continue;
                best = distance;
                hit.point = ray.GetPoint(distance);
                hit.normal = Vector3.Cross(b - a, c - a).normalized;
                hit.triangle = t / 3;
                hit.uv = m_UV[m_Triangles[t]] * (1f - u - v) + m_UV[m_Triangles[t + 1]] * u + m_UV[m_Triangles[t + 2]] * v;
            }
            return best < float.MaxValue;
        }

        // ---- Painting ----

        /// <summary>One brush dab: moves the selected channel towards <paramref name="value"/> inside a world-space sphere.</summary>
        public void Stamp(Vector3 center, Vector3 normal, float radius, float hardness, float strength, float value, bool frontOnly)
        {
            m_Paint.SetVector(BrushPosId, new Vector4(center.x, center.y, center.z, Mathf.Max(radius, 1e-5f)));
            m_Paint.SetVector(BrushParamsId, new Vector4(hardness, strength, value, 0f));
            m_Paint.SetVector(BrushNormalId, new Vector4(normal.x, normal.y, normal.z, frontOnly ? 1f : 0f));
            Draw(m_Mesh, SubMesh);
        }

        /// <summary>Sets the selected channel to <paramref name="value"/> on the UV island (vertex-connected triangles)
        /// that holds <paramref name="triangle"/>: the face-triangle selection quick start.</summary>
        public void FillIsland(int triangle, float value)
        {
            BuildIslands();
            if (triangle < 0 || triangle >= m_Island.Length) return;
            int island = m_Island[triangle];
            var indices = new List<int>();
            for (int t = 0; t < m_Island.Length; t++)
                if (m_Island[t] == island)
                {
                    indices.Add(m_Triangles[t * 3]);
                    indices.Add(m_Triangles[t * 3 + 1]);
                    indices.Add(m_Triangles[t * 3 + 2]);
                }
            var part = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = m_Mesh.indexFormat };
            part.vertices = m_Mesh.vertices;
            part.normals = m_Mesh.normals;
            part.uv = m_UV;
            part.SetTriangles(indices, 0);
            Fill(part, 0, value);
            Object.DestroyImmediate(part);
        }

        /// <summary>Sets the selected channel to <paramref name="value"/> over the whole submesh.</summary>
        public void FillAll(float value) => Fill(m_Mesh, SubMesh, value);

        private void Fill(Mesh mesh, int subMesh, float value)
        {
            m_Paint.SetVector(BrushParamsId, new Vector4(1f, 1f, value, 1f));
            m_Paint.SetVector(BrushNormalId, Vector4.zero);
            Draw(mesh, subMesh);
        }

        private void Draw(Mesh mesh, int subMesh)
        {
            m_Paint.SetVector(ChannelMaskId, Channels[Channel]);
            m_Paint.SetTexture(SrcId, m_Scratch);
            var cmd = new CommandBuffer { name = "FairyToon mask paint" };
            cmd.Blit(Texture, m_Scratch);
            cmd.SetRenderTarget(Texture);
            cmd.DrawMesh(mesh, m_Matrix, m_Paint, subMesh, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            Dirty = true;
        }

        // ---- Undo ----

        /// <summary>Remembers the current state; call once at the start of each stroke or fill.</summary>
        public void PushUndo()
        {
            m_Undo.Add(Copy(Texture));
            if (m_Undo.Count > MaxUndo) { Release(m_Undo[0]); m_Undo.RemoveAt(0); }
            foreach (var rt in m_Redo) Release(rt);
            m_Redo.Clear();
        }

        public bool CanUndo => m_Undo.Count > 0;
        public bool CanRedo => m_Redo.Count > 0;

        public void Undo() => Swap(m_Undo, m_Redo);
        public void Redo() => Swap(m_Redo, m_Undo);

        private void Swap(List<RenderTexture> from, List<RenderTexture> to)
        {
            if (from.Count == 0) return;
            to.Add(Copy(Texture));
            var state = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Graphics.Blit(state, Texture);
            Release(state);
            Dirty = true;
        }

        // ---- Save / revert ----

        /// <summary>Reloads the texture file and drops the changes (undo history included).</summary>
        public void Revert()
        {
            LoadSource(AssetDatabase.GetAssetPath(Source));
            foreach (var rt in m_Undo) Release(rt);
            foreach (var rt in m_Redo) Release(rt);
            m_Undo.Clear();
            m_Redo.Clear();
            Dirty = false;
        }

        /// <summary>Writes the texture back. PNG sources are overwritten in place (GUID and import settings kept); other
        /// formats are saved as a new PNG beside them and assigned to the material. Returns the written asset path.</summary>
        public string Save()
        {
            string path = AssetDatabase.GetAssetPath(Source);
            bool png = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            if (!png) path = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(path, null) + "_painted.png");

            var result = NewTarget(Texture.width, Texture.height, "Mask save");
            var coverage = NewTarget(Texture.width, Texture.height, "Mask coverage");
            var cmd = new CommandBuffer { name = "FairyToon mask save" };
            cmd.SetRenderTarget(coverage);
            cmd.ClearRenderTarget(false, true, Color.clear);
            // (coverage of every submesh: texels another slot uses are never touched by the dilation)
            for (int s = 0; s < m_Mesh.subMeshCount; s++) cmd.DrawMesh(m_Mesh, m_Matrix, m_Paint, s, 1);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            m_Paint.SetTexture(CoverageId, coverage);
            m_Paint.SetFloat(DilateRadiusId, DilatePixels);
            m_Paint.SetTexture(SrcId, Texture);
            Graphics.Blit(Texture, result, m_Paint, 2);

            var previous = RenderTexture.active;
            RenderTexture.active = result;
            var readback = new Texture2D(result.width, result.height, TextureFormat.RGBA32, false, Linear);
            readback.ReadPixels(new Rect(0, 0, result.width, result.height), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, readback.EncodeToPNG());
            Object.DestroyImmediate(readback);
            Release(result);
            Release(coverage);

            AssetDatabase.ImportAsset(path);
            if (!png)
            {
                Source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                UnityEditor.Undo.RecordObject(Material, "Assign painted mask");
                Material.SetTexture(Property, Source);
                EditorUtility.SetDirty(Material);
            }
            Dirty = false;
            return path;
        }

        // ---- Preview ----

        private void BeginPreview()
        {
            m_Block ??= new MaterialPropertyBlock();
            Renderer.GetPropertyBlock(m_Block, Slot);
            m_Block.SetTexture(Property, Texture);
            Renderer.SetPropertyBlock(m_Block, Slot);
        }

        private void EndPreview()
        {
            if (Renderer == null) return;
            // (a property block cannot drop one entry: clear the slot and let the components that own the other
            // entries, such as FairyToonFaceShadow, write theirs again on the next player-loop tick)
            Renderer.SetPropertyBlock(null, Slot);
            EditorApplication.QueuePlayerLoopUpdate();
        }

        public void Dispose()
        {
            EndPreview();
            foreach (var rt in m_Undo) Release(rt);
            foreach (var rt in m_Redo) Release(rt);
            m_Undo.Clear();
            m_Redo.Clear();
            Release(Texture);
            Release(m_Scratch);
            Texture = null;
            m_Scratch = null;
            if (m_OwnsMesh && m_Mesh != null) Object.DestroyImmediate(m_Mesh);
            if (m_Paint != null) Object.DestroyImmediate(m_Paint);
        }

        // ---- Helpers ----

        private void LoadSource(string path)
        {
            // (PNG / JPG are read from disk so the work copy has the file's exact pixels, not the import's size or
            // compression; anything else is copied from the imported texture)
            Texture2D file = null;
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                file = new Texture2D(2, 2, TextureFormat.RGBA32, false, Linear);
                if (!file.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(file); file = null; }
            }
            Graphics.Blit(file != null ? file : Source, Texture);
            if (file != null) Object.DestroyImmediate(file);
        }

        private RenderTexture NewTarget(int width, int height, string name)
        {
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, Linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        private RenderTexture Copy(RenderTexture source)
        {
            var rt = NewTarget(source.width, source.height, "Mask undo");
            Graphics.Blit(source, rt);
            return rt;
        }

        private static void Release(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        private void BuildIslands()
        {
            if (m_Island != null && m_Island.Length == m_Triangles.Length / 3) return;
            var parent = new int[m_World.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            for (int t = 0; t < m_Triangles.Length; t += 3)
            {
                int a = Find(m_Triangles[t]);
                parent[Find(m_Triangles[t + 1])] = a;
                parent[Find(m_Triangles[t + 2])] = a;
            }
            m_Island = new int[m_Triangles.Length / 3];
            for (int t = 0; t < m_Island.Length; t++) m_Island[t] = Find(m_Triangles[t * 3]);
        }

        private static Bounds TransformBounds(Matrix4x4 m, Bounds b)
        {
            var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return result;
        }

        // Möller–Trumbore, both faces.
        private static bool IntersectTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance, out float u, out float v)
        {
            distance = u = v = 0f;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            Vector3 s = ray.origin - a;
            u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            v = Vector3.Dot(ray.direction, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(e2, q) * inv;
            return distance > 0f;
        }
    }
}
