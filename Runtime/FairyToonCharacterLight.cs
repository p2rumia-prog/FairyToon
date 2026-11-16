using UnityEngine;

namespace NW.FairyToon
{
    public enum FairyToonCharacterLightRole { Fill, Tint, Highlight, Rim, Fog }

    /// <summary>A small light for one character's Endfield-mode materials, by role (the per-character small lights of the
    /// community Endfield recreation: fill, shade tint, highlight, rim, fog).</summary>
    /// <remarks>Put it on any object near the character, usually a child of the avatar so it moves with it. It registers
    /// with the FairyToonCharacter on this object or a parent (or the one assigned), which takes the first four. It is not
    /// a Unity Light: it needs no light slot or light layer, casts no shadow and lights nothing else.</remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FairyToonCharacterLight : MonoBehaviour
    {
        [Tooltip("The character it lights. Empty: the FairyToonCharacter on this object or a parent.")]
        [SerializeField] private FairyToonCharacter character;
        [Tooltip("Fill: soft wrapped light, no toon edge. Tint: colours the shade side. Highlight: a toon specular dot. " +
            "Rim: lights the silhouette on this side. Fog: pulls the colour towards this colour.")]
        [SerializeField] private FairyToonCharacterLightRole role = FairyToonCharacterLightRole.Fill;
        [SerializeField] private Color color = new Color(1f, 0.82f, 0.62f);
        [Tooltip("Strength. For Tint and Fog: the blend amount (0-1) at the light's centre.")]
        [Range(0f, 4f)] [SerializeField] private float intensity = 0.5f;
        [Tooltip("Distance (m) at which it has faded out.")]
        [Range(0.05f, 10f)] [SerializeField] private float range = 1.5f;

        private FairyToonCharacter registered;

        public FairyToonCharacterLightRole Role
        {
            get => role;
            set { role = value; Changed(); }
        }

        public Color Color
        {
            get => color;
            set { color = value; Changed(); }
        }

        public float Intensity
        {
            get => intensity;
            set { intensity = Mathf.Max(0f, value); Changed(); }
        }

        public float Range
        {
            get => range;
            set { range = Mathf.Max(0.05f, value); Changed(); }
        }

        public FairyToonCharacter Character
        {
            get => registered;
            set { character = value; Bind(); }
        }

        private void OnEnable() => Bind();
        private void OnDisable() => Unbind();
        private void OnTransformParentChanged() => Bind();

        private void OnValidate()
        {
            if (isActiveAndEnabled) Bind();
        }

        private void Bind()
        {
            var target = character != null ? character : GetComponentInParent<FairyToonCharacter>(true);
            if (target != registered) Unbind();
            registered = target;
            if (registered != null) registered.Register(this);
        }

        private void Unbind()
        {
            if (registered != null) registered.Unregister(this);
            registered = null;
        }

        private void Changed()
        {
            if (registered != null) registered.SmallLightChanged();
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, 0.03f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(color.r, color.g, color.b, 0.35f);
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}
