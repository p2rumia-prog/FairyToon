using System;
using System.Collections.Generic;
using UnityEngine;

namespace NW.FairyToon.Editor
{
    /// <summary>Optional overrides applied after lilToon variant mapping and heuristics.</summary>
    [CreateAssetMenu(menuName = "FairyToon/Convert Profile")]
    public sealed class FairyToonConvertProfile : ScriptableObject
    {
        public enum Kind { Float, Color, Vector, Texture }

        [Serializable]
        public sealed class Override
        {
            public string property;
            public Kind kind;
            public float floatValue;
            public Color colorValue = Color.white;
            public Vector4 vectorValue;
            public Texture textureValue;
        }

        [Serializable]
        public sealed class Entry
        {
            public Material source;
            public List<Override> overrides = new List<Override>();
        }

        public List<Entry> entries = new List<Entry>();
    }

    /// <summary>Shared options for a single conversion or a batch.</summary>
    [Serializable]
    public sealed class FairyToonConvertOptions
    {
        public string OutputFolder;
        public bool InPlace;
        public bool TransparentHairToCutout = true;
        public FairyToonConvertProfile Profile;
        public bool UseProjectProfiles = true;
    }
}
