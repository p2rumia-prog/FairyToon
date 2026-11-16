# FairyToon variants - review correction report

> Current behavior: see [independent review corrections](MergeConsistencyReport.md#independent-review-corrections-2026-11-05). Earlier claims that Endfield/DepthNormals obey FlipNormal, Toon uses an unshadowed single probe, or Apply preserves all explicit queues are superseded. Gamma parity and optical/fur special cases remain unless corrected there. Current offline result: 94/94 Node checks and 180 DX11 configurations / 380 stages pass; Unity, Vulkan and visual checks were not run.

> Historical pre-merge evidence. Current merged-tree behavior and checks are in [MergeConsistencyReport.md](MergeConsistencyReport.md); the main-shader hash freeze and missing smoothness-ST limitation below are superseded.

2026-11-05; worktree branch fairytoon-variants, HEAD/base 579f055. This report supersedes the earlier verification report. Only Packages/com.nw.fairytoon was written. Unity was never launched; no Git commit/checkout/reset/pull or external-service writes. The main project's Library/PackageCache and cached compiler references were read-only.

## Per-file corrections

| File relative to package | Correction |
|---|---|
| Editor/FairyToonMaterialSetup.cs | Gem additive RGB; preserve optical ZWrite/Cull; correct all Fur body/shell mode states and auxiliary enabling; reserve Gem PRE tag; remove dead FurSecond pass-name API call. |
| Editor/FairyToonConverter.cs | Only Fur Cutout selects cutout body coverage; TwoPass body selects transparency. Existing by-name snapshot conversion preserves authored optical depth/culling. |
| Editor/FairyToonGUI.cs | Optical ZWrite editable; ExitGUI after shader replacement; hide dormant Gem Outline controls. |
| Shaders/FairyToonGem.shader | Cull Off / ZWrite Off / One-One defaults; dedicated GemPre before Forward, replacing Outline. Default coverage is Transparent. |
| Shaders/FairyToonVariants.hlsl | Minimal black/fog Gem PRE; additive Gem final fades towards black and retains material alpha; document absent smoothness ST uniform. |
| Shaders/FairyToonFur.shader | Default transparent straight-alpha body; remove normal/anisotropy/Backlight/Glitter/Reflection/MatCap/ordinary-Rim/Emission shell keywords and reflection-probe shell variants. |
| Shaders/FairyToonFur.hlsl | Vertex-normal shells; AO before lighting; Toon diffuse/shadow then rim shade and fur rim; corrected-light RGB inverse lighting; cheap Endfield main light + SH reused for rim. |
| Shaders/FairyToonRefraction.shader | Transparent-100 queue; default transparent coverage; existing ZWrite On retained. |
| README.md | Correct optical topology/depth, per-mode Fur state table, cheap shell shading, smoothness ST limitation and auxiliary-pass limits. |
| Shaders/README.md | Updated pass/include responsibilities, defaults, alpha/fog, shell lighting and limitations. |
| Tests~/VerifyLayers.cjs | Execute actual C# render-state derivation; additive Gem/alpha/default/normal/depth/routing regressions; Cutout/TwoPass PRE A2C; vertex-normal and source inverse-light checks. |
| Tests~/CompileShaders.ps1 | Include GemPre in forward coverage; permit its entirely stripped material buffer; derive expected normal/glitter UV interfaces from actual pass pragmas. Existing per-stage keyword filtering remains active. |
| Tests~/VariantsPlan.md | Record and complete the review-correction checklist. |
| Tests~/VariantsReport.md | Current per-file, source correspondence, verification and resource evidence. |

Deleted exactly the requested Tests~ metadata: ReportVariants.cjs.meta, VariantsPlan.md.meta, VariantsReport.md.meta, VerifyOfflineTools.cjs.meta. Unity ignores Tests~. Existing new shader/include .meta files are retained.

Earlier uncommitted variant infrastructure is retained: FairyToonInput.hlsl's default-empty buffer extension, FairyToonVariantInput.hlsl's per-shader constants, ReportVariants.cjs and VerifyOfflineTools.cjs. No new changes to those files were needed for these corrections.

## Correspondence with installed lilToon

- **Gem:** lts_gem.shader:752-817 uses PRE/SRPDefaultUnlit One/Zero then Forward/UniversalForward One/One, both Cull [_Cull]; defaults at 578-592 are Cull Off and ZWrite Off. FairyToon now follows that topology. PRE writes black with normal scene fog and alpha zero; final RGB fogs towards black, so fog is added once and front/interior faces accumulate independently of triangle order at default depth/culling. Final material alpha is retained, as source OVERRIDE_OUTPUT returns sampled RGBA. Snapshot conversion and Apply do not overwrite authored Cull or ZWrite. Outline values stay stored, without a competing SRPDefaultUnlit pass.
- **Fur body:** lts_fur/lts_fur_two body defaults are premultiplied One/OneMinusSrcAlpha with ZWrite On; lts_fur_cutout is One/Zero with ZWrite On/A2C On. FairyToon's straight RGB requires SrcAlpha/OneMinusSrcAlpha for equal visible contributions: source (C*a)*One + D*(1-a) equals Fairy C*SrcAlpha + D*(1-a). TwoPass body remains transparent; only its first shells are cut out.
- **Fur shell PRE:** lts_fur_two.shader:870-876 writes depth, One/Zero, AlphaToMask On. FairyToon's first shell derives _FurAlphaToMask=1 for Cutout/TwoPass and 0 for Transparent; the second full-length blended pass remains depth-off/A2C-off. All Fur alpha-channel blends are One/OneMinusSrcAlpha, including Cutout body.
- **Fur auxiliaries:** lilMaterialUtils.cs:400-404 enables shadow/depth/normals/motion for all these Fur modes. FairyToon now keeps every body auxiliary enabled. Transparent-queue filtering may still exclude these from normal URP camera prepasses.
- **Fur lighting:** lil_pass_forward_fur.hlsl:169-218 uses interpolated vertex normals, shadow, rim shade and fur rim. AO precedes lighting. FairyToon now follows that order and omits ordinary body surface effects from shells. Fur rim uses gray(saturate((1-lightColor)*sqrt(max(lightColor,0)))) from lil_common_macro.hlsl:2308-2311. Endfield shells use one shadowed main-light sample, SH, rim shade and fur rim, without full Endfield BRDFs.
- **Refraction:** lts_ref.shader:592,621 defaults to ZWrite On and Transparent-100. FairyToon retains authored/converted ZWrite and exposes it in the GUI, with queue 2900 by default. Existing verified refraction composition maths are unchanged.
- **Smoothness transform:** FairyToon's common _SmoothnessTex is NoScaleOffset with no _SmoothnessTex_ST uniform. Blur continues sampling transformed main UV. Saved lilToon smoothness transforms are not evaluated; this capability is documented rather than changing the shared main material layout.
- **Inspector/pass API:** shader replacement exits GUI traversal after PropertiesChanged. FurSecond is routed only through its UniversalForwardOnly LightMode, never its pass Name.

| Fur Mode | Body RGB; ZWrite / A2C | First shells RGB; ZWrite / A2C | Second shells |
|---|---|---|---|
| Transparent | SrcAlpha/OneMinusSrcAlpha; On / Off | SrcAlpha/OneMinusSrcAlpha; Off / Off | Disabled |
| Cutout | One/Zero; On / On | One/Zero; On / On | Disabled |
| 2 Pass | SrcAlpha/OneMinusSrcAlpha; On / Off | One/Zero; On / On, shortened PRE | SrcAlpha/OneMinusSrcAlpha; Off / Off |

## Executed checks

Node: node --test Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs Packages/com.nw.fairytoon/Tests~/VerifyOfflineTools.cjs - **54/54 PASS**, zero failures. New contracts failed before the corresponding fixes, including the separate Gem output-alpha correction.

Standalone Roslyn compile of the complete current Editor assembly against cached Unity 6000.6.7f1 references: **exit 0**, without a Unity process. Output paths were relocated inside package scratch; analyzers/additional files excluded. Two existing CS0618 warnings remain at FairyToonSeeThroughSetup.cs:78 (obsolete FindObjectsSortMode / FindObjectsByType).

DX11 D3DCompiler, installed URP 17.6/core, UNITY_VERSION=600060: **132 configurations / 290 stage compilations PASS**. This includes the final Gem alpha correction matrix. All used per-shader UnityPerMaterial layouts matched across stages/passes/models. Gem PRE uses no material constants after stripping. Actual interfaces verify conditional extra UVs and VS-to-GS-only Fur vertex IDs; no extra UVs leak into PRE or feature-off shells. Every stage stays below 128 textures / 16 samplers; maximum overall PS is 57 / 12. Regular Toon first shells are 18 / 6 (previously 44 / 10); regular Endfield shells 10 / 2. With APV stress, shell maximum is 28 / 7. Gem PRE is 0 / 0.

| Matrix | Configurations | Stages |
|---|---:|---:|
| Main + specials, all six shading models, Refraction Blur off/on | 74 | 160 |
| Specials Toon, layers/normals/outline/reflection/features off | 23 | 48 |
| Main + specials Endfield Face, Transparent, atlas + APV | 7 | 16 |
| Toon Fur shells, atlas + APV | 2 | 6 |
| Transparent Fur forward paths, Toon + Endfield Face | 6 | 16 |
| Special forward paths, no layers, extra-UV normal / UV0 glitter | 6 | 14 |
| Special forward paths, no layers, UV0 normal / extra-UV glitter | 6 | 14 |
| Final Gem alpha correction, all six models + PRE | 7 | 14 |
| Final Gem alpha correction, atlas + APV Endfield Face | 1 | 2 |

Main FairyToon.shader SHA256 after newline normalization remains 71f5ffd808cd3310f504fdc4413c229e5464b22f471ee6a6c1df970a0e81c849. FairyToonCore.hlsl and FairyToonPasses.hlsl are unchanged. Main's empty buffer hook adds no field/pass/keyword/interface. git diff --check passed. Compiler scratch (EditorCompiler~/OfflineCompiler~) and Review*.log files remain inside package Tests~: automatic approval review rejected both scoped recursive cleanup and explicit-file Remove-Item with "blocked by policy". They are temporary verification artifacts, not source changes; no cleanup approval can be requested under this session policy.

Independent read-only review found no blocking shader issues; the minor Gem Outline inspector visibility and retained output-alpha discrepancies were corrected and verified.

## Per-pass resources and declared variants

T/S = textures / samplers; measured maxima across the matrices above. Source-declared material/pipeline counts are upper bounds before Unity stripping, including conditional pragma branches; they are not actual build counts. Fur shell material combinations fell from 55296/18432 to 48/16 for FurForward/FurSecond.

### FairyToon

| Pass | VS T/S | GS T/S | PS T/S | Material | Pipeline | Combined upper bound |
|---|---:|---:|---:|---:|---:|---:|
| Forward | 0 / 0 | - | 56 / 11 | 276480 | 245760 | 67947724800 |
| Outline | 1 / 1 | - | 8 / 5 | 6 | 192 | 1152 |
| ShadowCaster | 0 / 0 | - | 6 / 1 | 6 | 4 | 24 |
| CharacterShadow | 0 / 0 | - | 6 / 1 | 6 | 2 | 12 |
| DepthOnly | 0 / 0 | - | 6 / 1 | 6 | 2 | 12 |
| DepthNormals | 0 / 0 | - | 9 / 3 | 36 | 8 | 288 |
| MotionVectors | 1 / 1 | - | 2 / 1 | 12 | 2 | 24 |
| SeeThroughMask | 0 / 0 | - | 7 / 2 | 6 | 2 | 12 |
| SeeThroughFakeShadow | 0 / 0 | - | 6 / 1 | 6 | 2 | 12 |
| SeeThroughEarlyZ | 0 / 0 | - | 6 / 1 | 6 | 2 | 12 |
| SeeThroughHair | 0 / 0 | - | 56 / 11 | 276480 | 245760 | 67947724800 |

Pass sum: 135895451148 source combinations.

### FairyToon/Refraction

| Pass | VS T/S | GS T/S | PS T/S | Material | Pipeline | Combined upper bound |
|---|---:|---:|---:|---:|---:|---:|
| Forward | 0 / 0 | - | 57 / 12 | 184320 | 245760 | 45298483200 |
| Outline | 1 / 1 | - | 8 / 5 | 2 | 192 | 384 |
| ShadowCaster | 0 / 0 | - | 6 / 1 | 2 | 4 | 8 |
| CharacterShadow | 0 / 0 | - | 6 / 1 | 2 | 2 | 4 |
| DepthOnly | 0 / 0 | - | 6 / 1 | 2 | 2 | 4 |
| DepthNormals | 0 / 0 | - | 9 / 3 | 12 | 8 | 96 |
| MotionVectors | 1 / 1 | - | 2 / 1 | 4 | 2 | 8 |

Pass sum: 45298483704 source combinations.

### FairyToon/Fur

| Pass | VS T/S | GS T/S | PS T/S | Material | Pipeline | Combined upper bound |
|---|---:|---:|---:|---:|---:|---:|
| Forward | 0 / 0 | - | 56 / 11 | 276480 | 245760 | 67947724800 |
| FurForward | 1 / 1 | 1 / 1 | 28 / 7 | 48 | 30720 | 1474560 |
| FurSecond | 1 / 1 | 1 / 1 | 26 / 7 | 16 | 30720 | 491520 |
| ShadowCaster | 0 / 0 | - | 6 / 1 | 6 | 4 | 24 |
| CharacterShadow | 0 / 0 | - | 6 / 1 | 6 | 2 | 12 |
| DepthOnly | 0 / 0 | - | 6 / 1 | 6 | 2 | 12 |
| DepthNormals | 0 / 0 | - | 9 / 3 | 36 | 8 | 288 |
| MotionVectors | 0 / 0 | - | 6 / 1 | 6 | 2 | 12 |

Pass sum: 67949691228 source combinations.

### FairyToon/Gem

| Pass | VS T/S | GS T/S | PS T/S | Material | Pipeline | Combined upper bound |
|---|---:|---:|---:|---:|---:|---:|
| GemPre | 0 / 0 | - | 0 / 0 | 1 | 8 | 8 |
| Forward | 0 / 0 | - | 54 / 12 | 46080 | 245760 | 11324620800 |
| ShadowCaster | 0 / 0 | - | 6 / 1 | 2 | 4 | 8 |
| CharacterShadow | 0 / 0 | - | 6 / 1 | 2 | 2 | 4 |
| DepthOnly | 0 / 0 | - | 6 / 1 | 2 | 2 | 4 |
| DepthNormals | 0 / 0 | - | 9 / 3 | 12 | 8 | 96 |
| MotionVectors | 1 / 1 | - | 2 / 1 | 4 | 2 | 8 |

Pass sum: 11324620928 source combinations.

## Reproduction

Use CompileShaders.ps1 with -PackageCache D:/UNITY_PROJECTS/LiminalNW/Library/PackageCache; do not launch Unity. Matrix-specific argument lists below use comma-separated arrays for readability; in PowerShell pass arrays as @('Refraction','Fur','Gem'), etc.

- Main + specials, all six shading models, Refraction Blur off/on: Default arguments.
- Specials Toon, layers/normals/outline/reflection/features off: -Shaders Refraction,Fur,Gem -Models Toon -NoLayers -NoNormalMap -NoOutline -NoReflection -SurfaceFeatures None.
- Main + specials Endfield Face, Transparent, atlas + APV: -Models EndfieldFace -Coverage Transparent -ProbeAtlas -ProbeVolumes -RefractionBlur Off.
- Toon Fur shells, atlas + APV: -Shaders Fur -Models Toon -PassNames FurForward,FurSecond -ProbeAtlas -ProbeVolumes.
- Transparent Fur forward paths, Toon + Endfield Face: -Shaders Fur -Models Toon,EndfieldFace -Coverage Transparent -OnlyForward.
- Special forward paths, no layers, extra-UV normal / UV0 glitter: -Shaders Refraction,Fur,Gem -Models Toon -OnlyForward -NoLayers -Normal2ndUV UV1To3 -GlitterUV UV0 -RefractionBlur Off.
- Special forward paths, no layers, UV0 normal / extra-UV glitter: -Shaders Refraction,Fur,Gem -Models Toon -OnlyForward -NoLayers -Normal2ndUV UV0 -GlitterUV UV1To3 -RefractionBlur Off.
- Final Gem alpha correction, all six models + PRE: -Shaders Gem -OnlyForward.
- Final Gem alpha correction, atlas + APV Endfield Face: -Shaders Gem -Models EndfieldFace -OnlyForward -Coverage Transparent -ProbeAtlas -ProbeVolumes.

Variant table reproduction: node Packages/com.nw.fairytoon/Tests~/ReportVariants.cjs --package-cache D:/UNITY_PROJECTS/LiminalNW/Library/PackageCache --json.

## Limits and not run

- NOT RUN: Unity import/compile/play, screenshots or rendered comparison, Frame Debugger draw ordering, actual SRP Batcher operation, Vulkan/SPIR-V, XR/native-half execution, or the Unity 6000.6.0f1 Editor. Standalone DX11 checks use the requested version define; they do not replace Unity/platform validation.
- URP Opaque Texture omits other transparent objects. Nine-tap Blur approximates lilToon's mip/Gaussian filter because the opaque copy has no mip chain. Refraction's shared Reflection and FairyToon Toon/Endfield diffuse remain lighting-model approximations.
- Fur auxiliary geometry remains body-only. Source shadow dithering / additional subpass alpha clipping and DepthOnly root FurNoise/FurMask coverage are not reproduced. Enabled transparent body passes may still be filtered by the renderer queue. Touch collision and legacy ForwardAdd remain inactive in URP. Actual Fur three-tag/Gem two-tag scheduling requires Unity validation.
