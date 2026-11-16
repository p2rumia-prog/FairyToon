// FairyToon vertex / fragment programs for every pass. Each pass picks its pair with #pragma vertex / fragment.
#ifndef FAIRYTOON_PASSES_INCLUDED
#define FAIRYTOON_PASSES_INCLUDED

#include "FairyToonInput.hlsl"
#include "FairyToonNormals.hlsl"

// ---------------------------------------------------------------------------------------------------------------------
// Outline hull (shared by the Outline and MotionVectors passes)

struct FairyToonOutlineVertex
{
    float3 positionOS;
    float3 normalOS;
    float4 tangentOS;
    half4  color;
    float4 smoothNormalTS;  // UV7 (FairyToon): baked smoothed normal in tangent space, w coherence (1 smooth)
    float2 uvMain;
};

bool FairyToonOutlineHasUV7(FairyToonOutlineVertex v)
{
    return abs(_OutlineVertexR2Width - 3.0) < 0.5 && dot(v.smoothNormalTS.xyz, v.smoothNormalTS.xyz) > 0.01;
}

// Hull width in metres. Object mode: lilToon's width (cm, scaled with the object). Screen mode (FairyToon): the world
// width that covers _OutlinePixelWidth pixels (1080p reference) at this vertex's depth, so the line is even on screen,
// floored at whole pixels (thinner lines crawl) and faded out with distance. At the silhouette that matches a
// clip-space extrusion, and staying an offset along the normal keeps the motion vector hull in step.
float FairyToonOutlineWidth(FairyToonOutlineVertex v)
{
    float mask = SAMPLE_TEXTURE2D_LOD(_OutlineWidthMask, sampler_ft_linear_repeat, v.uvMain, 0).r;
    if (abs(_OutlineVertexR2Width - 1.0) < 0.5) mask *= v.color.r;
    if (abs(_OutlineVertexR2Width - 2.0) < 0.5) mask *= v.color.a;
    // (low coherence = a hard corner, where the hull splits open: the line tapers there)
    if (FairyToonOutlineHasUV7(v)) mask *= lerp(1.0, smoothstep(0.25, 0.75, v.smoothNormalTS.w), _OutlineCornerTaper);
    float3 positionWS = TransformObjectToWorld(v.positionOS);
    float distance = length(GetCameraPositionWS() - positionWS);
    if (_OutlineMode < 0.5)
        return _OutlineWidth * 0.01 * mask * lerp(1.0, saturate(distance), _OutlineFixWidth);

    float pixels = _OutlinePixelWidth * _ScreenParams.y / 1080.0 * mask;
    pixels = mask > 0.01 ? max(pixels, _OutlineMinPixels) : 0.0;
    if (_OutlineFadeEnd > 0.0) pixels *= 1.0 - smoothstep(_OutlineFadeStart, max(_OutlineFadeEnd, _OutlineFadeStart + 0.01), distance);
    // (clip w is the view depth, 1 for orthographic cameras)
    float clipW = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0)).w;
    return pixels * 2.0 * clipW / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
}

// Object-space offset of the hull vertex. _OutlineVertexR2Width 2 = vertex colour RGB as a tangent-space direction
// (lilToon), 3 = smoothed normal from UV7 (FairyToon).
float3 FairyToonOutlineOffsetOS(FairyToonOutlineVertex v)
{
    float3 n = v.normalOS;
    float3 bitangent = cross(v.normalOS, v.tangentOS.xyz) * (v.tangentOS.w * GetOddNegativeScale());
    float3x3 tbn = float3x3(v.tangentOS.xyz, bitangent, v.normalOS);
    if (abs(_OutlineVertexR2Width - 2.0) < 0.5) n = mul(v.color.rgb * 2.0 - 1.0, tbn);
    if (FairyToonOutlineHasUV7(v)) n = mul(v.smoothNormalTS.xyz, tbn);
    float width = FairyToonOutlineWidth(v);
    // (screen mode widths are world metres: undo the object's scale along n)
    if (_OutlineMode > 0.5) width /= max(length(TransformObjectToWorldDir(n, false)), 1e-5);
    return n * width;
}

// ---------------------------------------------------------------------------------------------------------------------
// Forward

struct FairyToonAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 uv0        : TEXCOORD0;
    FT_LAYER_ATTRIBUTES
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct FairyToonVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv0        : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS   : TEXCOORD2;
    float4 tangentWS  : TEXCOORD3;
    half   fogFactor  : TEXCOORD4;
    FT_LAYER_VARYINGS
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// Camera depth needs the same optics as colour; the outline selects its lit colour by _ShadingModel.
#if defined(_FT_EF_IRIS)

// Stable roots avoid cancellation near a face-on view. -1 means no forward hit.
// The bowl rejects t=0; the dome accepts it so its rim normal can fade to flat.
float FairyToonEFIrisRayTime(float A, float B, float C, bool allowZero)
{
    if (abs(A) < 1e-7)
    {
        if (abs(B) < 1e-7) return -1.0;
        float t = -C / B;
        return (allowZero ? t >= 0.0 : t > 0.0) ? t : -1.0;
    }
    float discriminant = B * B - 4.0 * A * C;
    if (discriminant < 0.0) return -1.0;
    float root = sqrt(discriminant);
    float q = -0.5 * (B + (B >= 0.0 ? root : -root));
    float t0 = q / A;
    float t1 = abs(q) > 1e-20 ? C / q : -B / (2.0 * A);
    bool valid0 = allowZero ? t0 >= 0.0 : t0 > 0.0;
    bool valid1 = allowZero ? t1 >= 0.0 : t1 > 0.0;
    if (valid0 && valid1) return min(t0, t1);
    return valid0 ? t0 : (valid1 ? t1 : -1.0);
}

float3 FairyToonEFIrisHit(float2 p0, float3 D, float d, float k, bool allowZero, out bool hit)
{
    hit = false;
    if (d == 0.0) return float3(p0, 0.0);
    float invK2 = 1.0 / (k * k);
    float A = D.x * D.x + D.y * D.y * invK2;
    float B = 2.0 * (p0.x * D.x + p0.y * D.y * invK2) - D.z / d;
    float C = p0.x * p0.x + p0.y * p0.y * invK2 - 1.0;
    float t = FairyToonEFIrisRayTime(A, B, C, allowZero);
    hit = t >= 0.0;
    return hit ? float3(p0, 0.0) + t * D : float3(p0, 0.0);
}

// The exponential joins with slope 1 at the knee; retain the inner lookup exactly, even near zero.
float FairyToonEFSoftClamp(float radius, float edge, float softness)
{
    float knee = (1.0 - softness) * edge;
    float width = edge - knee;
    return (width <= 1e-5 || radius <= knee) ? min(radius, edge)
        : knee + width * (1.0 - exp(-(radius - knee) / width));
}

void FairyToonEFIrisGazeFrame(float3 normalWS, float4 tangentWS, float3 headForward,
    out float3 X, out float3 Y, out float3 Z)
{
    float3 T = normalize(tangentWS.xyz);
    float3 Ng = normalize(normalWS);
    float3 Bt = tangentWS.w * cross(Ng, T); // same handedness as the normal-map TBN
    // Curved, outward-tilted eye shells must not shift the art when the viewer meets the head's gaze.
    // Gram-Schmidt keeps the signed UV axes while giving the bowl and dome one gaze-aligned depth axis.
    Z = lerp(Ng, headForward, _EFIrisGaze);
    float zLengthSq = dot(Z, Z);
    Z = zLengthSq > 1e-6 ? normalize(Z) : Ng;
    X = T - Z * dot(T, Z);
    Y = Bt - Z * dot(Bt, Z);
    float xLengthSq = dot(X, X);
    X = xLengthSq > 1e-6 ? normalize(X) : T;
    Y -= X * dot(Bt, X);
    // Parallel UV/gaze axes (or cancelling depth axes) cannot define a basis; retain the old surface frame.
    if (zLengthSq <= 1e-6 || xLengthSq <= 1e-6 || dot(Y, Y) <= 1e-6)
    {
        X = T;
        Y = Bt;
        Z = Ng;
    }
    else Y = normalize(Y);
}

void FairyToonEFIrisOptics(float2 p, float3 I, float k, out float2 pArt, out float3 Nc)
{
    bool bowlHit, domeHit;
    float3 hitB = float3(p, 0.0);
    if (_EFIrisDepth > 0.0) hitB = FairyToonEFIrisHit(p, -I, _EFIrisDepth, k, false, bowlHit);
    Nc = float3(0, 0, 1);
    if (_EFCorneaBulge > 0.0)
    {
        float d = -_EFCorneaBulge; // positive slider, negative node depth gives the outward dome
        float3 hitD = FairyToonEFIrisHit(p, I, d, k, true, domeHit);
        if (domeHit)
        {
            float3 n = normalize(float3(hitD.x, hitD.y / (k * k), -1.0 / (2.0 * d)));
            n = n.z < 0.0 ? -n : n;
            float fade = _EFCorneaNormalRange > 0.0
                ? saturate(hitD.z / (_EFCorneaBulge * _EFCorneaNormalRange)) : 1.0;
            Nc = normalize(lerp(float3(0, 0, 1), n, fade));
        }
    }
    // Mayo's art ends at the island edge; scale its lookup without shrinking the bowl or dome.
    pArt = _EFIrisScale * (hitB.xy + _EFIrisRefraction * reflect(-I, Nc).xy);
    // Bypass even the subtract/add at defaults: cancellation would change legacy iris and pupil lookups.
    if (_EFIrisVertical != 1.0 || _EFIrisMaxShift < 2.0 || _EFIrisEdgeFade > 0.0)
    {
        float2 d = pArt - p;
        d.y *= _EFIrisVertical;
        float cap = _EFIrisMaxShift;
        if (cap < 2.0)
        {
            float m = length(float2(d.x, d.y / k));
            d *= m <= 0.5 * cap ? 1.0 : FairyToonEFSoftClamp(m, cap, 0.5) / max(m, 1e-6);
        }
        if (_EFIrisEdgeFade > 0.0)
        {
            float end = min(_EFIrisRim, 1.0);
            float rho = length(float2(p.x, p.y / k));
            // Widen with the shift so ellipse |d| <= (end - rho) / 1.25 inside the rim:
            // rho(p + d) < end, and a locally constant d has radial projection derivative
            // >= 1 - 1/1.25 > 0, preventing folds from the fade even at steep views.
            // This reduces depth near the rim like the tutorial's depth map; face-on magnification
            // also fades to identity, letting the art meet the fixed rim without a seam.
            float band = max(_EFIrisEdgeFade * end, 1.25 * length(float2(d.x, d.y / k)));
            d *= saturate((end - rho) / max(band, 1e-5));
        }
        pArt = p + d;
    }
    float radius = length(float2(pArt.x, pArt.y / k));
    float knee = (1.0 - _EFIrisEdgeSoftness) * _EFIrisEdge;
    float softRadius = FairyToonEFSoftClamp(radius, _EFIrisEdge, _EFIrisEdgeSoftness);
    pArt *= radius <= knee ? 1.0 : softRadius / max(radius, 1e-6);
}

// Solve the centre once per vertex: blend shapes have already reshaped the fan, so its own UV/art survives.
float2 FairyToonEFPupilShift(float2 uv0, float3 I, float k)
{
    if (_EFPupilLayer <= 0.5 || _EFPupilDepth <= 0.0) return 0.0;
    bool left = uv0.x < 0.5;
    float2 a = left ? _EFPupilOffset.xy : float2(-_EFPupilOffset.x, _EFPupilOffset.y);
    float2 p = a;
    [unroll] for (int iteration = 0; iteration < 2; iteration++)
    {
        float2 pArt;
        float3 Nc;
        FairyToonEFIrisOptics(p, I, k, pArt, Nc);
        p = a - _EFPupilDepth * (pArt - p);
    }
    // Reserve the pupil's ellipse extent inside the fixed rim; do not disturb an already-outside rest pose.
    float extent = _EFPupilScale * max(1.0, _EFPupilYRatio / k);
    float limit = min(_EFIrisRim, 1.0) - extent;
    float restRadius = length(float2(a.x, a.y / k));
    float radius = length(float2(p.x, p.y / k));
    if (limit > restRadius)
    {
        float softRadius = FairyToonEFSoftClamp(radius, limit, 0.3);
        p *= radius <= 0.7 * limit ? 1.0 : softRadius / max(radius, 1e-6);
    }
    return p - a;
}

float3 FairyToonEFPupilPosition(float3 positionWS, float3 normalWS, float4 tangentWS, float2 uv0)
{
    float4 f = SAMPLE_TEXTURE2D_LOD(_EFIrisFrame, sampler_ft_linear_clamp, uv0, 0);
    if (_EFPupilLayer > 0.5 && _EFPupilDepth > 0.0 && f.a <= -0.5)
    {
        float k = max(abs(f.a), 1e-3);
        float3 headForward, headRight, headUp, headPosition;
        FairyToonEFHeadFrame(headForward, headRight, headUp, headPosition);
        float3 X, Y, Z;
        FairyToonEFIrisGazeFrame(normalWS, tangentWS, headForward, X, Y, Z);
        float3 V = GetWorldSpaceNormalizeViewDir(positionWS);
        float3 I = normalize(float3(dot(V, X), dot(V, Y), dot(V, Z)));
        float2 s = FairyToonEFPupilShift(uv0, I, k);
        float2 p0 = f.rg;
        float2 rest = float2(p0.x, p0.y / k);
        float2 moved = float2(p0.x + s.x, (p0.y + s.y) / k);
        float rise = _EFIrisDish * (dot(moved, moved) - dot(rest, rest));
        positionWS += (s.x * X + s.y * Y) * _EFIrisWorldRadius;
        // Re-evaluate the ray after lateral travel: only that ray preserves perspective screen position.
        V = GetWorldSpaceNormalizeViewDir(positionWS);
        positionWS += V * (rise * _EFIrisWorldRadius / max(dot(V, Z), 0.25));
    }
    return positionWS;
}
#endif

// All camera passes use the same skinned POSITION/NORMAL/TANGENT/UV0, before any pass-specific extrusion.
float3 FairyToonEFPupilVertexPositionWS(float3 positionOS, float3 normalOS, float4 tangentOS, float2 uv0)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    #if defined(_FT_EF_IRIS)
        VertexNormalInputs n = GetVertexNormalInputs(normalOS, tangentOS);
        positionWS = FairyToonEFPupilPosition(positionWS, n.normalWS,
            float4(n.tangentWS, tangentOS.w * GetOddNegativeScale()), uv0);
    #endif
    return positionWS;
}

#if defined(_FT_EF_IRIS)
float2 FairyToonEFIrisUV(float2 uv0, float3 V, float3 normalWS, float4 tangentWS, half facing,
    out half4 pupilFold, out half4 corneaMatcap)
{
    pupilFold = 0.0;
    corneaMatcap = 0.0;
    // Island codes must never blend through mips, independently of the albedo's filtering and wrap settings.
    float4 f = SAMPLE_TEXTURE2D_LOD(_EFIrisFrame, sampler_ft_linear_clamp, uv0, 0);
    UNITY_BRANCH if (abs(f.a) >= 0.5)
    {
        float k = abs(f.a) > 5.0 ? abs(f.a) - 10.0 : abs(f.a);
        k = max(k, 1e-3);
        float3 headForward, headRight, headUp, headPosition;
        FairyToonEFHeadFrame(headForward, headRight, headUp, headPosition);
        float3 X, Y, Z;
        FairyToonEFIrisGazeFrame(normalWS, tangentWS, headForward, X, Y, Z);
        // Keep the UV axes on back faces so parallax follows the art; only depth faces the viewer.
        float faceSign = facing < 0.0 ? -1.0 : 1.0;
        float3 I = normalize(float3(dot(V, X), dot(V, Y), faceSign * dot(V, Z)));
        float2 p0 = f.rg;
        float2 pArt;
        float3 Nc;
        FairyToonEFIrisOptics(p0, I, k, pArt, Nc);
        if (_EFPupilLayer > 0.5 && f.a < 0.0)
        {
            // The highlight belongs to the moved surface; pupil art remains on its original UV island.
            float2 s = FairyToonEFPupilShift(uv0, I, k);
            float2 unusedArt;
            FairyToonEFIrisOptics(p0 + s, I, k, unusedArt, Nc);
        }
        #if !defined(FAIRYTOON_PASS_OUTLINE)
            float3 Rr = refract(-I, Nc, 1.0 / max(_EFCorneaIOR, 1e-6));
            // Reconstruct world space before sampling in the head frame so mirrored UVs share one highlight.
            // Character right is viewer left from the front; the matcap uses viewer right and head up.
            float3 RrWS = Rr.x * X + Rr.y * Y + Rr.z * Z;
            float3 RrHead = float3(-dot(RrWS, headRight), dot(RrWS, headUp), dot(RrWS, headForward));
            float2 muv = RrHead.xy * _EFIrisSparkleScale * 0.5 + 0.5;
            corneaMatcap = SAMPLE_TEXTURE2D(_EFIrisSparkleTex, sampler_ft_linear_clamp, muv);
        #endif
        // The dark ring and rim lights belong to the eye surface like a real limbus; only the inner pattern sinks.
        // Keep the cornea over the limbus and let its hard edge occlude the pattern, without folding a pupil onto it.
        float rho0 = length(float2(p0.x, p0.y / k));
        if (f.a > 0.0 && rho0 >= _EFIrisRim) return uv0;

        if (_EFPupilLayer > 0.5 && f.a < 0.0) return uv0;
        bool left = uv0.x < 0.5;
        float2 irisCentre = left ? _EFIrisCenterL.xy : _EFIrisCenterR.xy;
        float2 irisUV = f.a > 0.0 ? uv0 + (pArt - p0) * f.b : irisCentre + pArt * _EFIrisRadius;

        // The opaque pupil mesh draws the same folded picture as the iris beneath it; never discard it.
        // Expression islands (k+10) already have painted pupils and must not fold the main pupil over them.
        if (_EFPupilLayer <= 0.5 && f.a < 5.0 && _EFPupil > 0.0)
        {
            float2 off = float2(left ? _EFPupilOffset.x : -_EFPupilOffset.x, _EFPupilOffset.y);
            float2 q = (pArt - off) / max(_EFPupilScale, 1e-6);
            float2 pupilCentre = left ? _EFPupilCenterL.xy : _EFPupilCenterR.xy;
            float2 pupilUV = pupilCentre + q * _EFPupilRadius;
            pupilUV = pupilUV * _MainTex_ST.xy + _MainTex_ST.zw;
            float pupilDistance = length(float2(q.x, q.y / max(_EFPupilYRatio, 1e-6)));
            float w = max(1.5 * fwidth(pupilDistance), 1e-5);
            float inside = 1.0 - smoothstep(1.0 - w, 1.0 + w, pupilDistance);
            pupilFold = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, pupilUV) * _Color;
            pupilFold.a *= inside * _EFPupil;
        }
        return irisUV;
    }
    return uv0;
}
#endif

#if defined(FAIRYTOON_PASS_FORWARD)
#include "FairyToonCore.hlsl"

FairyToonVaryings FairyToonForwardVertex(FairyToonAttributes input)
{
    FairyToonVaryings output = (FairyToonVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    output.positionWS = FairyToonEFPupilVertexPositionWS(input.positionOS.xyz, input.normalOS, input.tangentOS, input.uv0);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.normalWS = normalInputs.normalWS;
    output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.uv0 = input.uv0;
    FT_TRANSFER_LAYERS(input, output)
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
    return output;
}

// One surface setup for Forward, Refraction, Gem and Fur. Special optical/strand rules are compile-time branches.
FairyToonSurface FairyToonForwardSurface(FairyToonVaryings input, half facing)
{
    FairyToonSurface s = (FairyToonSurface)0;
    s.facing = facing;
    s.positionWS = input.positionWS;
    #if defined(_FT_ENDFIELD)
        s.V = GetWorldSpaceNormalizeViewDir(input.positionWS);
    #else
        // lilToon keeps a camera-position view vector even for orthographic cameras.
        s.V = normalize(GetCameraPositionWS() - input.positionWS);
    #endif
    s.screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
    s.origN = normalize(input.normalWS);
    s.N = s.origN;
    #if defined(_FT_EF_IRIS)
        half4 pupilFold, corneaMatcap;
        // Match the pupil vertex ray in both models, including orthographic cameras;
        // Toon keeps its camera-position s.V for lighting, while iris optics follow the moved surface.
        input.uv0 = FairyToonEFIrisUV(input.uv0, GetWorldSpaceNormalizeViewDir(input.positionWS),
            input.normalWS, input.tangentWS, facing, pupilFold, corneaMatcap);
    #endif
    s.layers = FT_LAYER_CONTEXT(input, facing);
    s.uvMain = input.uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
    #if defined(_FT_MATCAP)
        s.matcapTBN = float3x3(input.tangentWS.xyz,
            input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz), input.normalWS);
    #endif
    #if defined(_NORMALMAP) || defined(_FT_NORMAL2ND)
        float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
        half3 normalTS = FairyToonNormalTS(s.layers, s.uvMain);
        s.N = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));
    #endif

    #if defined(FAIRYTOON_GEM)
        // Gem's interior reversal/tilt precedes anisotropy and layer coverage, once in either shading model.
        if (s.facing < 0.0) s.N = normalize(-s.N - s.V * 0.2);
    #elif defined(_FT_ENDFIELD)
        // Endfield always lights the visible side, regardless of lilToon's _FlipNormal slider.
        s.N = s.facing < 0.0 ? -s.N : s.N;
    #elif !defined(FAIRYTOON_FUR_INCLUDED)
        s.N = FairyToonFacingNormal(s.N, s.facing);
    #endif

    #if defined(_FT_ANISOTROPY)
        FairyToonSetupAnisotropy(s, input.tangentWS.xyz, input.tangentWS.w);
    #endif

    // Decal edges use mapped N.V; Rim UV uses its absolute value. MatCap retains geometric UVs.
    #if defined(FAIRYTOON_GEM)
        s.layers.nv = abs(dot(s.N, s.V));
    #else
        s.layers.nv = saturate(dot(s.N, s.V));
    #endif
    s.layers.nvabs = abs(dot(s.N, s.V));
    half4 base = FairyToonLayeredBase(s.layers, s.layer2, s.layer3, s.dissolveEdge, s.layerDissolveEdges);
    #if defined(FAIRYTOON_REFRACTION) || defined(FAIRYTOON_GEM) || defined(FAIRYTOON_FUR_INCLUDED)
        // Optics compose material alpha internally; strands apply their own noise coverage afterwards.
        s.alpha = saturate(base.a);
    #else
        s.alpha = FairyToonClipAlpha(base.a);
    #endif
    s.albedo = base.rgb;
    #if defined(_FT_EF_IRIS)
        s.albedo = lerp(s.albedo, pupilFold.rgb, pupilFold.a);
        // Screen into the layered base before either lighting model so the iris shares its surface shading.
        // Separate the window reflection from the blue glow so each can be tuned without changing the other.
        half3 light = corneaMatcap.rgb * _EFIrisSparkle;
        half a = saturate(corneaMatcap.a * _EFIrisGlowStrength);
        half3 c = s.albedo + light;
        s.albedo = 1.0 - (1.0 - a * _EFIrisGlowColor.rgb) * (1.0 - c);
    #endif
    // Sheer fabric: looking straight through one thin layer shows what is under it; towards the edges the view crosses
    // more fabric and the stocking colour takes over (thin denier). The fabric takes on the skin tone (_SheerColor) and,
    // on a Transparent material, also lets the skin below show (lower alpha, at most half way, so the fabric stays read).
    // Wet streaks then make it darker and more opaque (FairyToonApplyWetDetails).
    if (_SheerAmount > 0.0)
    {
        half through = pow(saturate(s.layers.nv), _SheerPower) * _SheerAmount;
        s.albedo = lerp(s.albedo, _SheerColor.rgb, through);
        #if defined(_SURFACE_TYPE_TRANSPARENT)
            // _SheerAlpha: alpha where the fabric is most see-through (facing the view); 1 at the edges.
            s.alpha *= lerp(1.0, _SheerAlpha, pow(saturate(s.layers.nv), _SheerPower));
        #endif
    }
    #if defined(_FT_WET)
        FairyToonApplyWet(s, input.uv0);
        FairyToonApplyWetDetails(s, input.uv0);
    #endif
    return s;
}

half4 FairyToonForwardFragment(FairyToonVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    FairyToonSurface s = FairyToonForwardSurface(input, IS_FRONT_VFACE(isFront, 1.0, -1.0));
    half3 col = FairyToonShade(s, input.uv0);
    // (character shadow debug view: its visibility only)
    if (_FT_CharShadowParams2.x > 0.5) col = ftCharShadowVisibility;
    col = MixFog(col, input.fogFactor);
    #if defined(FAIRYTOON_SEETHROUGH_HAIR)
        clip(0.5 - abs(_SeeThroughRole - 2.0));
        #if defined(_SURFACE_TYPE_TRANSPARENT) || defined(_ALPHATEST_ON)
            half coverage = s.alpha;
        #else
            half coverage = 1.0;
        #endif
        return half4(col, coverage * FairyToonSeeThroughAlpha());
    #elif defined(_SURFACE_TYPE_TRANSPARENT) || defined(_ALPHATEST_ON)
        return half4(col, s.alpha);
    #else
        return half4(col, 1.0);
    #endif
}
#endif

// ---------------------------------------------------------------------------------------------------------------------
// Outline (Cull Front hull, drawn by URP as SRPDefaultUnlit; off when the material disables the pass)

struct FairyToonOutlineAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 uv0        : TEXCOORD0;
    half4  color      : COLOR;
    float4 uv7        : TEXCOORD7;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct FairyToonOutlineVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv0        : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS   : TEXCOORD2;
    half   fogFactor  : TEXCOORD3;
    float4 tangentWS  : TEXCOORD4; // geometric iris frame for the lit outline colour
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

FairyToonOutlineVertex FairyToonMakeOutlineVertex(float3 positionOS, float3 normalOS, float4 tangentOS, half4 color, float4 uv7, float2 uv0)
{
    FairyToonOutlineVertex v;
    v.positionOS = positionOS;
    v.normalOS = normalOS;
    v.tangentOS = tangentOS;
    v.color = color;
    v.smoothNormalTS = uv7;
    v.uvMain = uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
    return v;
}

#if defined(FAIRYTOON_PASS_OUTLINE)
#include "FairyToonCore.hlsl"

FairyToonOutlineVaryings FairyToonOutlineVertexProgram(FairyToonOutlineAttributes input)
{
    FairyToonOutlineVaryings output = (FairyToonOutlineVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 pupilShiftWS = FairyToonEFPupilVertexPositionWS(input.positionOS.xyz, input.normalOS, input.tangentOS, input.uv0)
        - TransformObjectToWorld(input.positionOS.xyz);
    FairyToonOutlineVertex v = FairyToonMakeOutlineVertex(input.positionOS.xyz, input.normalOS, input.tangentOS, input.color, input.uv7, input.uv0);
    // Width is evaluated after the shift; retaining the original extrusion arithmetic keeps fold mode unchanged.
    v.positionOS += TransformWorldToObjectDir(pupilShiftWS, false);
    float3 positionOS = input.positionOS.xyz + FairyToonOutlineOffsetOS(v);
    float3 positionWS = TransformObjectToWorld(positionOS);
    #if defined(_FT_EF_IRIS)
        positionWS += pupilShiftWS;
    #endif
    positionWS -= GetWorldSpaceNormalizeViewDir(positionWS) * _OutlineZBias; // (lilToon: Z bias pushes the hull away)
    output.positionWS = positionWS;
    output.positionCS = TransformWorldToHClip(positionWS);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
    output.uv0 = input.uv0;
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
    #if !defined(_FT_OUTLINE)
        output.positionCS = float4(0, 0, -1, 1); // (outline off: clipped away)
    #endif
    return output;
}

half4 FairyToonOutlineFragment(FairyToonOutlineVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uvMain = input.uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
    half4 base = FairyToonBaseColor(uvMain, input.uv0, FT_DISSOLVE_POSITION_OS(input));
    half alpha = FairyToonClipAlpha(base.a);
    half4 col = _OutlineColor * SAMPLE_TEXTURE2D(_OutlineTex, sampler_MainTex, input.uv0 * _OutlineTex_ST.xy + _OutlineTex_ST.zw);

    FairyToonSurface s = (FairyToonSurface)0;
    s.positionWS = input.positionWS;
    s.V = GetWorldSpaceNormalizeViewDir(input.positionWS);
    s.N = s.origN = normalize(input.normalWS);
    s.uvMain = uvMain;
    s.screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
    s.facing = 1;
    // Iris lookup is shared; only the outline lighting below depends on the shading model.
    s.albedo = base.rgb;
    #if defined(_FT_EF_IRIS)
        // Keep hull coverage unchanged; the lookup feeds lighting masks and Endfield's local outline colour.
        half4 pupilFold, unusedMatcap;
        float2 irisUV = FairyToonEFIrisUV(input.uv0, s.V, input.normalWS, input.tangentWS,
            IS_FRONT_VFACE(isFront, 1.0, -1.0), pupilFold, unusedMatcap);
        s.uvMain = irisUV * _MainTex_ST.xy + _MainTex_ST.zw;
        s.albedo = FairyToonBaseColor(s.uvMain, irisUV, FT_DISSOLVE_POSITION_OS(input)).rgb;
        s.albedo = lerp(s.albedo, pupilFold.rgb, pupilFold.a);
    #endif
    if (_ShadingModel > 0.5)
    {
        // (Endfield mode: a darker version of the local shadow colour instead of one outline colour)
        #if defined(_FT_WET)
            FairyToonApplyWet(s, input.uv0);
        #endif
        col.rgb = FairyToonEFOutlineColor(s, s.albedo);
    }
    else
    {
        #if defined(_FT_WET)
            s.albedo = col.rgb;
            FairyToonApplyWet(s, input.uv0);
            col.rgb = s.albedo;
        #endif
        FairyToonLight l = FairyToonGetLight(s);
        FairyToonAdditionalLights(s, l);
        col.rgb = lerp(col.rgb, col.rgb * min(l.lightColor + l.addLightColor, _LightMaxLimit), _OutlineEnableLighting);
    }
    col.rgb = MixFog(col.rgb, input.fogFactor);
    return half4(col.rgb, alpha * col.a);
}
#endif

// ---------------------------------------------------------------------------------------------------------------------
// ShadowCaster / DepthOnly / DepthNormals

struct FairyToonDepthVaryings
{
    float4 positionCS : SV_POSITION;
    #if defined(FT_DISSOLVE_DEPTHNORMALS_PACKED)
        float4 uv0      : TEXCOORD0; // xy mesh UV; zw object-position XY
        float4 normalWS : TEXCOORD1; // xyz geometric normal; w object-position Z
    #else
        float2 uv0      : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
    #endif
    float4 tangentWS  : TEXCOORD2;
    FT_LAYER_VARYINGS
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void FairyToonDepthClip(FairyToonLayerContext layers)
{
    #if defined(_ALPHATEST_ON) || defined(_SURFACE_TYPE_TRANSPARENT)
        clip(FairyToonLayeredBase(layers).a - max(_Cutoff, 0.001));
    #endif
}

#if defined(FAIRYTOON_PASS_SHADOWCASTER)
float3 _LightDirection;
float3 _LightPosition;
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

FairyToonDepthVaryings FairyToonShadowVertex(FairyToonAttributes input)
{
    FairyToonDepthVaryings output = (FairyToonDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif
    output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
    output.uv0 = input.uv0;
    FT_TRANSFER_LAYERS(input, output)
    return output;
}

half4 FairyToonShadowFragment(FairyToonDepthVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    FairyToonDepthClip(FT_LAYER_CONTEXT(input, IS_FRONT_VFACE(isFront, 1.0, -1.0)));
    return 0;
}
#endif

// Character self-shadow caster. FairyToonFeature sets the tile's view-projection and the bias per draw; the bias moves
// the surface away from the projector and inwards, more at grazing angles (URP's ApplyShadowBias).
#if defined(FAIRYTOON_PASS_CHARSHADOW)
float4x4 _FT_CharShadowCasterVP;
float4 _FT_CharShadowCasterLight;   // xyz towards the projector
float4 _FT_CharShadowCasterBias;    // x depth bias (m), y normal bias (m)

FairyToonDepthVaryings FairyToonCharShadowVertex(FairyToonAttributes input)
{
    FairyToonDepthVaryings output = (FairyToonDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    float3 L = _FT_CharShadowCasterLight.xyz;
    positionWS -= L * _FT_CharShadowCasterBias.x;
    positionWS -= normalWS * (_FT_CharShadowCasterBias.y * (1.0 - saturate(dot(L, normalWS))));
    output.positionCS = mul(_FT_CharShadowCasterVP, float4(positionWS, 1.0));
    output.uv0 = input.uv0;
    FT_TRANSFER_LAYERS(input, output)
    return output;
}

half4 FairyToonCharShadowFragment(FairyToonDepthVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    FairyToonDepthClip(FT_LAYER_CONTEXT(input, IS_FRONT_VFACE(isFront, 1.0, -1.0)));
    return 0;
}
#endif

#if defined(FAIRYTOON_PASS_DEPTH)
FairyToonDepthVaryings FairyToonDepthVertex(FairyToonAttributes input)
{
    FairyToonDepthVaryings output = (FairyToonDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    float3 positionWS = FairyToonEFPupilVertexPositionWS(input.positionOS.xyz, input.normalOS, input.tangentOS, input.uv0);
    output.positionCS = TransformWorldToHClip(positionWS);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    output.normalWS.xyz = normalInputs.normalWS;
    output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.uv0.xy = input.uv0;
    FT_TRANSFER_LAYERS(input, output)
    return output;
}

half FairyToonDepthFragment(FairyToonDepthVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    FairyToonDepthClip(FT_LAYER_CONTEXT(input, IS_FRONT_VFACE(isFront, 1.0, -1.0)));
    return input.positionCS.z;
}

void FairyToonDepthNormalsFragment(FairyToonDepthVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    FairyToonDepthClip(FT_LAYER_CONTEXT(input, IS_FRONT_VFACE(isFront, 1.0, -1.0)));

    float3 normalWS = input.normalWS.xyz;
    #if defined(_NORMALMAP) || defined(_FT_NORMAL2ND)
        float2 uvMain = input.uv0.xy * _MainTex_ST.xy + _MainTex_ST.zw;
        float3 bitangent = input.tangentWS.w * cross(input.normalWS.xyz, input.tangentWS.xyz);
        half3 normalTS = FairyToonNormalTS(FT_LAYER_CONTEXT(input, IS_FRONT_VFACE(isFront, 1.0, -1.0)), uvMain);
        normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz));
    #endif
    // SSAO / depth rim require camera-facing normals in both models, independently of _FlipNormal.
    normalWS *= IS_FRONT_VFACE(isFront, 1.0, -1.0);

    #if defined(_GBUFFER_NORMALS_OCT)
        float2 octNormalWS = PackNormalOctQuadEncode(normalize(normalWS));
        outNormalWS = half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
    #else
        outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
    #endif
    #ifdef _WRITE_RENDERING_LAYERS
        outRenderingLayers = EncodeMeshRenderingLayer();
    #endif
}
#endif

// ---------------------------------------------------------------------------------------------------------------------
// MotionVectors (object motion for DLAA / TAA). With the outline on it draws the outline hull pulled towards the camera,
// so the outline ring and the inside of double-sided parts both get the object's motion instead of the background's.

#if defined(FAIRYTOON_PASS_MOTIONVECTORS)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

struct FairyToonMotionAttributes
{
    float4 positionOS  : POSITION;
    float3 normalOS    : NORMAL;
    float4 tangentOS   : TANGENT;
    float2 uv0         : TEXCOORD0;
    float3 positionOld : TEXCOORD4;
    half4  color       : COLOR;
    float4 uv7         : TEXCOORD7;
    FT_LAYER_ATTRIBUTES
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct FairyToonMotionVaryings
{
    float4 positionCS                 : SV_POSITION;
    float4 positionCSNoJitter         : POSITION_CS_NO_JITTER;
    float4 previousPositionCSNoJitter : PREV_POSITION_CS_NO_JITTER;
    #if defined(FT_DISSOLVE_MOTION_PACKED)
        float3 uv0                    : TEXCOORD0; // xy UV; z carries object-position Z
    #else
        float2 uv0                    : TEXCOORD0;
    #endif
    FT_LAYER_VARYINGS
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

FairyToonMotionVaryings FairyToonMotionVertex(FairyToonMotionAttributes input)
{
    FairyToonMotionVaryings output = (FairyToonMotionVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionOS = input.positionOS.xyz;
    float3 previousOS = unity_MotionVectorsParams.x == 1 ? input.positionOld : input.positionOS.xyz;
    // Reuse the current camera displacement for old and new skin positions: it must not create velocity.
    float3 pupilShiftWS = FairyToonEFPupilVertexPositionWS(input.positionOS.xyz, input.normalOS, input.tangentOS, input.uv0)
        - TransformObjectToWorld(input.positionOS.xyz);
    float3 towardCamera = 0;
    #if defined(_FT_OUTLINE)
        FairyToonOutlineVertex v = FairyToonMakeOutlineVertex(positionOS, input.normalOS, input.tangentOS, input.color, input.uv7, input.uv0);
        v.positionOS += TransformWorldToObjectDir(pupilShiftWS, false);
        float3 offset = FairyToonOutlineOffsetOS(v);
        positionOS += offset;
        previousOS += offset;
        float3 positionWS = TransformObjectToWorld(positionOS) + pupilShiftWS;
        towardCamera = GetWorldSpaceNormalizeViewDir(positionWS) * (length(TransformObjectToWorldDir(offset, false)) * 2.0 + 0.0005);
    #endif

    float3 currentWS = TransformObjectToWorld(positionOS) + pupilShiftWS + towardCamera;
    float3 previousWS = mul(UNITY_PREV_MATRIX_M, float4(previousOS, 1)).xyz + pupilShiftWS + towardCamera;
    output.positionCS = TransformWorldToHClip(currentWS);
    output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, float4(currentWS, 1));
    output.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, float4(previousWS, 1));
    output.uv0.xy = input.uv0;
    FT_TRANSFER_LAYERS(input, output)
    #if defined(FT_DISSOLVE_MOTION_PACKED)
        // Match the visible outline's authored Z bias; exclude the motion-only towardCamera offset.
        #if defined(_FT_OUTLINE)
            UNITY_BRANCH if (FairyToonNeedsDissolvePosition())
            {
                float3 dissolveWS = TransformObjectToWorld(positionOS);
                dissolveWS -= GetWorldSpaceNormalizeViewDir(dissolveWS) * _OutlineZBias;
                positionOS = TransformWorldToObject(dissolveWS);
            }
        #endif
        // URP's 2D motion helper consumes clip XY/W only; both Z components are free.
        // Application SpaceWarp's CalcAswNdcMotionVectorFromCsPositions reads posCS.z;
        // it must not be used with this packing (restore clip Z before adding ASW).
        output.positionCSNoJitter.z = positionOS.x;
        output.previousPositionCSNoJitter.z = positionOS.y;
        output.uv0.z = positionOS.z;
    #endif
    return output;
}

float4 FairyToonMotionFragment(FairyToonMotionVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    #if defined(_FT_OUTLINE)
        // This variant draws the outline hull, whose coverage never includes main layers.
        #if defined(_ALPHATEST_ON) || defined(_SURFACE_TYPE_TRANSPARENT)
            float2 uvMain = input.uv0.xy * _MainTex_ST.xy + _MainTex_ST.zw;
            clip(FairyToonBaseColor(uvMain, input.uv0.xy, FT_DISSOLVE_POSITION_OS(input)).a - max(_Cutoff, 0.001));
        #endif
    #else
        FairyToonDepthClip(FT_LAYER_CONTEXT(input, IS_FRONT_VFACE(isFront, 1.0, -1.0)));
    #endif
    return float4(CalcNdcMotionVectorFromCsPositions(input.positionCSNoJitter, input.previousPositionCSNoJitter), 0, 0);
}
#endif

#endif
