using System.Collections.Generic;
using UnityEngine;

namespace NW.FairyToon
{
    /// <summary>Feeds the head frame (face forward / right, head-bone position) to FairyToon materials that need it:
    /// face materials (Mask Type = Face SDF) and Endfield Hair / Eye parts.</summary>
    /// <remarks>Put it on the avatar root. The face axes are stored in head-bone space, captured once from the root's
    /// forward / right while the avatar is in its rest pose, so they follow the head when it turns. The vectors go in
    /// a per-material property block, which takes the renderer out of the SRP Batcher.</remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FairyToonFaceShadow : MonoBehaviour
    {
        private static readonly int FaceForwardId = Shader.PropertyToID("_FaceForward");
        private static readonly int FaceRightId = Shader.PropertyToID("_FaceRight");
        private static readonly int HeadId = Shader.PropertyToID("_SeeThroughHead");
        private static readonly int MaskTypeId = Shader.PropertyToID("_ShadowMaskType");
        private static readonly int ShadingModelId = Shader.PropertyToID("_ShadingModel");
        private static readonly int PartId = Shader.PropertyToID("_EFPart");
        private const int PartHair = 3, PartEye = 4;

        [Tooltip("Head bone. Empty: the Animator's humanoid head.")]
        [SerializeField] private Transform head;
        [Tooltip("Face forward in head-bone space. Zero: captured from this object's forward on enable.")]
        [SerializeField] private Vector3 localForward;
        [Tooltip("Face right in head-bone space. Zero: captured from this object's right on enable.")]
        [SerializeField] private Vector3 localRight;

        private readonly List<(Renderer renderer, int index)> targets = new List<(Renderer, int)>();
        private readonly List<Material> materials = new List<Material>();
        private MaterialPropertyBlock block;

        public Transform Head => head;

        /// <summary>Gets the captured face axes in world space for other head effects.</summary>
        public bool TryGetFaceAxes(out Vector3 forward, out Vector3 right)
        {
            forward = right = Vector3.zero;
            if (head == null || localForward == Vector3.zero || localRight == Vector3.zero) return false;
            forward = head.TransformDirection(localForward).normalized;
            right = head.TransformDirection(localRight).normalized;
            return true;
        }

        private void OnEnable()
        {
            block ??= new MaterialPropertyBlock();
            if (head == null)
            {
                var animator = GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman) head = animator.GetBoneTransform(HumanBodyBones.Head);
            }
            if (head != null && (localForward == Vector3.zero || localRight == Vector3.zero)) CaptureAxes();
            Refresh();
        }

        private void OnDisable()
        {
            foreach (var (renderer, index) in targets)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block, index);
                block.SetVector(FaceForwardId, Vector4.zero);
                block.SetVector(FaceRightId, Vector4.zero);
                block.SetVector(HeadId, Vector4.zero);
                renderer.SetPropertyBlock(block, index);
            }
            targets.Clear();
        }

        /// <summary>Stores the root's current forward / right in head-bone space. Call with the avatar in rest pose.</summary>
        [ContextMenu("Capture Axes From Root")]
        public void CaptureAxes()
        {
            if (head == null) return;
            localForward = head.InverseTransformDirection(transform.forward).normalized;
            localRight = head.InverseTransformDirection(transform.right).normalized;
        }

        /// <summary>Finds the renderer slots that need the head frame. Call after swapping materials.</summary>
        public void Refresh()
        {
            targets.Clear();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetSharedMaterials(materials);
                for (int i = 0; i < materials.Count; i++)
                    if (NeedsHeadFrame(materials[i])) targets.Add((renderer, i));
            }
        }

        private static bool NeedsHeadFrame(Material material)
        {
            if (material == null || !material.HasProperty(FaceForwardId)) return false;
            if (material.HasProperty(MaskTypeId) && Mathf.RoundToInt(material.GetFloat(MaskTypeId)) == 2) return true;
            if (!material.HasProperty(ShadingModelId) || !material.HasProperty(PartId)) return false;
            int part = Mathf.RoundToInt(material.GetFloat(PartId));
            return Mathf.RoundToInt(material.GetFloat(ShadingModelId)) == 1 && (part == PartHair || part == PartEye);
        }

        private void LateUpdate()
        {
            if (head == null || targets.Count == 0) return;
            Vector3 forward = head.TransformDirection(localForward).normalized;
            Vector3 right = head.TransformDirection(localRight).normalized;
            Vector3 position = head.position;
            var headPosition = new Vector4(position.x, position.y, position.z, 1f);
            foreach (var (renderer, index) in targets)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block, index);
                block.SetVector(FaceForwardId, forward);
                block.SetVector(FaceRightId, right);
                block.SetVector(HeadId, headPosition);
                renderer.SetPropertyBlock(block, index);
            }
        }
    }
}
