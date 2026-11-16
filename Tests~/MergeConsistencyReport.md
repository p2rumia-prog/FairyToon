# FairyToon merged-tree consistency report

## Independent review corrections (2026-11-05)

This section supersedes the older normal, queue, specular and probe rules below and in the Sleeve/Variants historical reports. The nine issues were checked against the existing working tree, installed lilToon and URP 17.6 sources. Only this package was edited; no Unity, material asset edits or Git state-changing commands were used.

| File(s) | Current correction |
|---|---|
| Shaders/FairyToonCore.hlsl | Clustered probes bypass empty legacy cube fallback; Reflection uses position/screen-UV probe lookup, roughness >=0.002 and FairyToon shadowmix attenuation for both Toon and Endfield, preserving the toon terminator, Receive Shadow sliders, AO, face map and character self-shadow. Extract ShadeBase for identical main/optical base layers. |
| Shaders/FairyToonPasses.hlsl | Share ForwardSurface across main/optical/shell entry points. Toon mapped normal obeys FlipNormal; Endfield always faces mapped N; DepthNormals always faces mapped N. Keep Gem tilt and strand coverage exceptions. |
| Shaders/FairyToonEndfield.hlsl | Backface handling no longer depends on FlipNormal. ShadeBase supplies facing geometric origN, so Face uses -original geometric N on backs. Front-face math is unchanged. |
| Shaders/FairyToonVariants.hlsl | Remove duplicated surface/base-lighting setup, call shared helpers, use five-argument URP probe sampling in Gem. Retain post-optics framebuffer RGB / coverage rules. |
| Shaders/FairyToonFur.hlsl | Use shared surface construction; keep geometric Toon shells and align both geometric normals for Endfield shell RimShade strength blends. |
| Shaders/FairyToon.shader, FairyToonRefraction.shader, FairyToonFur.shader, FairyToonGem.shader | Remove NoScaleOffset on eight consumed transforms each: Bump2ndScaleMask, AnisotropyScaleMask, BacklightColorTex, SmoothnessTex, MetallicGlossMap, ReflectionColorTex and both MatCap blend masks. Gamma attributes remain. |
| Editor/FairyToonMaterialSetup.cs | Restore coverage repair while preserving authored low queues; track automatic Hair2460 assignment with a private material tag plus conservative legacy-state recognition. |
| Editor/FairyToonConverter.cs | Mark copied source queues as authored so cleared shader keywords cannot misclassify imported Transparent2460 as old Hair state. Profile coverage changes still run Apply. |
| Editor/FairyToonGUI.cs | Mixed popups use showMixedValue plus an unselected index so choosing the first target's value applies to all; expose consumed transforms, including both MatCap textures, in Advanced. |
| README.md, Shaders/README.md | Document current normals/probes/queues and direct-authored lilToon Gamma slider semantics (Linear: .04 reflectance ~.003096; .5 metallic ~.214041). No numeric migration. |
| Tests~/VerifyLayers.cjs, CompileShaders.ps1, VerifyOfflineTools.cjs | Replace superseded expectations with review regressions; execute distinct shadow/probe branches with the native offline DX11 compiler. |

### Normal and layer-order decisions

Toon keeps the lilToon mapped-normal FlipNormal test and unmodified geometric origN. Endfield faces mapped N before anisotropy/coverage and faces geometric origN in the shading copy; Face therefore receives -origN instead of a substituted mapped normal. DepthNormals uses raster facing independently of either model's control, including OCT encoding. Gem retains its single interior reversal plus tilt in both models. Endfield Fur shells skip ShadeBase, so they align origN with already-faced geometric N before RimShade.

Main and optical paths now share: surface context/main UV/view/normal -> facing exception -> anisotropy -> mapped N.V layer coverage -> layered albedo; then emission albedo snapshot -> RimShade -> model diffuse/additional lights -> unlit main layers -> Backlight. Main continues Reflection -> MatCap -> Rim -> Glitter -> Emission -> BackfaceColor. Optical composition precedes that latter stack; Gem owns its environment reflection and retains source alpha weighting, while Refraction uses framebuffer alpha one. Required optical differences remain explicit. Independent review caught and corrected Gem tilt suppression and Endfield Fur opposing-normal interpolation during extraction.

### Queue 2460 decision

For the main shader, Transparent repairs 2000/2450 to 3000 and repairs 2460 only when FairyToon previously assigned it for Hair. The FairyToonManagedHairQueue override tag records 1 for automatic Hair2460 and 0 only for an authored 2460 or an already-tagged cleared queue. SetHairQueueTag leaves ordinary untagged materials without an override tag. Untagged legacy materials are recognized only from previous nontransparent coverage and enabled Hair/disabled Mask pass state, before keywords change. Conversion explicitly stamps 0; imported lilToon Transparent2460 stays 2460 even after selecting Hair and repeated Apply. Authored 2461-2466 and other nondefault queues <=2500 stay. Opaque/Cutout repair queues >2500 to 2000/2450; lower authored queues remain. A copied 2000 with a profile setting Transparent ends at 3000. Special Refraction/Gem/Fur rules remain unchanged.

### Current verification

Final results: **94/94 Node checks PASS; 180/180 offline DX11 pass/model/lighting configurations and 380/380 VS/GS/PS stage programs PASS** on the final source and final compiler harness. No failed or skipped Node checks. All used UnityPerMaterial layouts match within each shader across stages/passes/models, and VS/GS/PS interface checks pass. Maximum observed resources: VS 1 texture / 1 sampler; GS 1 / 1; PS 49 textures / 13 samplers (DX11 limits 128 / 16).

| Final matrix | Configurations | Stages | CompileShaders.ps1 options |
|---|---:|---:|---|
| All shaders, all six models, all passes, features enabled | 74 | 160 | default arguments |
| Probe/shadow review, all four Forward shaders | 48 | 96 | -ReviewMatrix -PassNames Forward -Models Toon,EndfieldFace -RefractionBlur Off -Coverage Opaque |
| Feature-off, Gamma, OCT depth, no/main shadows | 58 | 124 | -Models Toon,EndfieldFace -NoLayers -NoNormalMap -NoOutline -NoReflection -SurfaceFeatures None -Coverage Opaque -OctNormals -ColorSpace Gamma -MainLightShadows None,Main |

Node command: node --test Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs Packages/com.nw.fairytoon/Tests~/VerifyOfflineTools.cjs (90 layer/material checks + 4 tooling checks). ReviewMatrix covers Cascade/Screen x Forward/ForwardPlus/ForwardPlusAtlas; transparent shaders retain URP's intentional screen-shadow exclusion. The offline harness now models Unity's per-pass _REFLECTION_PROBE_ATLAS_KEYWORD_DECLARED marker, checks actual CLUSTER_HAS_REFLECTION_PROBES branches, and has an executable installed-URP preprocessing regression. An independent harness review found this omission; both default and lighting matrices were rerun after correction. Earlier exploratory runs are excluded from the totals above.

Red-before-fix checks reproduced 3-argument probes, Endfield FlipNormal=0 backface failure, missing roughness floor, and Transparent2000 remaining2000. Regression tests now execute those contracts plus mixed selection, queue provenance/repeated Apply, all exposed transforms, shared layer order, geometric Face normals, Gem interior tilt and Fur rim-shade interpolation. PowerShell parsing and package diff whitespace checks pass.

Unity import/compile/play, GPU image comparisons, Inspector interaction/Undo in the Editor, screenshots, Vulkan compilation and Unity 6000.6.0f1 Editor validation were not performed. The shader compiler used only installed URP 17.6 includes and Windows D3DCompiler; this is not Unity/Vulkan certification. No claim is made about a new image-difference score. Final cleanup was attempted after all compiler processes exited. Automatic approval rejected the PowerShell Remove-Item operation with only "blocked by policy"; the session cannot request elevation. Read-only verification confirmed the following generated items remain under Tests~ (and the scratch directory is empty):

- review-default.log
- review-lighting.log
- review-default-final.log
- review-lighting-final.log
- review-minimal.log
- review-node-final.log
- OfflineCompiler~/ (empty)

These are temporary verification outputs, not deliverable source files. No other temporary outputs were created for this review.

---

## Historical merge evidence (superseded where noted above)

2026-11-05. Task-only changes to the already merged working tree; no Unity Editor/ShaderCompiler process, Git commit/checkout/reset/pull, asset changes or external-service writes. All writes stayed under Packages/com.nw.fairytoon.

## Changes by file

| File | Change |
|---|---|
| Shaders/FairyToonRefraction.shader | Restore ten missing common properties (FlipNormal, BackfaceColor, third-shadow controls/AO shift, cube-lighting strength); Gamma Metallic/Reflectance attributes. Existing Transparent-100 default retained. |
| Shaders/FairyToonFur.shader | Same common-property/Gamma restoration; body keeps shared Forward, while strand keywords and geometry remain specialized. |
| Shaders/FairyToonGem.shader | Same common-property/Gamma restoration; default queue corrected to Transparent-100, matching installed lts_gem. |
| Shaders/FairyToonVariants.hlsl | Correct both MatCap mask-ST arguments; align view vectors, Refraction facing and Endfield layer normals; apply Refraction BackfaceColor after optics with unit alpha; use shared corrected Gem cube selection/sampling/tint/lighting and model-specific probe paths; add Gamma Gem grazing/reduction; apply Refraction smoothness ST and one optical anisotropy roughness factor. |
| Editor/FairyToonMaterialSetup.cs | Preserve explicitly assigned queues during Apply, including canonical numbers and values crossing 2500. Derive defaults only for raw queue -1. Public mode/Fur subtype transitions migrate canonical defaults but preserve custom ordering. Retain the existing See-through Hair minimum-queue exception to preserve stencil ordering during role edits; special shaders bypass it. |
| Editor/FairyToonConverter.cs | Always retain effective source queue, including implicit lilToon shader defaults; subsequent Apply/validation preserves it. |
| Editor/FairyToonGUI.cs | Route Fur subtype edits through the queue-aware API with multi-edit/Undo; disable the derived Fur alpha-to-coverage control. |
| Tests~/VerifyLayers.cjs | Restore missing final test closure; replace obsolete shader hash freeze with common-property/type/default/Gamma contracts; update the MatCap test ABI; add executable queue transitions and optical normal, transform, color, Gamma and alpha regressions. |
| README.md | Correct queue/converter/Fur coverage/defaults, shared property contract, smoothness transforms and verification claims; document scripted transitions and special paths. |
| Shaders/README.md | Correct shared inputs, optical behavior/defaults and current verification links. |
| Tests~/VariantsReport.md | Mark historical pre-merge evidence and superseded assumptions. |
| Tests~/MergeConsistencyReport.md | This report. |

The merged CompileShaders.ps1 already accepts all four shaders and both lines' options: ColorSpace, NativeHDR, OctNormals, RefractionBlur, explicit package cache, pass/stage keyword filtering, UV layouts, and GS compilation. No further compiler-script change was needed. FairyToonInput and VariantInput already form one unconditional UnityPerMaterial buffer with a per-shader extension; no duplicate buffers or pass-specific material fields were introduced. Main shader/Core/Endfield/Normals/Passes and Fur strand code were preserved from the merged input.

## Regression evidence and superseded rules

Initial VerifyLayers could not parse because the final test lacked its closing brace. After restoring it, the only existing assertion failure was the main shader SHA256 pinned to pre-parity variant development. That freeze is intentionally superseded by the approved parity fixes: functional property/Gamma contracts replace it, rather than pinning a new arbitrary hash.

The original offline Refraction/Toon Forward compile reproduced X3013 at the old 20-argument MatCap call. New common-property and optical-helper tests failed against the stale variant code before fixes. Queue execution reproduced Refraction 2450 -> 3000, Fur TwoPass 2460 -> 3000 and Fur Cutout 2600 -> 2450; current tests retain all three and verify repeated Apply. The old optical roughness test is extended to include routed anisotropy once; the prior no-smoothness-ST limitation is superseded only for Refraction, where shared Input already carries the transform. The MatCap numerical mock now includes maskST in the shared signature. Existing transparency, normal and light-ownership oracles remain intact.

## Deliberate special-path behavior

- Gem uses its source interior normal reversal/tilt once, independently of FlipNormal. Fur strands keep geometric normals before decal coverage; their body uses shared Forward and its parity fixes.
- Refraction applies BackfaceColor after optical layers in framebuffer RGB, with alpha one to avoid inverse-coverage gain. Gem and Fur strands omit that tint, matching their installed lilToon source paths.
- Refraction blur evaluates SmoothnessTex_ST; Gem retains the source bare main-UV smoothness sample. Optical roughness is pre-GSAA and includes routed anisotropy once; Gem grazing still uses smoothness, independently of that roughness factor.
- Ordinary Reflection is absent from Gem's keyword set because its optics own environment reflection. Fur strands retain shadow/rim-shade/fur-rim lighting instead of adding the full body surface stack.

Local references: lil_pass_forward_normal.hlsl (facing, Refraction blur ST, final backface color); lil_pass_forward_gem.hlsl (interior normals, smoothness UV, Gamma dielectric/reduction); lil_pass_forward_fur.hlsl (geometric strand normal); lil_common_macro.hlsl (custom-cube fallback, tint/lighting and single-probe sampling).

## Verification

**85/85 Node tests PASS; 287/287 DX11 pass/model configurations, 626/626 stage programs PASS.** Zero failed/skipped Node tests. Both VerifyLayers.cjs and VerifyOfflineTools.cjs ran. All used UnityPerMaterial layouts matched within each shader across its passes/stages/models; genuinely unused buffers stripped (including Gem PRE). VS/GS/PS signatures and UV/vertex-ID interfaces passed. Per-stage maxima: VS 1 texture / 1 sampler, GS 1 / 1, PS 57 / 12, under DX11 128 / 16 limits. Final package whitespace validation passed.

Reproduce Node checks with `node --test Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs Packages/com.nw.fairytoon/Tests~/VerifyOfflineTools.cjs`. For each row below, run `& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1'` in PowerShell with its options; comma-separated values are PowerShell arrays. The default includes all four shaders and all six models, and Refraction blur off/on. Other rows select all shaders unless specified. The final Fur feature-off rerun covers the review correction that keeps strand normals geometric before decal coverage.

| Matrix | Configurations | Stages | Options |
|---|---:|---:|---|
| default | 74 | 160 | `(default arguments)` |
| off | 34 | 70 | `-Models Toon -NoLayers -NoNormalMap -NoOutline -NoReflection -SurfaceFeatures None -Coverage Opaque` |
| transparent | 43 | 98 | `-Coverage Transparent -OnlyForward` |
| gamma | 15 | 34 | `-Models Toon,Endfield -OnlyForward -ColorSpace Gamma` |
| hdr | 15 | 34 | `-Models Toon,Endfield -OnlyForward -NativeHDR` |
| probes | 7 | 16 | `-Models EndfieldFace -Coverage Transparent -ProbeAtlas -ProbeVolumes -RefractionBlur Off` |
| furprobes | 2 | 6 | `-Shaders Fur -Models Toon -PassNames FurForward,FurSecond -ProbeAtlas -ProbeVolumes` |
| normaluv | 7 | 16 | `-Models Toon -OnlyForward -NoLayers -Normal2ndUV UV1To3 -GlitterUV UV0 -RefractionBlur Off` |
| glitteruv | 7 | 16 | `-Models Toon -OnlyForward -NoLayers -Normal2ndUV UV0 -GlitterUV UV1To3 -RefractionBlur Off` |
| bare | 14 | 28 | `-Models Toon,Endfield -SurfaceFeatures None -NoLayers -PassNames Forward,DepthNormals,SeeThroughHair -RefractionBlur Off` |
| oct | 4 | 8 | `-Models Toon -PassNames DepthNormals -OctNormals` |
| octbare | 4 | 8 | `-Coverage Opaque -Models Toon -PassNames DepthNormals -NoLayers -NoNormalMap -SurfaceFeatures None -OctNormals` |
| emission | 7 | 16 | `-Models Toon -LayerFeatures Emission -SurfaceFeatures None -OnlyForward -RefractionBlur Off` |
| mainlayers | 33 | 68 | `-Models Toon -LayerFeatures Main -SurfaceFeatures Normal2nd -Normal2ndUV UV1To3 -RefractionBlur Off` |
| anisonoreflect | 6 | 14 | `-Models Endfield -SurfaceFeatures Anisotropy -NoReflection -OnlyForward -RefractionBlur Off` |
| glitteronly | 7 | 16 | `-Models Toon -NoLayers -NoNormalMap -SurfaceFeatures Glitter -GlitterUV UV1To3 -OnlyForward -RefractionBlur Off` |
| fur-off-final | 8 | 18 | `-Shaders Fur -Models Toon -NoLayers -NoNormalMap -NoOutline -NoReflection -SurfaceFeatures None -Coverage Opaque` |

The matrix covers Gamma, native HDR, probe atlas/APV, transparent output, both Refraction blur states, normal/glitter mixed UVs, both DepthNormals encodings, stripped features, individual layer groups, and anisotropy without the ordinary Reflection layer. ReportVariants.cjs ran after each successful matrix; these are source variant bounds, not Unity build/stripping counts.

Standalone Editor Roslyn compilation: PASS, exit 0, against cached Unity 6000.6.7f1 references. Command used Unity's Data/DotNetSdk/dotnet.exe with sdk/8.0.318/Roslyn/bincore/csc.dll and a copy of Library/Bee/artifacts/1900b0aE.dag/NW.FairyToon.Editor.rsp. Output/ref/PDB/SARIF/temp paths were relocated under Tests~/EditorCompiler~; eight analyzer/additional-file inputs were excluded. Only the two pre-existing CS0618 warnings remain at FairyToonSeeThroughSetup.cs:78.

## Limits

Not run: Unity import/compilation/play, screenshots, GPU A/B comparison, Frame Debugger, actual SRP Batcher behavior, Vulkan/XR/native-half execution, or the Unity 6000.6.0f1 Editor. DX11 checks use installed URP 17.6/Core headers with UNITY_VERSION=600060; they are offline compilation evidence, not certification of either Unity Editor. Queue/draw changes need visual validation, especially authored transparent queues at or below 2500. Existing optical-copy and Fur auxiliary approximations remain documented in README.

## Temporary outputs

Automatic approval review rejected the path-bounded PowerShell Remove-Item cleanup command with **"blocked by policy"**, before execution. No deletion occurred; no approval escalation was attempted. Task-created paths still present:

- `Tests~/EditorCompiler~/`: `EditorCompiler.sarif`, `NW.FairyToon.Editor.dll`, `NW.FairyToon.Editor.pdb`, `NW.FairyToon.Editor.ref.dll`, `NW.FairyToon.Editor.rsp`, `command.txt`, `compile-final.log`, `compile.log`, `manifest.json`, `temp`.
- `Tests~/OfflineCompiler~/MergeBaseline/`: 48 pre-task source snapshots used only for task-relative diffs.
- `Tests~/OfflineCompiler~/MergeMatrix.ps1`: temporary matrix runner.
- Under `Tests~/`: `Merge-anisonoreflect.log`, `Merge-bare.log`, `Merge-default.log`, `Merge-emission.log`, `Merge-fur-off-final.log`, `Merge-furprobes.log`, `Merge-gamma.log`, `Merge-glitteronly.log`, `Merge-glitteruv.log`, `Merge-hdr.log`, `Merge-mainlayers.log`, `Merge-node-final.log`, `Merge-normaluv.log`, `Merge-oct.log`, `Merge-octbare.log`, `Merge-off.log`, `Merge-probes.log`, `Merge-progress.log`, `Merge-queues.log`, `Merge-red.log`, `Merge-transparent.log`.

Pre-existing audit artifacts were never cleanup targets: `Tests~/OfflineCompiler~/reflection-before/`, `reflection-*.log`, `backface-*.log` and `backface-task.diff`. Source/report files remain separate from these temporary outputs.
