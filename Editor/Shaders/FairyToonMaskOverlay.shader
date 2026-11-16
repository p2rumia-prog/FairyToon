// Mask Painter: Scene view overlay that tints the painted channel on the mesh. Editor only.
Shader "Hidden/FairyToon/MaskOverlay"
{
    Properties
    {
        _Mask ("Mask", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 0.2, 0.6, 0.6)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            ZTest [_ZTest]
            Cull Back
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _Mask;
            float4 _ChannelMask;
            float4 _Tint;

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float value = dot(tex2D(_Mask, i.uv), _ChannelMask);
                // (a faint wash shows the painted mesh where the mask is empty)
                return float4(_Tint.rgb, lerp(0.06, _Tint.a, saturate(value)));
            }
            ENDCG
        }
    }
}
