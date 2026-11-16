using System.Collections.Generic;
using UnityEngine;

namespace NW.FairyToon
{
    /// <summary>Feeds the head frame to the hair slots used by FairyToon's see-through fringe.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FairyToonSeeThroughHair : MonoBehaviour
    {
        private static readonly int RoleId = Shader.PropertyToID("_SeeThroughRole");
        private static readonly int HeadId = Shader.PropertyToID("_SeeThroughHead");
        private static readonly int FaceForwardId = Shader.PropertyToID("_FaceForward");
        private static readonly int FaceRightId = Shader.PropertyToID("_FaceRight");

        [SerializeField] private Renderer faceRenderer;
        [SerializeField] private Renderer[] hairRenderers = System.Array.Empty<Renderer>();
        [Tooltip("Head bone. Empty: the Animator's humanoid head.")]
        [SerializeField] private Transform head;
        [SerializeField, HideInInspector] private Vector3 localForward;
        [SerializeField, HideInInspector] private Vector3 localRight;

        private readonly List<(Renderer renderer, int index)> targets = new List<(Renderer, int)>();
        private readonly List<Material> materials = new List<Material>();
        private MaterialPropertyBlock block;
        private FairyToonFaceShadow faceShadow;

        public Renderer FaceRenderer
        {
            get => faceRenderer;
            set => faceRenderer = value;
        }

        public Renderer[] HairRenderers
        {
            get => hairRenderers;
            set
            {
                hairRenderers = value ?? System.Array.Empty<Renderer>();
                if (isActiveAndEnabled) Refresh();
            }
        }

        public Transform Head
        {
            get => head;
            set
            {
                if (head == value) return;
                ClearHeadFrame();
                head = value;
                localForward = localRight = Vector3.zero;
                if (isActiveAndEnabled)
                {
                    InitializeHead();
                    WriteHeadFrame();
                }
            }
        }

        private void OnEnable()
        {
            InitializeHead();
            Refresh();
        }

        private void OnValidate()
        {
            hairRenderers ??= System.Array.Empty<Renderer>();
            if (!isActiveAndEnabled) return;
            InitializeHead();
            Refresh();
        }

        private void OnDisable()
        {
            ClearHeadFrame();
            targets.Clear();
        }

        private void InitializeHead()
        {
            block ??= new MaterialPropertyBlock();
            faceShadow = GetComponent<FairyToonFaceShadow>();
            if (head == null)
            {
                var animator = GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman) head = animator.GetBoneTransform(HumanBodyBones.Head);
            }
            if (head != null && (localForward == Vector3.zero || localRight == Vector3.zero))
            {
                localForward = head.InverseTransformDirection(transform.forward).normalized;
                localRight = head.InverseTransformDirection(transform.right).normalized;
            }
        }

        /// <summary>Rescans Hair-role slots after material swaps and updates their head frame.</summary>
        public void Refresh()
        {
            ClearHeadFrame();
            targets.Clear();
            if (!isActiveAndEnabled) return;
            InitializeHead();
            if (hairRenderers == null) return;
            foreach (var renderer in hairRenderers)
            {
                if (renderer == null) continue;
                renderer.GetSharedMaterials(materials);
                for (int i = 0; i < materials.Count; i++)
                {
                    var material = materials[i];
                    if (material != null && material.HasProperty(RoleId)
                        && Mathf.RoundToInt(material.GetFloat(RoleId)) == 2 && !targets.Contains((renderer, i)))
                        targets.Add((renderer, i));
                }
            }
            WriteHeadFrame();
        }

        private void LateUpdate() => WriteHeadFrame();

        private void WriteHeadFrame()
        {
            if (head == null || targets.Count == 0) return;
            Vector3 forward, right;
            if (faceShadow == null || !faceShadow.TryGetFaceAxes(out forward, out right))
            {
                forward = head.TransformDirection(localForward).normalized;
                right = head.TransformDirection(localRight).normalized;
            }
            Vector3 position = head.position;
            Vector4 headFrame = new Vector4(position.x, position.y, position.z, 1f);
            foreach (var (renderer, index) in targets)
            {
                if (renderer == null) continue;
                renderer.GetSharedMaterials(materials);
                if (index >= materials.Count) continue;
                var material = materials[index];
                if (material == null || !material.HasProperty(RoleId) || Mathf.RoundToInt(material.GetFloat(RoleId)) != 2) continue;
                renderer.GetPropertyBlock(block, index);
                block.SetVector(HeadId, headFrame);
                block.SetVector(FaceForwardId, forward);
                block.SetVector(FaceRightId, right);
                renderer.SetPropertyBlock(block, index);
            }
        }

        private void ClearHeadFrame()
        {
            if (block == null) return;
            foreach (var (renderer, index) in targets)
            {
                if (renderer == null) continue;
                renderer.GetSharedMaterials(materials);
                if (index >= materials.Count) continue;
                renderer.GetPropertyBlock(block, index);
                block.SetVector(HeadId, Vector4.zero);
                block.SetVector(FaceForwardId, Vector4.zero);
                block.SetVector(FaceRightId, Vector4.zero);
                renderer.SetPropertyBlock(block, index);
            }
        }
    }
}
