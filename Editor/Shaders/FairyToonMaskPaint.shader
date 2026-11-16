// Mask Painter: paints into a mask render texture in UV space. Each mesh triangle is drawn at its UV position and
// works out its world position, so the brush is a world-space sphere that crosses UV seams. Editor only.
Shader "Hidden/FairyToon/MaskPaint"
{
    Properties
    {
        _Src ("Source", 2D) = "black" {}
        _Coverage ("Coverage", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _Src;
    float4 _Src_TexelSize;
    sampler2D _Coverage;
    float4 _BrushPos;       // xyz world centre, w radius
    float4 _BrushParams;    // x hardness, y strength, z value, w 1 = fill (no distance falloff)
    float4 _BrushNormal;    // xyz surface normal at the centre, w 1 = skip faces turned away from it
    float4 _ChannelMask;    // 1 for each channel that is written
    float _DilateRadius;

    struct Attributes
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float2 uv     : TEXCOORD0;
    };

    struct Varyings
    {
        float4 pos  : SV_POSITION;
        float2 uv   : TEXCOORD0;
        float3 wpos : TEXCOORD1;
        float3 wnrm : TEXCOORD2;
    };

    Varyings UnwrapVertex(Attributes i)
    {
        Varyings o;
        o.uv = i.uv;
        o.wpos = mul(unity_ObjectToWorld, i.vertex).xyz;
        o.wnrm = UnityObjectToWorldNormal(i.normal);
        float2 p = i.uv * 2.0 - 1.0;
        #if UNITY_UV_STARTS_AT_TOP
            p.y = -p.y;
        #endif
        o.pos = float4(p, 0.5, 1.0);
        return o;
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: brush stamp / island fill. The target is a copy of _Src; only the selected channels change.
        Pass
        {
            CGPROGRAM
            #pragma vertex UnwrapVertex
            #pragma fragment frag

            float4 frag(Varyings i) : SV_Target
            {
                float4 src = tex2Dlod(_Src, float4(i.uv, 0, 0));
                float k = 1.0;
                if (_BrushParams.w < 0.5)
                {
                    float radius = _BrushPos.w;
                    float d = distance(i.wpos, _BrushPos.xyz);
                    float inner = radius * saturate(_BrushParams.x);
                    k = inner >= radius ? step(d, radius) : 1.0 - smoothstep(inner, radius, d);
                    if (_BrushNormal.w > 0.5 && dot(normalize(i.wnrm), _BrushNormal.xyz) < 0.0) k = 0.0;
                }
                k *= _BrushParams.y;
                float4 painted = lerp(src, _BrushParams.zzzz, k);
                return lerp(src, painted, _ChannelMask);
            }
            ENDCG
        }

        // 1: UV coverage of every submesh, for the save-time dilation.
        Pass
        {
            CGPROGRAM
            #pragma vertex UnwrapVertex
            #pragma fragment frag

            float4 frag(Varyings i) : SV_Target { return 1.0; }
            ENDCG
        }

        // 2: dilation. Texels no triangle covers take the nearest covered texel, so bilinear sampling and mips do not
        // pull the outside colour into island edges. Covered texels are left exactly as painted.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2Dlod(_Src, float4(i.uv, 0, 0));
                if (tex2Dlod(_Coverage, float4(i.uv, 0, 0)).r > 0.5) return src;
                int r = (int)_DilateRadius;
                float best = 1e9;
                float4 result = src;
                for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    float2 uv = i.uv + float2(x, y) * _Src_TexelSize.xy;
                    float d = x * x + y * y;
                    if (d < best && tex2Dlod(_Coverage, float4(uv, 0, 0)).r > 0.5)
                    {
                        best = d;
                        result = tex2Dlod(_Src, float4(uv, 0, 0));
                    }
                }
                return result;
            }
            ENDCG
        }
    }
}
