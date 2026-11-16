// FairyToon wet surface: UV-stable soak front and normal-weighted rain. Original implementation.
#ifndef FAIRYTOON_WET_INCLUDED
#define FAIRYTOON_WET_INCLUDED

// One active character, written by FairyToonWetness. These are deliberately not material properties.
float4 _FT_Wet0; // world soak line, wetness, current water surface (or -1e5), seconds since leaving
float4 _FT_Wet1; // rain wetness, edge width in metres, trail bone count, reserved
float4 _FT_WetDebug; // lab ablation, all 0 normally: x no water specular, y no haze loss, z no trails / drops, w no step-1 darkening / smoothness

float FairyToonWetHash(float2 p)
{
    float3 h = frac(float3(p.x, p.y, p.x) * 0.1031);
    h += dot(h, h.yzx + 33.33);
    return frac((h.x + h.y) * h.z);
}

float FairyToonWetNoise(float2 uv)
{
    float2 p = uv * 64.0;
    float2 cell = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(FairyToonWetHash(cell), FairyToonWetHash(cell + float2(1, 0)), f.x),
                lerp(FairyToonWetHash(cell + float2(0, 1)), FairyToonWetHash(cell + 1.0), f.x), f.y);
}

half FairyToonWetMask(float3 positionWS, float3 N, float2 uv, out half edge)
{
    edge = 0.0;
    if (_FT_Wet0.y <= 0.0 && _FT_Wet1.x <= 0.0) return 0.0;
    float jitter = (FairyToonWetNoise(uv) * 2.0 - 1.0) * _WetEdgeNoise;
    float soakLine = _FT_Wet0.x;
    float w = max(_FT_Wet1.y, 0.001);
    float y = positionWS.y + jitter;
    half below = 1.0 - smoothstep(soakLine - w, soakLine + w, y);
    half soak = saturate(below * _FT_Wet0.y * (1.0 + _WetPorosity));
    half rain = _FT_Wet1.x * lerp(0.6, 1.0, saturate(N.y * 0.5 + 0.5));
    // A narrow smooth band just below the front; rain alone never paints a horizontal edge.
    float front = (y - (soakLine - w * 0.5)) / w;
    edge = exp2(-4.0 * front * front) * below * soak;
    return saturate(max(soak, rain));
}

void FairyToonApplyWet(inout FairyToonSurface s, float2 uv0)
{
    half edge;
    s.wet = FairyToonWetMask(s.positionWS, s.N, uv0, edge);
    if (s.wet <= 0.0) return;
    half gray = FairyToonGray(s.albedo);
    half3 dark = saturate(lerp(gray, s.albedo, _WetSaturation)) * (1.0 - _WetDarken);
    half step1 = _FT_WetDebug.w > 0.5 ? 0.0 : 1.0;
    s.albedo = lerp(s.albedo, dark, s.wet * lerp(0.25, 1.0, _WetPorosity) * step1);
    s.albedo *= 1.0 - edge * _WetEdgeDarken * step1;
}



// ---------------------------------------------------------------------------------------------------------------------
// Step 2: water running down the limbs. Trails, droplets and the film's long axis live in the frame of the nearest limb
// bone (published by FairyToonWetness), not in UV or world space: the water stays on the limb and runs along it.

float4 _FT_WetBoneStart[10]; // xyz upper joint (water starts here), w limb radius (m)
float4 _FT_WetBoneEnd[10];   // xyz lower joint, w distance along the chain above this bone (m)
float4 _FT_WetBoneSide[10];  // xyz a direction across the bone (zero angle, carried across joints), w chain id (hash seed)

// Four random channels from one evaluation per trail column / droplet seed.
float4 FairyToonWetHash4(float2 p)
{
    float4 h = frac(float4(p.xy, p.xy) * float4(0.1031, 0.1030, 0.0973, 0.1099));
    h += dot(h, h.wzxy + 33.33);
    return frac((h.xxyz + h.yzzw) * h.zywx);
}

struct FairyToonWetBoneFrame
{
    float  valid;
    float  along;   // metres down the chain from its top joint
    float  angle;   // around the bone, [0, 1)
    float  radius;  // limb radius used to lay out columns
    float  chain;   // bone index, seeds the hashes
    float3 axis;    // world, pointing down the bone
    float3 around;  // world, tangent around the bone at this point
};

// The frame of bone i at P: distance down the chain, angle around the bone (zero at the carried side vector).
FairyToonWetBoneFrame FairyToonWetBoneFrameAt(int bi, float3 P)
{
    FairyToonWetBoneFrame f = (FairyToonWetBoneFrame)0;
    float3 a = _FT_WetBoneStart[bi].xyz, ab = _FT_WetBoneEnd[bi].xyz - a;
    float len = max(length(ab), 1e-4);
    float3 axis = ab / len;
    float bt = dot(P - a, axis) / len;
    float3 d = P - (a + axis * (bt * len));
    float r = length(d);
    float3 side = _FT_WetBoneSide[bi].xyz;
    float3 dir = r > 1e-5 ? d / r : side;
    float3 side2 = cross(axis, side);
    f.valid = 1.0;
    f.along = _FT_WetBoneEnd[bi].w + bt * len;
    f.angle = atan2(dot(dir, side2), dot(dir, side)) * (0.5 / PI) + 0.5;
    f.radius = max(_FT_WetBoneStart[bi].w, 0.01);
    f.chain = _FT_WetBoneSide[bi].w;
    f.axis = axis;
    f.around = cross(axis, dir);
    return f;
}

// Nearest trail bone segment; index -1 when none.
int FairyToonWetNearestBoneIndex(float3 P)
{
    int count = (int)_FT_Wet1.z;
    float best = 1e9;
    int bi = -1;
    [loop] for (int i = 0; i < 10; i++)
    {
        if (i >= count) break;
        float3 a = _FT_WetBoneStart[i].xyz, ab = _FT_WetBoneEnd[i].xyz - a;
        float t = saturate(dot(P - a, ab) / max(dot(ab, ab), 1e-8));
        float3 c = a + ab * t;
        float d2 = dot(P - c, P - c);
        if (d2 < best) { best = d2; bi = i; }
    }
    return bi;
}

FairyToonWetBoneFrame FairyToonWetNearestBone(float3 P)
{
    int bi = FairyToonWetNearestBoneIndex(P);
    if (bi < 0) return (FairyToonWetBoneFrame)0;
    return FairyToonWetBoneFrameAt(bi, P);
}

// Near a chained joint (knee, elbow), the other bone of the pair and how much of the result it should take, so the
// trails cross the crease smoothly when the joint bends. Returns -1 away from a joint. Band: 4 cm either side.
int FairyToonWetJointPartner(int bi, float3 P, out float otherWeight)
{
    otherWeight = 0.0;
    int count = (int)_FT_Wet1.z;
    float chain = _FT_WetBoneSide[bi].w;
    int upper = -1, lower = -1;
    if (_FT_WetBoneEnd[bi].w > 0.0 && bi > 0 && _FT_WetBoneSide[bi - 1].w == chain) { upper = bi - 1; lower = bi; }
    else if (bi + 1 < count && _FT_WetBoneSide[bi + 1].w == chain && _FT_WetBoneEnd[bi + 1].w > 0.0) { upper = bi; lower = bi + 1; }
    if (upper < 0) return -1;
    float3 joint = _FT_WetBoneStart[lower].xyz;
    float3 axisUpper = SafeNormalize(_FT_WetBoneEnd[upper].xyz - _FT_WetBoneStart[upper].xyz);
    float3 axisLower = SafeNormalize(_FT_WetBoneEnd[lower].xyz - joint);
    float h = dot(P - joint, SafeNormalize(axisUpper + axisLower));
    const float band = 0.04;
    if (abs(h) >= band) return -1;
    float lowerWeight = smoothstep(-band, band, h);
    otherWeight = bi == lower ? 1.0 - lowerWeight : lowerWeight;
    return bi == lower ? upper : lower;
}

float FairyToonWetFlowTime()
{
    // Integral of velocity (1 - t/8): the trails run while fresh and stop after eight seconds; they keep running in rain.
    float t = clamp(_FT_Wet0.w, 0.0, 8.0);
    return _FT_Wet1.x > 0.0 ? _Time.y : t - t * t * 0.0625;
}

// Wet fabric on a limb, in the bone frame. u: columns around the limb (integer per column, seamless), v: metres down the
// chain. Water wicks into fabric: each wet column holds a soft, wandering streak of soaked fabric (darker, less sheer,
// fuzzy edged) rather than a crisp liquid line; neighbouring streaks overlap into a grainy field with blotches. A few
// short stretches of a streak still carry free water (a glint), and tiny droplets sit on it. While the character is fresh
// out of the water (or in rain) the streaks are still growing downwards. Our own construction.
struct FairyToonWetTrailSample
{
    float track;     // soaked fabric, 0-1 (darkening)
    float shine;     // free water on the surface (glint coverage)
    float drop;      // tiny droplet coverage
};

float FairyToonWetDisc(float2 delta, float radius, float footprint)
{
    float distance = length(delta);
    float disc = 1.0 - smoothstep(radius - footprint * 0.5, radius + footprint * 0.5, distance);
    // Keep sub-pixel drops as a point of light rather than losing them.
    return max(disc, saturate(1.0 - distance / max(footprint, 1e-5)) * saturate(radius / max(footprint, 1e-5)));
}

FairyToonWetTrailSample FairyToonWetTrailColumn(float c, float x0, float v, float chain, float footprint)
{
    FairyToonWetTrailSample o = (FairyToonWetTrailSample)0;
    float4 rnd = FairyToonWetHash4(float2(c + chain * 131.0, chain * 17.0 + 3.0));
    float4 grp = FairyToonWetHash4(float2(floor(c / 4.0) + chain * 53.0, chain * 7.0 + 11.0));
    // Clustered coverage: groups of columns soak together, with drier gaps between groups.
    if (rnd.w > saturate(_WetStreakAmount) * lerp(0.15, 1.6, grp.x * grp.x)) return o;
    // Fresh: the streak front is still running down (it stops a few seconds after leaving the water).
    float grow = FairyToonWetFlowTime() * max(_WetStreakFlow, 0.0) * lerp(0.6, 1.4, rnd.x);
    float period = lerp(0.35, 0.9, rnd.z);                  // metres between streak starts in this column
    float local = v + rnd.y * period;
    float cycle = floor(local / period);
    float phase = local / period - cycle;
    float length01 = lerp(0.55, 1.0, rnd.x);                // long streaks: most run the whole limb
    float4 cyc = FairyToonWetHash4(float2(c + chain * 131.0, cycle + 7.0));
    // Meander (metres): slow sway plus a finer wobble.
    float wander = max(_WetStreakAxis, 0.0) * (0.003 * sin(v * 16.0 + cyc.x * 6.2832) + 0.0012 * sin(v * 47.0 + cyc.y * 6.2832)); // _WetStreakAxis: Trail Wander
    float x = x0 - (cyc.z - 0.5) * 0.003 - wander;
    // Width 1.5-6 mm, swelling and pinching along the streak; soft, wicked edges (Gaussian profile).
    float halfWidth = max(_WetStreakBump, 0.05) * lerp(0.0008, 0.003, cyc.w) * (0.7 + 0.6 * (0.5 + 0.5 * sin(v * 29.0 + rnd.w * 6.2832)));
    halfWidth = max(halfWidth, footprint * 0.75);
    float profile = exp2(-1.4427 * (x * x) / (halfWidth * halfWidth));
    // Ends fade over a few centimetres; the lower end advances while fresh.
    float top = smoothstep(0.0, 0.06, phase);
    float bottom = 1.0 - smoothstep(length01 - 0.06 + grow / period, length01 + grow / period, phase);
    // Blotchy darkness along the streak: soaked more in some stretches than others.
    float blotch = 0.25 + 0.75 * (0.5 + 0.5 * sin(v * 9.0 + cyc.x * 11.0)) * (0.5 + 0.5 * sin(v * 23.0 + cyc.y * 7.0));
    float strength = lerp(0.45, 1.0, rnd.y) * blotch;
    o.track = profile * top * bottom * strength;
    // Free water: a few 1-4 cm stretches of a streak still hold a film of water and glint along their length.
    float seg = 0.025;
    float4 g = FairyToonWetHash4(float2(c * 5.0 + chain * 29.0, floor(v / seg)));
    float inSeg = frac(v / seg);
    float segOn = step(g.x, saturate(_WetGlintAmount)) * smoothstep(0.0, 0.25, inSeg) * (1.0 - smoothstep(lerp(0.4, 1.0, g.y) - 0.2, lerp(0.4, 1.0, g.y), inSeg));
    o.shine = segOn * exp2(-1.4427 * (x * x) / (0.35 * halfWidth * 0.35 * halfWidth + footprint * footprint)) * top * bottom;
    // Tiny droplets sitting on the streak: one chance per 9 mm.
    float cell = 0.009;
    float k = floor(v / cell);
    float4 d = FairyToonWetHash4(float2(c * 3.0 + chain * 71.0, k));
    if (d.x < saturate(_WetDropAmount))
    {
        float cy = (k + lerp(0.2, 0.8, d.y)) * cell;
        float cx = (d.z - 0.5) * 2.0 * halfWidth;
        float r = lerp(0.4, 1.0, d.w) * max(_WetDropScale, 0.0001); // _WetDropScale: largest droplet radius (m)
        o.drop = FairyToonWetDisc(float2(x - cx, v - cy), r, footprint) * top * bottom;
    }
    return o;
}

// The pixel's column and two neighbours either side, so a wide, wandering streak is never clipped at a column edge.
FairyToonWetTrailSample FairyToonWetTrail(float u, float v, float columns, float metresPerU, float chain, float footprint)
{
    FairyToonWetTrailSample o = (FairyToonWetTrailSample)0;
    float base = floor(u);
    [unroll] for (int i = -3; i <= 3; i++)
    {
        float c = base + i;
        float wrapped = c - columns * floor(c / columns);
        float x0 = (u - (c + 0.5)) * metresPerU;
        FairyToonWetTrailSample s = FairyToonWetTrailColumn(wrapped, x0, v, chain, footprint);
        // Overlapping streaks soak the fabric further, with diminishing returns.
        o.track = 1.0 - (1.0 - o.track) * (1.0 - s.track);
        o.shine = max(o.shine, s.shine);
        o.drop = max(o.drop, s.drop);
    }
    return o;
}

// Forward / SeeThroughHair only. Outline keeps the inexpensive step-1 global darkening above.
void FairyToonApplyWetDetails(inout FairyToonSurface s, float2 uv0)
{
    s.wetFilmNormal = s.N;
    s.wetStreak = 0.0;
    s.wetAlong = 0.0;
    s.wetDrop = 0.0;
    s.wetDropNormal = s.N;
    s.wetShine = 0.0;
    // Pixel footprint in metres, taken before any per-pixel early-out (derivatives in divergent flow are undefined,
    // and the angle wraps around the limb, so fwidth of the trail coordinate itself would spike at the seam).
    float3 dpx = ddx(s.positionWS), dpy = ddy(s.positionWS);
    float footprint = max(max(length(dpx), length(dpy)), 1e-5);
    if (s.wet <= 0.0 || _FT_WetDebug.z > 0.5) return;
    int bi = FairyToonWetNearestBoneIndex(s.positionWS);
    if (bi < 0) return;
    FairyToonWetBoneFrame f = FairyToonWetBoneFrameAt(bi, s.positionWS);
    // Bone axis on the surface: the long axis of the glints and the way the water runs.
    float3 along = f.axis - s.N * dot(f.axis, s.N);
    s.wetAlong = dot(along, along) > 1e-6 ? normalize(along) : 0.0;
    if (_WetStreakAmount <= 0.0) return;
    float aboveWater = saturate((s.positionWS.y - _FT_Wet0.z) / 0.003);
    if (aboveWater <= 0.0) return;
    float columns = max(3.0, round(2.0 * PI * f.radius / max(_WetStreakScale, 0.002)));
    float metresPerU = 2.0 * PI * f.radius / columns;
    FairyToonWetTrailSample w = FairyToonWetTrail(f.angle * columns, f.along, columns, metresPerU, f.chain, footprint);
    // Within 4 cm of a knee / elbow, also evaluate the other bone of the pair and blend across the crease. Both share the
    // chain's distance, side vector, radius and seeds, so on a straight limb they agree and on a bent one this hides the fold.
    float otherWeight;
    int other = FairyToonWetJointPartner(bi, s.positionWS, otherWeight);
    if (other >= 0 && otherWeight > 0.0)
    {
        FairyToonWetBoneFrame g = FairyToonWetBoneFrameAt(other, s.positionWS);
        FairyToonWetTrailSample o = FairyToonWetTrail(g.angle * columns, g.along, columns, metresPerU, g.chain, footprint);
        w.track = lerp(w.track, o.track, otherWeight);
        w.shine = lerp(w.shine, o.shine, otherWeight);
        w.drop = lerp(w.drop, o.drop, otherWeight);
    }
    float wet = s.wet * aboveWater;
    s.wetStreak = saturate(w.track) * wet;
    s.wetShine = w.shine * wet;
    s.wetDrop = w.drop * wet;
    // Soaked fabric: darker and less sheer, the main read of wet stockings.
    s.albedo *= 1.0 - s.wetStreak * saturate(_WetStreakDarken);
    #if defined(_SURFACE_TYPE_TRANSPARENT)
        s.alpha = lerp(s.alpha, 1.0, s.wetStreak * saturate(_WetStreakDarken) * saturate(s.alpha * 8.0));
    #endif
}

float FairyToonWetFresnel(float cosine)
{
    float grazing = 1.0 - saturate(cosine);
    return 0.02 + 0.98 * grazing * grazing * grazing * grazing * grazing;
}

// Anisotropic GGX, no 1/pi, correlated Smith visibility, the existing D*vis <= 20 clamp. G: the long (stretched) axis.
float FairyToonWetGGX(float3 N, float3 G, float3 V, float3 L, float acrossRoughness, float alongRoughness)
{
    G = G - N * dot(G, N);
    G = dot(G, G) > 1e-6 ? normalize(G) : SafeNormalize(cross(N, float3(1, 0, 0)));
    float3 T = SafeNormalize(cross(G, N));
    float3 H = SafeNormalize(V + L);
    float NoL = saturate(dot(N, L)), NoV = max(saturate(dot(N, V)), 0.0001);
    float NoH = saturate(dot(N, H));
    float ax = max(acrossRoughness, 0.0025), ay = max(alongRoughness, 0.0025);
    float3 scaledH = float3(dot(T, H) / ax, dot(G, H) / ay, NoH);
    float d = max(dot(scaledH, scaledH), 1e-6);
    float D = 1.0 / max(ax * ay * d * d, 1e-12);
    float lambdaV = length(float3(ax * dot(T, V), ay * dot(G, V), NoV));
    float lambdaL = length(float3(ax * dot(T, L), ay * dot(G, L), NoL));
    float vis = 0.5 / max(NoL * lambdaV + NoV * lambdaL, 1e-5);
    return min(max(D * vis - 6e-5, 0.0), 20.0) * FairyToonWetFresnel(dot(L, H)) * NoL;
}

// The water on top of the material's own specular, in both models. Wet fabric gets darker, not brighter: free water
// glints only on a few stretches of the streaks (a sharp lobe stretched along the bone) and on tiny droplets (solid
// sparkles, no lens ring). _WetSpecular remains a lilToon-only multiplier; Endfield uses _WetFilm directly.
half3 FairyToonWetSpecular(FairyToonSurface s, half characterShadow, float2 uv0)
{
    if (s.wet <= 0.0 || _WetFilm <= 0.0 || _FT_WetDebug.x > 0.5) return 0.0;
    float strength = _WetFilm;
    #if !defined(_FT_ENDFIELD)
        strength *= _WetSpecular;
    #endif
    if (strength <= 0.0 || (s.wetShine <= 0.0 && s.wetDrop <= 0.0)) return 0.0;
    float3 N = s.N;
    float3 G = dot(s.wetAlong, s.wetAlong) > 0.5 ? s.wetAlong : float3(0.0, -1.0, 0.0);
    float acrossRoughness = lerp(0.03, 0.002, saturate(_WetSmoothness)); // _WetSmoothness: Glint Sharpness
    float alongRoughness = lerp(acrossRoughness, 0.12, saturate(_WetFilmStretch));
    Light light = GetMainLight(TransformWorldToShadowCoord(s.positionWS), s.positionWS, half4(1, 1, 1, 1));
    float attenuation = light.distanceAttenuation * light.shadowAttenuation * characterShadow;
    #if defined(_LIGHT_LAYERS)
        if (!IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer())) attenuation = 0.0;
    #endif
    float glint = FairyToonWetGGX(N, G, s.V, light.direction, acrossRoughness, alongRoughness) * s.wetShine;
    // A droplet is a tiny bead: whatever its orientation, some part of it mirrors the light, so it reads as a solid
    // speck, brighter when the light is in front. A broad lobe on the base normal stands in for the bead's curvature.
    float3 H = SafeNormalize(s.V + light.direction);
    float bead = 4.0 * pow(saturate(dot(N, H)), 16.0) * saturate(dot(N, light.direction) + 0.2);
    float drops = bead * s.wetDrop;
    half3 environment = GlossyEnvironmentReflection(reflect(-s.V, N), s.positionWS, 0.05, 1.0, s.screenUV);
    // Fade towards the silhouette: at grazing view the Smith / Fresnel terms grow until the clamp.
    float grazingFade = smoothstep(0.08, 0.35, saturate(dot(N, s.V)));
    return grazingFade * strength * ((glint + drops) * light.color * attenuation
        + environment * (0.08 * s.wetShine + 0.1 * s.wetDrop));
}
#endif
