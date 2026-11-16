using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace NW.FairyToon.Editor
{
    /// <summary>Import-time outline normals in UV7.xyz, with corner coherence in UV7.w.</summary>
    public static class FairyToonSmoothNormals
    {
        private const string Flag = "FairyToonSmoothNormals";
        private const double WeldTolerance = 1e-4;
        private const string BakeMenu = "Assets/FairyToon/Bake Smooth Normals (UV7)";
        private const string RemoveMenu = "Assets/FairyToon/Remove Smooth Normals (UV7)";

        public static bool IsEnabled(string modelPath)
            => !string.IsNullOrEmpty(modelPath) && AssetImporter.GetAtPath(modelPath) is ModelImporter importer
                && HasFlag(importer.userData);

        /// <summary>Changes only the bake flag; reimporting restores the source before each bake or removal.</summary>
        public static void SetEnabled(string modelPath, bool on)
        {
            var importer = string.IsNullOrEmpty(modelPath) ? null : AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) throw new ArgumentException("Not a model asset: " + modelPath, nameof(modelPath));
            string data = importer.userData ?? string.Empty;
            if (on && !HasFlag(data))
                importer.userData = data + (data.Length == 0 || data.EndsWith("\n", StringComparison.Ordinal)
                    || data.EndsWith("\r", StringComparison.Ordinal) ? string.Empty : "\n") + Flag;
            else if (!on && HasFlag(data)) importer.userData = data.Replace(Flag, string.Empty);
            importer.SaveAndReimport();
        }

        internal static bool HasFlag(string data) => data != null && data.IndexOf(Flag, StringComparison.Ordinal) >= 0;

        [MenuItem(BakeMenu)]
        private static void BakeSelected() => SetSelected(true);

        [MenuItem(RemoveMenu)]
        private static void RemoveSelected() => SetSelected(false);

        [MenuItem(BakeMenu, true)]
        [MenuItem(RemoveMenu, true)]
        private static bool ValidateSelection()
        {
            if (Selection.objects.Length == 0) return false;
            foreach (var selected in Selection.objects)
                if (!AssetDatabase.IsMainAsset(selected)
                    || !(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(selected)) is ModelImporter)) return false;
            return true;
        }

        private static void SetSelected(bool on)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (AssetDatabase.IsMainAsset(selected) && AssetImporter.GetAtPath(path) is ModelImporter) paths.Add(path);
            }
            foreach (string path in paths)
            {
                try { SetEnabled(path, on); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        internal static void Bake(GameObject root, ModelImporter importer)
        {
            var timer = Stopwatch.StartNew();
            string path = importer.assetPath;
            if (importer.importNormals == ModelImporterNormals.Calculate)
                Debug.LogWarning($"[FairyToon] {path}: normals are Calculate; changing the smoothing angle can change the outline on reimport.");
            double tolerance = WeldTolerance * Math.Max(Math.Abs((double)importer.globalScale), 1e-8);
            var meshes = CollectMeshes(root);
            var points = new List<WeldedPoint>();
            var cells = new Dictionary<(long, long, long), List<int>>();
            long vertices = 0;
            foreach (var data in meshes)
            {
                vertices += data.positions.Length;
                for (int i = 0; i < data.positions.Length; i++)
                    data.welds[i] = Weld(data.positions[i], tolerance, cells, points);
            }
            foreach (var data in meshes) Accumulate(data, points, tolerance);
            foreach (var point in points)
            {
                point.normal = point.sum.Normalized;
                // Squaring maps the equal-weight 90-degree edge from sqrt(0.5) to 0.5.
                point.coherence = point.weight > 0.0
                    ? (float)Math.Min(1.0, point.sum.LengthSquared / (point.weight * point.weight)) : 0f;
            }
            long lowCoherence = 0;
            foreach (var data in meshes) lowCoherence += WriteUVs(data, points, path);
            timer.Stop();
            Debug.Log($"[FairyToon] {path}: smooth normals UV7, {meshes.Count} meshes, {vertices} vertices, {points.Count} welded points, {lowCoherence} vertices with coherence < 0.6, {timer.Elapsed.TotalMilliseconds:F1} ms.");
        }

        private static List<MeshData> CollectMeshes(GameObject root)
        {
            var result = new List<MeshData>();
            var seen = new HashSet<Mesh>();
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            void Add(Mesh mesh, Transform node, Action<Mesh> assign)
            {
                if (mesh == null || mesh.vertexCount == 0) return;
                // Instances can meet different neighbours, so each needs its own imported UV7.
                if (!seen.Add(mesh))
                {
                    mesh = UnityEngine.Object.Instantiate(mesh);
                    mesh.name = node.name + "_SmoothNormals";
                    assign(mesh);
                }
                result.Add(new MeshData(mesh, toRoot * node.localToWorldMatrix));
            }
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                Add(filter.sharedMesh, filter.transform, mesh => filter.sharedMesh = mesh);
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Add(renderer.sharedMesh, renderer.transform, mesh => renderer.sharedMesh = mesh);
            return result;
        }

        private static int Weld(Double3 position, double tolerance,
            Dictionary<(long, long, long), List<int>> cells, List<WeldedPoint> points)
        {
            long x = (long)Math.Floor(position.x / tolerance);
            long y = (long)Math.Floor(position.y / tolerance);
            long z = (long)Math.Floor(position.z / tolerance);
            int match = -1;
            double distance = tolerance * tolerance;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!cells.TryGetValue((x + dx, y + dy, z + dz), out var candidates)) continue;
                foreach (int index in candidates)
                {
                    double squared = (points[index].position - position).LengthSquared;
                    if (squared > distance || (squared == distance && match >= 0 && index > match)) continue;
                    distance = squared;
                    match = index;
                }
            }
            if (match >= 0) return match;
            var key = (x, y, z);
            if (!cells.TryGetValue(key, out var bucket)) cells.Add(key, bucket = new List<int>());
            int added = points.Count;
            bucket.Add(added);
            points.Add(new WeldedPoint { position = position });
            return added;
        }

        private static void Accumulate(MeshData data, List<WeldedPoint> points, double tolerance)
        {
            double minimumAreaSquared = Math.Pow(tolerance, 4.0) * 1e-12;
            double handedness = data.toRoot.determinant < 0f ? -1.0 : 1.0;
            for (int submesh = 0; submesh < data.mesh.subMeshCount; submesh++)
            {
                if (data.mesh.GetTopology(submesh) != MeshTopology.Triangles) continue;
                int[] triangles = data.mesh.GetTriangles(submesh);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    Double3 ab = data.positions[b] - data.positions[a];
                    Double3 ac = data.positions[c] - data.positions[a];
                    Double3 bc = data.positions[c] - data.positions[b];
                    Double3 cross = Double3.Cross(ab, ac);
                    double areaSquared = cross.LengthSquared;
                    if (areaSquared <= Math.Max(minimumAreaSquared, ab.LengthSquared * ac.LengthSquared * 1e-24)) continue;
                    double area = Math.Sqrt(areaSquared);
                    // Mirrored nodes still contribute outward normals in the common root space.
                    Double3 normal = cross * (handedness / area);
                    points[data.welds[a]].Add(normal, Math.Atan2(area, Double3.Dot(ab, ac)));
                    points[data.welds[b]].Add(normal, Math.Atan2(area, -Double3.Dot(ab, bc)));
                    points[data.welds[c]].Add(normal, Math.Atan2(area, Double3.Dot(ac, bc)));
                }
            }
        }

        private static long WriteUVs(MeshData data, List<WeldedPoint> points, string path)
        {
            Mesh mesh = data.mesh;
            if (mesh.HasVertexAttribute(VertexAttribute.TexCoord7))
                Debug.LogWarning($"[FairyToon] {path}: overwriting existing UV7 on {mesh.name}.");
            if (mesh.normals.Length != mesh.vertexCount)
            {
                mesh.RecalculateNormals();
                Debug.LogWarning($"[FairyToon] {path}: {mesh.name} has no normals; recalculated them for the tangent frame.");
            }
            if (mesh.tangents.Length != mesh.vertexCount)
            {
                mesh.RecalculateTangents();
                Debug.Log($"[FairyToon] {path}: {mesh.name} has no tangents; ran RecalculateTangents.");
            }
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            var uv7 = new List<Vector4>(mesh.vertexCount);
            Matrix4x4 toLocalNormal = data.toRoot.transpose;
            long lowCoherence = 0;
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                var point = points[data.welds[i]];
                var normal = new Double3(normals[i]);
                Double3 smooth = Double3.Transform(toLocalNormal, point.normal, 0.0).Normalized;
                if (smooth.LengthSquared == 0.0) smooth = normal.Normalized;
                Vector4 t = tangents[i];
                var tangent = new Double3(t.x, t.y, t.z);
                Double3 bitangent = Double3.Cross(normal, tangent) * t.w;
                uv7.Add(new Vector4((float)Double3.Dot(smooth, tangent), (float)Double3.Dot(smooth, bitangent),
                    (float)Double3.Dot(smooth, normal), point.coherence));
                if (point.coherence < 0.6f) lowCoherence++;
            }
            mesh.SetUVs(7, uv7);
            return lowCoherence;
        }

        private sealed class MeshData
        {
            public readonly Mesh mesh;
            public readonly Matrix4x4 toRoot;
            public readonly Double3[] positions;
            public readonly int[] welds;

            public MeshData(Mesh mesh, Matrix4x4 toRoot)
            {
                this.mesh = mesh;
                this.toRoot = toRoot;
                Vector3[] vertices = mesh.vertices;
                positions = new Double3[vertices.Length];
                welds = new int[vertices.Length];
                // Imported skinned vertices use the mesh node's bind-space transform, not the current bone pose.
                for (int i = 0; i < vertices.Length; i++)
                    positions[i] = Double3.Transform(toRoot, new Double3(vertices[i]), 1.0);
            }
        }

        private sealed class WeldedPoint
        {
            public Double3 position, sum, normal;
            public double weight;
            public float coherence;

            public void Add(Double3 faceNormal, double angle)
            {
                sum += faceNormal * angle;
                weight += angle;
            }
        }

        private readonly struct Double3
        {
            public readonly double x, y, z;
            public Double3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
            public Double3(Vector3 v) : this(v.x, v.y, v.z) { }
            public double LengthSquared => Dot(this, this);
            public Double3 Normalized => LengthSquared > 1e-30 ? this * (1.0 / Math.Sqrt(LengthSquared)) : default;
            public static Double3 operator +(Double3 a, Double3 b) => new Double3(a.x + b.x, a.y + b.y, a.z + b.z);
            public static Double3 operator -(Double3 a, Double3 b) => new Double3(a.x - b.x, a.y - b.y, a.z - b.z);
            public static Double3 operator *(Double3 v, double scale) => new Double3(v.x * scale, v.y * scale, v.z * scale);
            public static double Dot(Double3 a, Double3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
            public static Double3 Cross(Double3 a, Double3 b)
                => new Double3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
            public static Double3 Transform(Matrix4x4 m, Double3 v, double w)
                => new Double3(m.m00 * v.x + m.m01 * v.y + m.m02 * v.z + m.m03 * w,
                    m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13 * w,
                    m.m20 * v.x + m.m21 * v.y + m.m22 * v.z + m.m23 * w);
        }
    }

    internal sealed class FairyToonSmoothNormalsPostprocessor : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 100000;
        public override uint GetVersion() => 1;

        private void OnPostprocessModel(GameObject root)
        {
            if (assetImporter is ModelImporter importer && FairyToonSmoothNormals.HasFlag(importer.userData))
                FairyToonSmoothNormals.Bake(root, importer);
        }
    }
}
