using UnityEngine;

namespace NW.FairyToon
{
    /// <summary>Root-relative soak memory and rain for one FairyToon character. Polls water; no game-code dependencies.</summary>
    /// <remarks>Uses shader globals: only one enabled wet character may write them at a time.</remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FairyToonWetness : MonoBehaviour
    {
        [Tooltip("Seconds for full wetness to dry.")]
        [Min(0f)] public float dryTime = 90f;
        [Tooltip("Metres of wicking above the water surface.")]
        [Min(0f)] public float wickMargin = 0.04f;
        [Tooltip("Wetness gained per second while submerged.")]
        [Min(0f)] public float soakSpeed = 4f;
        [Tooltip("Fraction of the departure soak height lost before fully dry.")]
        [Range(0f, 1f)] public float lineSink = 0.3f;
        [Tooltip("Softness of the soak line, in metres.")]
        [Min(0.001f)] public float edgeWidth = 0.03f;
        [Tooltip("Rain intensity; game code may set this.")]
        [Range(0f, 1f)] public float rain;
        [Tooltip("Seconds to gain one unit of rain wetness.")]
        [Min(0f)] public float rainSoakTime = 20f;
        [Tooltip("Disable for deterministic captures: the four state properties remain fixed.")]
        public bool simulate = true;
        [Tooltip("Opt in to wetness simulation while editing. Off keeps serialized scene state unchanged.")]
        public bool simulateInEditMode;

        [SerializeField] private float soakHeight;
        [SerializeField] private float wetness;
        [SerializeField] private float rainWetness;
        [SerializeField] private float timeSinceLeft;

        [Tooltip("Animator whose humanoid limb bones carry the water trails. Empty: the first Animator on or under this object.")]
        public Animator trailAnimator;

        private static readonly int Wet0Id = Shader.PropertyToID("_FT_Wet0");
        private static readonly int Wet1Id = Shader.PropertyToID("_FT_Wet1");
        private static readonly int BoneStartId = Shader.PropertyToID("_FT_WetBoneStart");
        private static readonly int BoneEndId = Shader.PropertyToID("_FT_WetBoneEnd");
        private static readonly int BoneSideId = Shader.PropertyToID("_FT_WetBoneSide");

        // Trail bones: water runs from the first joint towards the second, in that bone's own frame, so the trails
        // stay on the limb when it moves. w of the start is the limb radius used to lay trails around it.
        public const int MaxTrailBones = 10;
        private static readonly (HumanBodyBones from, HumanBodyBones to, float radius)[] TrailBones =
        {
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.075f),
            (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.05f),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.075f),
            (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.05f),
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.04f),
            (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.032f),
            (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.04f),
            (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.032f),
            (HumanBodyBones.Neck, HumanBodyBones.Hips, 0.13f),
            (HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, 0.0f), // placeholder slot, unused (radius 0)
        };
        private readonly Vector4[] boneStart = new Vector4[MaxTrailBones];
        private readonly Vector4[] boneEnd = new Vector4[MaxTrailBones];
        private readonly Vector4[] boneSide = new Vector4[MaxTrailBones];
        private readonly Transform[] boneFrom = new Transform[MaxTrailBones];
        private readonly Transform[] boneTo = new Transform[MaxTrailBones];
        private Animator resolvedAnimator;
        private bool hasOverride;
        private float overrideY;
        private bool drying;
        private float dryStartHeight, dryStartWetness;
        private double lastUpdateTime;

        public float SoakHeight
        {
            get => soakHeight;
            set { soakHeight = Mathf.Max(0f, Finite(value)); drying = false; }
        }
        public float Wetness
        {
            get => wetness;
            set { wetness = Mathf.Clamp01(Finite(value)); drying = false; }
        }
        public float RainWetness
        {
            get => rainWetness;
            set => rainWetness = Mathf.Clamp01(Finite(value));
        }
        public float TimeSinceLeft
        {
            get => timeSinceLeft;
            set => timeSinceLeft = Mathf.Max(0f, Finite(value));
        }
        public bool HasWaterSurface { get; private set; }
        public float WaterSurfaceY { get; private set; } = -100000f;

        public void SetWaterSurface(float worldY)
        {
            if (float.IsNaN(worldY) || float.IsInfinity(worldY)) { ClearWaterSurface(); return; }
            overrideY = worldY;
            hasOverride = true;
        }

        public void ClearWaterSurface() => hasOverride = false;

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;

        private void OnEnable()
        {
            lastUpdateTime = Time.realtimeSinceStartupAsDouble;
            drying = false;
            Push();
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            // ExecuteAlways only receives editor Update when the scene updates. Do not force editor updates,
            // and never consume the entire wall-clock gap after the editor was idle or a capture was frozen.
            bool playing = Application.IsPlaying(gameObject);
            float delta = playing ? Mathf.Max(0f, Time.deltaTime)
                : Mathf.Clamp((float)(now - lastUpdateTime), 0f, 0.1f);
            lastUpdateTime = now;
            if (!simulate || (!playing && !simulateInEditMode) || delta <= 0f) return;
            ResolveSurface();
            if (HasWaterSurface && WaterSurfaceY > transform.position.y)
            {
                soakHeight = Mathf.Max(soakHeight, WaterSurfaceY - transform.position.y + Mathf.Max(0f, wickMargin));
                wetness = Mathf.MoveTowards(wetness, 1f, Mathf.Max(0f, soakSpeed) * delta);
                timeSinceLeft = 0f;
                drying = false;
            }
            else
            {
                if (!drying)
                {
                    dryStartHeight = soakHeight;
                    dryStartWetness = wetness;
                    drying = true;
                }
                wetness = Mathf.MoveTowards(wetness, 0f, dryTime > 0f ? delta / dryTime : 1f);
                timeSinceLeft += delta;
                // Anchor at departure, not last frame: linear sinking without exponential/frame-rate drift.
                float dryProgress = dryStartWetness > 0f ? 1f - wetness / dryStartWetness : 1f;
                soakHeight = wetness > 0f ? dryStartHeight * (1f - Mathf.Clamp01(lineSink) * dryProgress) : 0f;
            }

            float target = Mathf.Clamp01(rain);
            float duration = rainWetness < target ? rainSoakTime : dryTime;
            rainWetness = Mathf.MoveTowards(rainWetness, target, duration > 0f ? delta / duration : 1f);
        }

        private void ResolveSurface()
        {
            float surface;
            HasWaterSurface = hasOverride;
            if (hasOverride) surface = overrideY;
            else HasWaterSurface = FairyToonWaterVolume.TryGetSurface(transform.position, out surface);
            WaterSurfaceY = HasWaterSurface ? surface : -100000f;
        }

        /// <summary>Publishes immediately without advancing time, for capture scripts and editor tools.</summary>
        public void Push()
        {
            ResolveSurface();
            Shader.SetGlobalVector(Wet0Id, new Vector4(transform.position.y + soakHeight, wetness, WaterSurfaceY, timeSinceLeft));
            int bones = PushBones();
            Shader.SetGlobalVector(Wet1Id, new Vector4(rainWetness, Mathf.Max(0.001f, edgeWidth), bones, 0f));
        }

        // Resolves the humanoid limb bones (again after an outfit merge replaces them) and publishes their segments.
        private int PushBones()
        {
            Animator animator = trailAnimator;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != resolvedAnimator || NeedsRebind())
            {
                resolvedAnimator = animator;
                bool human = animator != null && animator.isHuman && animator.avatar != null;
                for (int i = 0; i < MaxTrailBones; i++)
                {
                    bool used = human && TrailBones[i].radius > 0f;
                    boneFrom[i] = used ? animator.GetBoneTransform(TrailBones[i].from) : null;
                    boneTo[i] = used ? animator.GetBoneTransform(TrailBones[i].to) : null;
                }
            }
            int count = 0;
            // Limb chains (thigh -> shin, upper arm -> forearm) are one continuous strip for the water: the lower bone's
            // distance along the limb continues from the upper bone's length, and its zero angle is the upper bone's side
            // vector carried across the joint bend (parallel transport), so trails run on over the knee / elbow.
            bool previousValid = false;
            Vector3 previousAxis = Vector3.zero, previousSide = Vector3.zero;
            float previousLength = 0f, previousRadius = 0f, previousChain = 0f;
            for (int i = 0; i < MaxTrailBones; i++)
            {
                Transform from = boneFrom[i], to = boneTo[i];
                bool lowerOfChain = i < 8 && (i & 1) == 1; // TrailBones pairs: 0-1, 2-3, 4-5, 6-7
                if (from == null || to == null) { previousValid = false; continue; }
                Vector3 a = from.position, b = to.position;
                Vector3 axis = b - a;
                if (axis.sqrMagnitude < 1e-6f) { previousValid = false; continue; }
                float length = axis.magnitude;
                axis /= length;
                Vector3 side;
                float offset = 0f, radius = TrailBones[i].radius, chain = i;
                if (lowerOfChain && previousValid)
                {
                    side = Quaternion.FromToRotation(previousAxis, axis) * previousSide;
                    offset = previousLength;
                    radius = previousRadius; // same column layout around the limb on both sides of the joint
                    chain = previousChain;   // same trail seeds
                }
                else
                {
                    // A side direction fixed in the bone's own frame: the bone axis most perpendicular to the segment.
                    side = from.right;
                    if (Mathf.Abs(Vector3.Dot(side, axis)) > 0.7f) side = from.forward;
                }
                side = Vector3.ProjectOnPlane(side, axis).normalized;
                boneStart[count] = new Vector4(a.x, a.y, a.z, radius);
                boneEnd[count] = new Vector4(b.x, b.y, b.z, offset);
                boneSide[count] = new Vector4(side.x, side.y, side.z, chain);
                count++;
                previousValid = !lowerOfChain;
                previousAxis = axis;
                previousSide = side;
                previousLength = length;
                previousRadius = radius;
                previousChain = chain;
            }
            for (int i = count; i < MaxTrailBones; i++) boneStart[i] = boneEnd[i] = boneSide[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(BoneStartId, boneStart);
            Shader.SetGlobalVectorArray(BoneEndId, boneEnd);
            Shader.SetGlobalVectorArray(BoneSideId, boneSide);
            return count;
        }

        private bool NeedsRebind()
        {
            for (int i = 0; i < MaxTrailBones; i++)
                if (TrailBones[i].radius > 0f && (boneFrom[i] == null || boneTo[i] == null)) return true;
            return false;
        }

        private void LateUpdate() => Push();

        private void OnDisable()
        {
            Shader.SetGlobalVector(Wet0Id, Vector4.zero);
            Shader.SetGlobalVector(Wet1Id, Vector4.zero);
        }
    }
}
