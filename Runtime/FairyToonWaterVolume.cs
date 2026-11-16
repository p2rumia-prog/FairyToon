using System.Collections.Generic;
using UnityEngine;

namespace NW.FairyToon
{
    /// <summary>A polled water footprint. Its box's world-bounds top is the surface; no Rigidbody is needed.</summary>
    /// <remarks>Supports yaw and scale. X/Z tilt is ignored for the footprint. There is deliberately no bottom test.</remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class FairyToonWaterVolume : MonoBehaviour
    {
        private static readonly List<FairyToonWaterVolume> Volumes = new List<FairyToonWaterVolume>();
        private BoxCollider boxCollider;

        private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        private void OnEnable()
        {
            boxCollider = GetComponent<BoxCollider>();
            if (!Volumes.Contains(this)) Volumes.Add(this);
        }

        private void OnDisable() => Volumes.Remove(this);

        /// <summary>Finds the highest surface whose XZ footprint contains the point, up to 0.5 m above its top.</summary>
        public static bool TryGetSurface(Vector3 worldPos, out float surfaceY)
        {
            surfaceY = -100000f;
            bool found = false;
            for (int i = Volumes.Count - 1; i >= 0; --i)
            {
                var volume = Volumes[i];
                if (volume == null) { Volumes.RemoveAt(i); continue; }
                if (!volume.isActiveAndEnabled || volume.boxCollider == null || !volume.boxCollider.enabled) continue;
                if (!volume.Contains(worldPos, out float top)) continue;
                if (!found || top > surfaceY) surfaceY = top;
                found = true;
            }
            return found;
        }

        private bool Contains(Vector3 point, out float top)
        {
            GetFootprint(out Vector3 center, out Vector3 half, out float c, out float s, out top);
            if (point.y > top + 0.5f) return false;
            float dx = point.x - center.x, dz = point.z - center.z;
            return Mathf.Abs(dx * c - dz * s) <= half.x && Mathf.Abs(dx * s + dz * c) <= half.z;
        }

        private void GetFootprint(out Vector3 center, out Vector3 half, out float c, out float s, out float top)
        {
            center = transform.TransformPoint(boxCollider.center);
            Vector3 scale = transform.lossyScale;
            half = new Vector3(Mathf.Abs(boxCollider.size.x * scale.x), Mathf.Abs(boxCollider.size.y * scale.y),
                Mathf.Abs(boxCollider.size.z * scale.z)) * 0.5f;
            float yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
            c = Mathf.Cos(yaw);
            s = Mathf.Sin(yaw);
            top = boxCollider.bounds.max.y;
        }

        private void OnDrawGizmos()
        {
            if (boxCollider == null) boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null || !boxCollider.enabled) return;
            GetFootprint(out Vector3 center, out Vector3 half, out float c, out float s, out float top);
            center.y = top;
            Vector3 x = new Vector3(c * half.x, 0f, -s * half.x);
            Vector3 z = new Vector3(s * half.z, 0f, c * half.z);
            Gizmos.color = new Color(0.15f, 0.75f, 1f, 0.8f);
            Gizmos.DrawLine(center + x + z, center - x + z);
            Gizmos.DrawLine(center - x + z, center - x - z);
            Gizmos.DrawLine(center - x - z, center + x - z);
            Gizmos.DrawLine(center + x - z, center + x + z);
        }
    }
}
