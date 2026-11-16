namespace NW.FairyToon
{
    /// <summary>Names shared by the FairyToon shader, its inspector and its tools.</summary>
    public static class FairyToon
    {
        /// <summary>The shader's name, for <c>Shader.Find</c>.</summary>
        public const string ShaderName = "FairyToon";

        public const int SeeThroughBit = 4;
        public const int FaceBit = 8;
        public const string SeeThroughMaskLightMode = "UniversalForwardOnly";
        public const string FakeShadowLightMode = "FairyToonFakeShadow";
        public const string CharacterShadowLightMode = "FairyToonCharacterShadow";
        public const string CharacterMaskLightMode = "FairyToonCharacterMask";
        public const string SeeThroughEarlyZLightMode = "FairyToonSeeThroughEarlyZ";
        public const string SeeThroughHairLightMode = "FairyToonSeeThroughHair";
    }
}
