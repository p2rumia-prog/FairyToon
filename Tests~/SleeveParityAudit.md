# Silent Code Sleeve parity audit

> Current behavior: see [independent review corrections](MergeConsistencyReport.md#independent-review-corrections-2026-11-05). Earlier claims that Endfield/DepthNormals obey FlipNormal, Toon uses an unshadowed single probe, or Apply preserves all explicit queues are superseded. Gamma parity and optical/fur special cases remain unless corrected there. Current offline result: 94/94 Node checks and 180 DX11 configurations / 380 stages pass; Unity, Vulkan and visual checks were not run.

2026-11-05, first investigation (historical; superseded by the sc5 reflection follow-up below). Code/data investigation only; no Unity process was started. The reported pre-fix mean absolute RGB difference is 0.0209. Existing images inspected: Logs/Captures/sc3_matdiff/mdiff_base_close.png and mdiff_011_Mat_Sleeve_A_close.png. No post-fix image or new image-difference measurement exists from this task.

## Evidence paths

All paths below are relative to D:/UNITY_PROJECTS/LiminalNW.

- S = Assets/BOOTH/CYCR/Silent Code/Materials&Textures/Materials/A_Black&Purple
- A = S/Mat_Sleeve_A.mat
- B = S/Mat_Sleeve_B.mat
- C = S/Mat_Cloth.mat
- FA = Assets/Lab/Materials/SilentCode/FairyToon/31ffe641135eaec48bca0111059fefdc/Mat_Sleeve_A.mat
- FB = Assets/Lab/Materials/SilentCode/FairyToon/cf29123a6c8969548a22c28ea1960557/Mat_Sleeve_B.mat
- FC = Assets/Lab/Materials/SilentCode/FairyToon/9da87976b875ed14b8535f6fd78b577d/Mat_Cloth.mat
- L = Packages/jp.lilxyzw.liltoon/Shader/Includes
- F = Packages/com.nw.fairytoon/Shaders
- T = Assets/BOOTH/CYCR/Silent Code/Materials&Textures/Textures
- CT = Assets/BOOTH/CYCR/CommonTextures

Source and converted YAML were read and compared in full, including inactive saved texture slots. All original texture references and transforms survive conversion. Every nonempty texture GUID across these materials was resolved (10 unique textures). All transforms relevant to this audit are scale (1,1), offset (0,0).

## Confirmed code differences and repairs

These are proven behavior differences under the material settings. The individual contribution of each to the measured image difference is not established without an isolated render.

| Difference | lilToon evidence | FairyToon evidence and repair | Expected difference for Sleeve A |
|---|---|---|---|
| Custom cube ignored its lighting/tint | L/lil_common_macro.hlsl:1859, URP macro multiplies decoded cube by CubeColor and lerp(1, lightColor, CubeEnableLighting) | F/FairyToonCore.hlsl:516 now applies that multiplier; previously the override branch at baseline :494 only decoded the sample. Input :278 and ShaderLab :251 now expose EnableLighting. | A:543 and FA:652 store EnableLighting=1; CubeColor is white (A:730, FA:868). Old environment E becomes E*lightColor. At lightColor=.05, E=.5 becomes .025 (20x lower); at lightColor=1 there is no change. Actual lightColor at captured pixels is unknown. |
| Backface normal ignored FlipNormal | L/lil_pass_forward_normal.hlsl:300 flips mapped N; :311 deliberately keeps origN unflipped | F/FairyToonPasses.hlsl:133 now calls F/FairyToonNormals.hlsl:6 before anisotropy, layers and shading. Baseline Forward :125-131 had no flip. | Cull=0, FlipNormal=1 (A:326/356, FA:381/459). A backface changes N to -N. MatCap dot products change sign, hence identity-ST UV goes from u to 1-u; diffuse half-Lambert goes from x to 1-x for normal-strength1. Reflection N.V changes too. A has ShadowMainStrength=1, so an incorrect lit/shadow choice can switch between albedo and albedo squared. Frontfaces are unchanged. |
| Third shadow omitted | L/lil_common_frag.hlsl:924, :1013, :1110; actual cutout pass defines LIL_FEATURE_SHADOW_3RD in Shader/ltspass_cutout.shader:657/1143 | F/FairyToonCore.hlsl:246 now blends third shadow after second and before MainStrength. Added third normal/border/blur/receive, blue AO/blur masks and AOShift2; baseline :239 proceeded directly to MainStrength. | A:735 third alpha=.25. At fully second+third shade, linear red coefficient changes from .3800487 to .3091272; green .3733664 to .3041155; blue .3936213 to .3193067. Missing third shade made these coefficients about 23% too bright, before albedo squared/light/final lit mix. This alone cannot explain arbitrarily large brightening (max 4/3 ratio for alpha .25 and nonnegative color). |
| Toon direct specular multiplied diffuse shadowmix unconditionally | L/lil_common_frag.hlsl:1467-1472 passes attenuation 1 outside ForwardAdd/SHADOWS_SCREEN. Installed URP does not alias _MAIN_LIGHT_SHADOWS to SHADOWS_SCREEN. | F/FairyToonCore.hlsl:460-465 uses attenuation1 for Toon, keeping shadowmix for Endfield or SHADOWS_SCREEN. Both isotropic and anisotropic paths use it. Baseline :478-480 always multiplied shadowmix. | Superseded: both Toon and Endfield reflection specular now intentionally use l.shadowmix, including isotropic Toon and GGX, with no attenuation reset. This preserves the toon terminator, Receive Shadow sliders, AO, face map and character self-shadow. |
| MatCap/reflection maps ignored their own ST | L/lil_common_frag.hlsl:1557/1625 use RGB MatCap masks, :1434/1443 use R smoothness/metallic, :1450 uses RGBA reflection color, all LIL_SAMPLE_2D_ST at uvMain | F/FairyToonCore.hlsl:409, :438, :449, :452 and both MatCap call sites now apply the respective ST after uvMain. | No value change for A/B/Cloth because all these STs are identity. This is a latent defect, not the cause of the measured Sleeve difference. |

**Correction from the sc5 follow-up:** this first pass incorrectly treated the saved reflectance .076 as the lilToon GPU F0. lilToon marks it `[Gamma]`, so Linear-project F0 is approximately .00669338. The earlier .06369 normal-incidence environment weight described the buggy FairyToon value; the matching value is .00560950. Mip remapping itself agreed, but filtering did not. See the follow-up for the complete corrected calculation.

## Checked candidates that did not justify speculative changes

- **Cube texture dimensions:** A:231 / FA:265 reference sky1 with fileID8900000. CT/sky1.png.meta:58 is textureShape2, as is CT/T_Matcap_MC (18) 1.jpg.meta:58 for B/C. These are cubemaps imported from PNG/JPG, not Unity Texture2D objects bound to a cube slot. Unity defines Texture2D=1 and TextureCube=2 in its [official TextureImporterShape enum](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/AssetPipeline/TextureImporterEnums.cs). Both shaders declare TEXTURECUBE and a Cube ShaderLab property. No invalid-dimension fallback was added. Actual GPU resource binding was not inspected.
- **RGB versus alpha:** MatCap masks use RGB on both sides; all three AlphaMask images have constant alpha255 and equal RGB channels. The mask information is in RGB. ReflectionColorTex multiplies RGBA on both sides; its alpha is1, so no extra loss or boost from that alpha. Metallic is0 regardless of its shared mask. Smoothness reads R on both sides.
- **sRGB:** All mask importers have sRGBTexture1. Both shaders use ordinary texture sampling and receive the same hardware decoding in this Linear project (ProjectSettings.asset:49). A source value128/255 corresponds to about .21586 linear, not .50196; no additional gamma conversion is appropriate. Import settings were not edited.
- **MatCap normal source:** L/lil_pass_forward_normal.hlsl:314-316 assigns mapped N to both MatCaps; F/FairyToonCore.hlsl:396 returns s.N without anisotropy. A uses normal strengths1, no custom normals, no anisotropy, no UV1 blend. Primary bump scale1 makes the pre-existing unpack-order difference inactive. MatCap view basis, perspective1, roll cancellation1 and ST algebra match L/lil_common_functions.hlsl:550. The applicable difference is the missing facing flip described above.
- **MatCap lighting:** L/lil_common_frag.hlsl:1545/1613 and F/FairyToonCore.hlsl:406 both multiply MatCap RGB by lightColor when EnableLighting1. Blend1, mode Add, colors alpha .366/.5, shadow-mask0 and main-strength0 are retained. No conversion loss of either mask/texture was found.
- **Light floor/diffuse:** L/lil_common_macro.hlsl:2081/2086 and F/FairyToonCore.hlsl:91 clamp lightColor to .05..1. Neither gamma-converts this scalar in the active Linear path. ShadowEnvStrength0 removes the indirect-color blend on both sides. First/second shadow color and MainStrength composition agree; third shadow was the active omission. Shadow LUT mode0 and GSAA0 make their other implementation differences inactive here.
- **Cutout alpha:** L/lil_pass_forward_normal.hlsl:404 and F/FairyToonInput.hlsl:712 use saturate((alpha-.5)/max(fwidth(alpha),.0001)+.5). Cutout is not premultiplied: lil LIL_PREMULTIPLY is empty for LIL_RENDER!=2 (L/lil_common_frag.hlsl:554), and Fairy's transparent blend branches do not execute. MatCap ApplyTransparency only scales alpha for transparent materials on both sides. Tiny discard thresholds differ (zero versus .0001), affecting edges rather than broad RGB. Source AlphaToMask1 becomes0 in FA; under MSAA this can alter edge coverage, not a full interior hex brightness. No render-state/converter changes were made.

## Texture and material inventory

All importers: mipmaps1, alphaUsage1 (this serialization uses alphaUsage, not alphaSource). Only normal is sRGB0/type1; other images are sRGB1/type0. Shape1 means 2D; shape2 means cube. Importer evidence is .meta:9-10 (mips/sRGB), :54-58 (alpha/type/shape); GUID is :2. T textures have alphaIsTransparency1; CT textures have0.

| GUID prefix | File | Main uses | Shape | Source RGB mean (byte values) | Source alpha |
|---|---|---|---|---|---|
| b7050367 | T/A_Black&Purple/Tex_p1.png | MainTex, BaseMap, BaseColorMap | 1 | 52.981,52.969,52.969 | 0..255, mean195.971 |
| 2f4fe86c | T/X_Other/Tex_P1_Nomal.png | BumpMap | 1 | 127.395,127.457,248.660 | 255 |
| 468dac8b | T/X_Other/Tex_P1_AlphaMask2.png | A/B MatCap1; inactive emission masks | 1 | 16.056 each | 255 |
| 73526a43 | T/X_Other/Tex_P1_AlphaMask3.png | A/B MatCap2; C MatCap1 | 1 | 32.077 each | 255 |
| e671d724 | CT/matcap_20260521_153840.png | A/B MatCap1 | 1 | 33.756 each | 255 |
| d1b9ee4c | CT/matcap_20250915_154001.png | A/B MatCap2; C MatCap1 | 1 | 78.153 each | 255 |
| 88ef977b | T/X_Other/Tex_P1_AlphaMask1.png | Smoothness, metallic, reflection color | 1 | 207.280 each | 255 |
| 3cf6d40f | T/X_Other/Tex_P1_AO.png | ShadowBorderMask | 1 | 228.732,228.731,228.732 | 255 |
| 3a8e1ef6 | CT/sky1.png | A custom cube | 2 | 173.066,172.435,171.573 | 255 |
| 040a1cfb | CT/T_Matcap_MC (18) 1.jpg | B/C custom cube | 2 | 84.533,83.286,81.516 | 255 |

Statistics describe original image pixels, not Unity's imported/compressed/GPU mip data. Mask2 is mostly black (90.359%) with hex lines; Mask3 is mostly black (86.233%) with trim/print. Main alpha is zero on22.106%, full on49.474%, intermediate on28.420%. T images are4096?; MatCaps are1024?/512?; sky1 is2048x1024 and the other cube source512?. Cube importers use generateCubemap6, convolution0 and seamless0 (.meta:30-32). Their actual imported mips/prefiltering were not inspected. This is shared input data, not justification to alter those assets.

| Setting | A | B | Cloth |
|---|---|---|---|
| Source shader | Hidden/lilToonCutout | Hidden/lilToonOnePassTransparent | opaque lilToon |
| Queue | 2463 | 2464 | 2462 |
| Cull / FlipNormal | 0 / 1 | 0 / 1 | 0 / 1 |
| Smoothness / Reflectance | .337 / .076 | .157 / .198 | .274 / .159 |
| ShadowMainStrength | 1 | 0 | .296 |
| Shadow2 alpha / Shadow3 alpha | 1 / .25 | 0 / 0 | 1 / 1 |
| MatCap1 alpha / MatCap2 enabled | .366 / 1 | .52 / 1 | .147 / 0 |
| Cutoff | .5 | .1 | .1 |

A and B share all active 2D images, but have different cubes, shadow settings and coverage. B's low measured difference does not isolate one code path. Source-to-target existing-property changes: A AlphaToMask1->0, DstBlendAlpha10->0, TransparentMode0->1; B SrcBlend1->5 and TransparentMode0->2; C DstBlendAlpha10->0. Other original scalar/color/texture values are retained. These render-state changes were inspected but not changed in this task.

> Historical result below: the Endfield normal/depth exemption is superseded by the [2026-11-05 backface follow-up](#endfield-backface-follow-up-2026-11-05).

## Change boundaries

- FairyToon.shader / FairyToonInput.hlsl: lilToon-named inputs, third-shadow texture, five STs; no new shader keywords or sampler states.
- FairyToonCore.hlsl: cube lighting/tint, third shadow, ST sampling, toon specular attenuation.
- FairyToonNormals.hlsl / FairyToonPasses.hlsl: mapped facing correction, consistent depth normals; geometric origN preserved.
- FairyToonEndfield.hlsl: compensate its existing visible-side flip so the new shared flip cannot flip twice. Endfield diffuse and DepthNormals retain their previous direction for either FlipNormal value; the face origN branch remains unchanged. Endfield's own shading is otherwise unchanged. Shared optional MatCap/reflection/anisotropy layers can change where the same missing facing/cube/ST behavior applied.
- FairyToonGUI.cs: advanced controls for the newly supported options.
- README.md / Tests~/VerifyLayers.cjs / this audit: compatibility notes, mappings and regressions.

Default opaque frontfaces retain the old arithmetic: FlipNormal0, shadow3 alpha0, identity STs, Reflection disabled. Toon materials with FlipNormal0 now also have unflipped backface DepthNormals, matching their forward normals; this can change their SSAO. Endfield's prior depth behavior is explicitly preserved using the existing ShadingModel constant. No assets/importers, Runtime code or other packages were modified. Converter.cs and MaterialSetup.cs had pre-existing changes; their bytes are unchanged by this task, including the protected queue block.

## Validation and remaining uncertainty

Final results: **31/31 Node checks pass; 49/49 offline DX11 VS/PS pairs (98 entry-point compilations) pass** on the final shader code. Constant-buffer layouts match wherever used; genuinely unused buffers strip. Maximum observed resource use: 10 PS samplers / 1 VS sampler, below the DX11 limit16. Independent diff review found and verified the fix for an Endfield double-flip regression; no unresolved blocking finding remains.

Commands run from the project root (PowerShell):

~~~powershell
node Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Cutout
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Opaque -NoLayers -NoNormalMap -NoReflection -SurfaceFeatures None
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Transparent -Models Toon,Endfield -PassNames Forward,SeeThroughHair
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Cutout -Models Toon -SurfaceFeatures None -NoLayers -PassNames Forward,DepthNormals,SeeThroughHair
~~~

Pair counts respectively:21 all-feature Cutout;21 reduced Opaque;4 Transparent;3 primary-normal-only Cutout. The first two matrices cover all six shading-model options (11 Toon passes plus Forward/SeeThroughHair for five Endfield options). The final small matrix covers the Sleeve's primary-normal/MatCap/reflection path without the optional second-normal/anisotropy/layer features. No compiler script change was required. New regression checks failed before the corresponding fixes and passed afterwards. Git diff whitespace validation passed. The Node tests execute scalar channels of the actual changed HLSL arithmetic and assert reference-source wiring; they are not GPU render tests. D3DCompiler checks syntax, stage/resource limits and material-buffer layouts against cached URP sources, not Unity shader import or runtime binding.

Not run: Unity editor/import/Play Mode or Editor C# compilation, a 6000.6.0f1 Editor compile, new A/B screenshots, per-layer ablation, runtime resource capture, rendered SRP Batcher validation. Consequently the new mean RGB error and exact screen-space contribution/facing/lightColor of the failing skirt pixels remain unknown. Existing screenshots are pre-fix evidence only.

## sc5 reflection follow-up — 2026-11-05

This section supersedes the first investigation's reflection conclusion. Scope: package shaders, README and Tests~ only. No Unity, asset/importer edits, converter edits, commits, checkout or reset. Existing uncommitted fixes were retained. Line numbers in this section refer to the resulting files; `before` means the working tree at the start of this follow-up, not Git HEAD.

### Evidence and active cause

The user supplied the same-property-on-both-materials waist ablation scores (whole-frame mean absolute RGB difference):

| Ablation | MAE |
|---|---:|
| Base | 0.0766 |
| MatCap / MatCap2nd / both off | 0.0763 / 0.0768 / 0.0775 |
| Reflection off | 0.0325 |
| ApplyReflection / ApplySpecular off | 0.0512 / 0.0702 |
| BumpMap off | 0.0776 |
| Cull back / Shadow off | 0.0517 / 0.0560 |
| Rim off | 0.0763 |
| MatCaps + Reflection off / flat | 0.0338 / 0.0330 |

Reflection-off removes 57.6% of the reported frame gap. These ablations are not additive and do not measure the effect of the new patch. Inspected and displayed the existing `Logs/Captures/sc5_ablation/abl_base_liltoon.png` and `abl_base_fairytoon.png`: the broad skirt region is much brighter in FairyToon. Other ablation scores above are user measurements, not reruns. No after-patch images exist from this task.

**The main active code defect is the missing `[Gamma]` attribute on `_Reflectance`.** lilToon `CustomShaderResources/Properties/Default.lilblock:226`, and the actual source `Shader/lts_cutout.shader:230`, mark the scalar Gamma; FairyToon.shader:237 previously did not. `ProjectSettings/ProjectSettings.asset:49` selects Linear. Source A:541 stores .07599995; FA:649 stores .076. Their saved values agree, but shader uploads did not. The local lilToon optimizer independently confirms Gamma conversion (`Editor/lilOptimizer.cs:257`, :296, :306). Unity documents float/vector Gamma uploads in [Add material properties](https://docs.unity.com/en-us/engine/6000.3/manual/materials-and-shaders/shaders/writing-custom/shader-writing/writing-shader-change-properties/sl-properties-in-programs).

For a saved .076, standard sRGB-to-linear gives **.006693375758**, versus the old FairyToon **.076**, a **11.3545x** F0 error. Adding `[Gamma]` lets Unity do the upload conversion exactly once; no material rewrite, converter correction or HLSL double conversion is needed. `_Metallic` also lacked `[Gamma]`; that was fixed for other materials, but A's metallic is zero.

The second active difference is cube filtering: lilToon's `lil_sampler_linear_repeat` aliases **trilinear** (`L/lil_common_input.hlsl:18`), whereas FairyToon used the cube's imported **bilinear** sampler. Both sky1 and T_Matcap_MC importers have filterMode1 at :37. Mip arithmetic was already equivalent, but sampling fractional mip 4.9164102 was not. Its brightness effect depends on the actual mip texels; it cannot be assigned a measured percentage offline.

### Exact material reflection walk

A:293–295 enables environment and direct specular; :543 selects Add; :544–546 enables cube lighting, override and normal strength1. A:325 has BumpScale1, :327 Cull0, :357 FlipNormal1, :358 GSAA0, :464 Metallic0, :592 Smoothness.337, :597 specular normal strength1, :598 non-toon specular. Reflection and cube colors are white (:730–731). Anisotropy and second normal are off. The shared mask GUID88ef977b is used for smoothness, metallic and reflection RGBA, with identity ST. A uses cube sky1; B/C use the cube-imported T_Matcap_MC JPG. These are valid cube imports, not 2D textures bound to a cube property.

| Step | lilToon source | FairyToon source after fix | Actual operation / effect for A |
|---|---|---|---|
| View and normal frame | L/lil_common_macro.hlsl:2317, :2324; L/lil_pass_forward_normal.hlsl:299 | F/FairyToonPasses.hlsl:123, :134 | V is normalized camera-position minus surface-position for Toon. TBN uses the same tangent, geometric normal and handedness. Mapped N is normalized after TBN. |
| Backface and origN | L/lil_pass_forward_normal.hlsl:300, :307, :311, :314 | F/FairyToonNormals.hlsl:6; F/FairyToonPasses.hlsl:130, :138 | Cull0 draws both sides; FlipNormal1 negates mapped N on backfaces. origN remains unflipped. Reflection starts with mapped N. Fresnel uses saturate(mapped N dot V). N -> -N leaves the reflected ray unchanged but can switch Fresnel from F0 towards grazing. No second face flip occurs inside reflection. |
| Hex normal decode | L/lil_common_functions.hlsl:166; L/lil_pass_forward_normal.hlsl:299 | F/FairyToonNormals.hlsl:28, :57; F/FairyToonPasses.hlsl:135 | Tex_P1_Nomal feeds mapped N and therefore the reflection ray and Fresnel. BC5/DXT5nm use A*R for X, G for Y; XY are scaled before reconstructing Z. At BumpScale1 the previous URP unpack order was already numerically equivalent. No channel-swizzle defect explains the active hex brightness. |
| Smoothness / roughness | L/lil_common_frag.hlsl:1432–1438; L/lil_common_functions.hlsl:1115 | F/FairyToonCore.hlsl:436, :491–497 | s=.337*m (R of the sRGB-sampled smoothness texture), GSAA0 leaves s unchanged. p=1-s; r=p*p. No material-independent .002 floor on environment/toon roughness. Anisotropy is disabled; its existing roughness factor remains available. |
| Metallic / diffuse energy | L/lil_common_frag.hlsl:1441–1446 | F/FairyToonCore.hlsl:499–501; F/FairyToon.shader:235, :237 | metallic=GammaUpload(0)*mask.r=0; col -= metallic*col has zero effect. F0=lerp(GammaUpload(.076),albedo,0)=.00669338. It does not multiply the dielectric cube reflection by main color. |
| Reflection color mask | L/lil_common_frag.hlsl:1448–1453 | F/FairyToonCore.hlsl:502, :557, :597 | White _ReflectionColor times sampled RGBA. RGB mask is already decoded by texture sampling; no second decode. Mask alpha is1. A is cutout, so ReflectionApplyTransparency does not apply another surface-alpha factor. |
| Direct specular | L/lil_common_frag.hlsl:1318–1418, :1460–1474 | F/FairyToonCore.hlsl:446, :506–557 | N=lerp(origN,mappedN,1), H=normalize(V+L). Same GGX visibility and Fresnel F0+(1-F0)*(1-L.H)^5; the missing GGX denominator epsilon is restored. Direct specular multiplies reflection RGB and toon lightColor once. Current FairyToon intentionally uses l.shadowmix in both models, superseding the earlier lilToon attenuation parity rule. |
| Environment N / ray | L/lil_common_frag.hlsl:1480–1485; L/lil_common_functions.hlsl:1157 | F/FairyToonCore.hlsl:561–567 | N=lerp(origN,reflectionN,1); no extra normalization of the lerp. R=reflect(-V,N). N-strength fixes are inactive for A because strength=1 and anisotropy is off. |
| Cube selection / mip / decode | L/lil_common_macro.hlsl:1859; L/lil_common_functions.hlsl:380, :1154; L/lil_common_input.hlsl:18 | F/FairyToonCore.hlsl:458–486, :569–581; F/FairyToonInput.hlsl:462 | Override1 selects the custom cube. mip=p*(10.2-4.2p). Sample trilinearly, then lil-style HDR decode. PNG/JPG original alpha is1; no evidence that decode branch differences are active for these cubes. Imported HDR constants and GPU texels remain uninspected. |
| Cube light / tint | L/lil_common_macro.hlsl:1861 | F/FairyToonCore.hlsl:572 | Decoded cube times white CubeColor times lerp(1,lightColor,1). Exactly one lightColor factor for custom environment; no extra shadowmix/attenuation or main-color darkening. Earlier repair retained. |
| Grazing and reduction | L/lil_common_frag.hlsl:1487–1500; L/lil_common_functions.hlsl:1139 | F/FairyToonCore.hlsl:583–596 | In Linear, oneMinusReflectivity=.96*(1-metallic), F90=s+.04 (saturated), reduction=1/(1+r*r). Environment Fresnel uses mapped N.V even if reflection normal strength changes. |
| Add / layer order | L/lil_common_functions.hlsl:325–334; L/lil_common_frag.hlsl:1474, :1502; L/lil_pass_forward_normal.hlsl:525, :532 | F/FairyToonInput.hlsl:588–599; F/FairyToonCore.hlsl:332–344, :557, :597 | Add yields col + reflectionColor.rgb*reflectCol*reflectionColor.a. Direct addition occurs before environment; MatCaps occur after Reflection in both paths. MatCaps and their masks were not altered. |

At m=1: s=.337, p=.663, r=.439569, mip=4.9164102, reduction=.8380677837, F90=.377. The following weights multiply decoded cube, CubeColor, cube lighting and reflection mask:

| mapped N.V | Before (wrong F0=.076) | Fixed / lilToon (F0=.00669338) |
|---:|---:|---:|
| 1 | .06369315 | .00560950 |
| .75 | .06393950 | .00591257 |
| .5 | .07157623 | .01530769 |
| .25 | .12355525 | .07925513 |
| 0 | .31595155 | .31595155 |

Thus the largest reduction is broad non-grazing fabric; the exact grazing endpoint is unchanged. The normal map can make hex edges sample different directions and N.V, but it was reflecting an excessively high dielectric baseline. This agrees with reflection-off and bump-off ablations without attributing the defect to the normal map. Exact postprocess/frame MAE cannot be predicted from these local linear weights.

### Other differences repaired, including inactive cases

| Difference | lilToon source | FairyToon before -> after | Expected numerical effect |
|---|---|---|---|
| Metallic Gamma flag | Default.lilblock:223; lts_cutout.shader:227 | FairyToon.shader:235 now [Gamma] | Saved .5 in Linear uploads .214041 instead of .5; with white mask diffuse retention changes .5 -> .785959. Zero effect on A's metallic0. |
| GSAA derivative selection | L/lil_common_functions.hlsl:1117–1128 | Core before:440–442 -> :436–443 | With dx=(.02,0,0), dy=(0,.02,0), strength1 and s=.99, old s=.866667; corrected s=.9. At GSAA0, no effect. |
| GGX floor / epsilon | L/lil_common_frag.hlsl:1334, :1393–1405 | Core before:447, :487–493 -> :446–455, :497, :545 | Floor .002 only inside GGX. At s=.99, toon exponent becomes10000 instead of500. At GGX r=.002, nh=1, denominator epsilon lowers the old singular peak by6251x; at A's m=1, relative change is below3e-6. Environment floor removal changes reduction by at most about4e-6. |
| Reflection normal interpolation | L/lil_common_frag.hlsl:1482 | Core before:509 -> :566 | For orig=(0,0,1), mapped=(.6,0,.8), V=(0,0,1), strength.5: ray changes (.6,0,.8) -> (.54,0,.62). Cubemap direction changes; sign of image effect depends on the cube. Inactive at A's strength1. |
| Fresnel N source | L/lil_pass_forward_normal.hlsl:307; L/lil_common_frag.hlsl:1500 | Core before:525–528 -> :595 | Same example: old normalized-lerp fifth-power factor .0000003559, correct mapped-N factor .00032. Also fixed when anisotropy/strength changes; inactive for A's strength1. |
| Primary normal strength | L/lil_common_functions.hlsl:166–178 | Normals before:51–54 -> :50–58 | With decoded XY=(.6,0), scale2: normalized old N=(.83205,0,.55470), lil-compatible N=(1,0,0). Toon now uses the correct decoder even without a second map. Endfield keeps its authored decoder selection using _ShadingModel, including DepthNormals. At A's scale1, no change. |
| Cube mip filter | L/lil_common_functions.hlsl:1158; L/lil_common_input.hlsl:18 | Core before:515 -> :470–474; Input before:446 -> :446, :462 | At mip4.9164102, replace nearest-mip bilinear sampling with .0835898*M4 + .9164102*M5. Difference is .0835898*(M4-M5) if old selection was M5. Active on current bilinear cube assets; actual texel difference unknown. |
| HDR decoding branches | L/lil_common_functions.hlsl:380–390 | Core before:515 -> :458–468; Endfield before:542 -> :541 | For RGB=.5, alpha=.25, HDR=(2,2.2,0,1), Gamma returns .25, native HDR returns1, encoded Linear returns .0473661. Old unconditional SRP decode returned .0473661 in all branches. No expected difference when effective alpha1. |
| Probe fallback and overload | L/lil_common_macro.hlsl:359–363, :1859–1862 | Core before:512, :520 -> :477–486, :569–581 | Toon now falls back when width<15/HDR.x0, and otherwise uses the same single-probe overload as lilToon. Result can change by an arbitrary probe/cube difference. A override1 makes this inactive. Endfield explicitly retains atlas-capable position-aware selection; an empty legacy probe is not evidence of an empty atlas. |
| Gamma BRDF branches | L/lil_common_frag.hlsl:1409–1412, :1489–1493; L/lil_common_macro.hlsl:1938–1941 | Core before:493, :522–524 -> :522–525, :546–549, :583–592 | Gamma specular takes sqrt(max(.0001,term)); Gamma reduction at p=.663 is .91839841 rather than .83806778; F90 at m=1 becomes .557916301 rather than .377. Linear project A is unaffected by these branches. |
| Toon specular attenuation | L/lil_common_frag.hlsl:1334, :1467–1472 | Core before:480, :496–498 -> :533–537, :553–555 | Superseded: both Toon and Endfield reflection specular now intentionally use l.shadowmix, including isotropic Toon and GGX, with no attenuation reset. This preserves the toon terminator, Receive Shadow sliders, AO, face map and character self-shadow. |
| Orthographic V | L/lil_common_macro.hlsl:616–618, :2317 | Passes before:123 -> :123–128 | Toon uses position-to-camera V even in orthographic views, matching lilToon. Example off-axis point (1,0,0), camera(0,0,10): V=(-.099504,0,.995037) instead of (0,0,1). Perspective behavior matches; Endfield retains URP's orthographic V. Capture projection was not inspected at runtime. |

Normal importer evidence: `T/X_Other/Tex_P1_Nomal.png.meta:10` is sRGB0, :57 normal-map type, :87 format12 but :92 override0. This does **not** establish the actual GPU BC5/DXT5nm format. Both encodings use the same A*R decoder, so no speculative importer or channel change was made.

### Per-file changes and compatibility

- `Shaders/FairyToon.shader`: `[Gamma]` on Reflectance and Metallic; all saved defaults remain unchanged.
- `Shaders/FairyToonCore.hlsl`: exact GSAA, GGX denominator/floor placement, cube sampling/decode/fallback, Fresnel N and Gamma branches; toon specular attenuation ordering. Existing metallic darkening, RGBA/ST sampling, tint/light multiplication and Add blending retained.
- `Shaders/FairyToonInput.hlsl`: replace the unused cube-asset sampler with an inline trilinear-repeat sampler; declared sampler count stays5. No constant-buffer layout changes from this follow-up.
- `Shaders/FairyToonNormals.hlsl`: correct Toon primary strength; preserve Endfield's existing primary-only and second-normal routing with the same selector in Forward/DepthNormals.
- `Shaders/FairyToonPasses.hlsl`: lil-compatible Toon V; Endfield V and existing facing behavior retained.
- `Shaders/FairyToonEndfield.hlsl`: native custom-cube branch reuses shared sampling/decode; retains ambient multiplier, environment BRDF, AO, specular ownership and probe selection. The optional shared Reflection layer receives corrected Gamma F0, cube filtering and mathematical fixes. Endfield's separate EFMetallic/EFSmoothness controls are unchanged.
- `Tests~/VerifyLayers.cjs`: nine new regression groups, including upload semantics, numerical source arithmetic, decoder modes, normal/facing wiring and Endfield atlas/normal guards; adjusted existing source contracts for the refactored cube selector and attenuation order.
- `Tests~/CompileShaders.ps1`: add ColorSpace/NativeHDR switches for offline branch coverage.
- `README.md`, this audit: conversion semantics, cross-model behavior and corrected evidence.

Other Toon materials using nonzero reflectance/metallic receive the same intended upload correction. White/default scalar endpoints remain stable; existing authored specular brightness can change, especially with low dielectric F0. Endfield retains its authored primary normal strength (e.g. underwear BumpScale2) and its independent shading model, while shared Reflection and custom-cube sampling receive the fixes. Visual preservation of every material is not claimed.

### Remaining uncertainties and validation boundary

- No Unity import/compilation/play/capture was run, on either 6000.6.7f1 or 6000.6.0f1. No new MAE is available; the 11.35x result is an analytical F0 comparison, not a whole-image improvement claim.
- ShaderLab Gamma semantics are checked against local source plus Unity documentation, but actual per-draw GPU constant uploads, texture formats/mips/HDR vectors, inline-sampler binding and pixel history require Unity/GPU inspection.
- Both shaders multiply their computed toon lightColor into the custom cube, but their upstream lighting is not identical: FairyToonCore.hlsl:99/281 deliberately uses its own additional-light steps/SSAO, whereas lil_common_macro.hlsl:2135/2307 and lil_pass_forward_normal.hlsl:457 use lilToon's lighting accumulation. No scene-light rewrite was attempted. The actual lightColor at the measured pixels is unknown.
- The supplied reflection-off and flat scores retain about .033 frame MAE across **all** stand materials. This patch does not claim to eliminate that residual. Existing shadow/coverage differences and the converter render queue remain outside these reflection edits.
- Source review found and corrected Gamma dielectric and Endfield probe-atlas regressions before completion. The converter SHA256 remains `4831a9cb278899eb5559138dba747f40f3aa0b55362abdec983a1f320ae2da16`; its render-queue block was untouched.

### Final offline results

**40/40 Node regression checks pass. 58/58 DX11 vertex/pixel pairs compile (116 entry-point compilations).** Final logs are under ignored `Tests~/OfflineCompiler~/reflection-*.log`. Maximum observed resource use: VS1 sampler, PS10 samplers; used material-buffer layouts agree across the matrix, and truly unused buffers strip. `git diff --check -- Packages/com.nw.fairytoon` passes.

Commands run on the final implementation (from project root):

```powershell
node Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Cutout
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Opaque -NoLayers -NoNormalMap -NoReflection -SurfaceFeatures None
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Transparent -Models Toon,Endfield -PassNames Forward,SeeThroughHair
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Cutout -Models Toon,Endfield -PassNames Forward -ColorSpace Gamma
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Cutout -Models Toon,Endfield -PassNames Forward -NativeHDR
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Cutout -Models Toon,Endfield -PassNames Forward -ProbeAtlas
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Cutout -Models Toon,Endfield -SurfaceFeatures None -NoLayers -PassNames Forward,DepthNormals,SeeThroughHair
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Opaque -Models Toon -NoLayers -NoNormalMap -SurfaceFeatures None -PassNames Forward
```

Pair counts are 21+21+4+2+2+2+5+1. The compiler uses Windows D3DCompiler, the cached URP headers and UNITY_VERSION600060; this is not Unity shader import and does not prove either editor version's runtime behavior. New regressions were observed failing before their fixes. Existing regression contracts were retained except for source-wiring updates necessitated by the helper extraction. An intermediate run caught two sampler-count assertions; replacing the now-unused asset sampler with the inline sampler restored the existing budget.


## Endfield backface follow-up (2026-11-05)

Requested capture case: Silent Code Mat_Hat, Cloth, double-sided with FlipNormal=1, BackfaceForceShadow=0 and BackfaceColor=(0,0,0,0); peach inside faces and a brown patch on the far hood wall. No Unity process or capture was run for this follow-up.

### Evidence and scope

The pre-task working tree differs from the proposed diagnosis: FairyToonEndfieldFacingNormal left the already-flipped s.N alone at FlipNormal=1. Cloth diffuse, NoL, view light, rims, specular, environment and character-shadow lookups already used that flipped N. It forced a flip only when FlipNormal=0. Thus this patch does **not** establish that reversing Cloth diffuse alone removes the captured tint. Remaining bypasses were shared lighting blends using unflipped origN, the Face geometric/SDF/sphere paths, and the Hair sphere band. Shared layer changes may affect this material depending on its enabled features and normal strengths; the capture's actual shader variant/runtime bindings were not inspected.

Read-only material check: both saved Endfield Mat_Hat variants under Assets/Lab/Materials/SilentCode/Endfield/13491baeafb06c041867ae5917eee4f8 and /352c6bbcf96c234499a6a415293342fb are Cloth (EFPart 0), Cull 0, FlipNormal 1, ForceShadow 0 and BackfaceColor alpha 0. Their active keywords are Endfield, MatCap, Reflection and primary normal; Shadow/MatCap/Specular/Reflection normal strengths are all 1, MatCap custom normal is off, and Rim/RimShade/Backlight/Glitter are off. These saved inputs therefore already resolve the active lighting to mapped N in the pre-task code; no large tint correction is predicted for them solely from this patch. Stored EFSSSStrength/EFSSSColor are inactive in Cloth: the SSS block compiles only for Skin/Face. Which saved material and compiled variant produced the capture is still unverified.

Local reference: lil_pass_forward_normal.hlsl applies FlipNormal after mapped normal construction (around lines 299-315), leaves geometric origN alone, and applies BackfaceColor after emission as lerp(finalRGB, BackfaceColor.rgb * lightColor, BackfaceColor.a) (around lines 583-589). lil_common_frag.hlsl scales lit/border weights by 1-BackfaceForceShadow (around lines 1038-1047 and 1156-1162). Endfield is an artistic lighting model, so its corresponding lit/view/deeper-tone weights are used; force shadow is not a final RGB blackout.

### Per-term behavior

| Term | Flipped Endfield backface behavior |
|---|---|
| Shared surface normal | Forward/SeeThroughHair still flip mapped N once. Inside the Endfield shading copy only, origN=N before Rim Shade, so normal-strength blends cannot reintroduce the outward geometric normal. Layer/decal UV geometry was computed earlier and stays intact. Toon retains lilToon's geometric origN. |
| Diffuse NoL, ramps, terminator lift | Use s.N directly; no second facing helper. On Face backs, use the mesh NoL and mesh blend 1 instead of the head SDF. Front Face SDF is unchanged. |
| View light, dark/deeper tones, SSS | N.V and view-light N.L use the same selected normal. Skin/Face SSS and Hair halo therefore no longer get an unrelated back-facing normal. The intentionally warm ramp/SSS colors themselves are not changed. |
| Character shadow | Lookup/fade receives N. Flipped Face backs receive it as mesh regions; front SDF regions retain their original exemption. Caster geometry/bias is unchanged. |
| Ambient/top fill | Both use the selected N, including directional ambient and top-light N.y. |
| Direct/environment specular | GGX N.H/N.V, Fresnel, reflection ray and environment BRDF use N. Shared Reflection and anisotropy receive the same mapped-normal lighting basis; geometric tangent setup is unchanged. |
| Rim/depth edge | Fresnel, light-side rim, Face side mask and screen-space depth-edge offset use N. |
| Additional/small lights | Shared additional-light normal blends resolve to N on flipped backs. Face lamps override the front head-sphere normal with N. Character fill/tint/highlight/rim lights receive N; Fog role has no normal dependency. |
| Hair band | Flipped backs replace the head-sphere input with N before flattening, strand direction, both shifted lobes and band-ramp coordinate. The front head-sphere band is unchanged. |
| Shared lilToon layers | Rim Shade, Backlight, Reflection, both MatCaps, Rim and Glitter cannot mix the flipped normal with an outward geometric normal in Endfield lighting. Main-layer UV construction is unchanged. |
| Force shadow | Existing lit weight keeps its single post-ramp multiplier. Eye lit override, camera-light weight and deeper-tone ramp alpha now also receive the backface factor. Strength 0 is neutral; 1 selects the dark diffuse weights. Independent additive lights, highlights/environment and emission are not forcibly extinguished. |
| Backface color | Added in both shading models after emission, before fog. RGB blends toward color times the model's light color; alpha is the blend amount, not coverage. Color alpha 0 and all front faces return the original RGB directly. Transparent coverage uses the existing bounded premultiplied-to-straight conversion. |
| DepthNormals | Exactly one shared facing correction after mapped-normal construction, for either model. FlipNormal=0 now leaves Endfield backs unflipped as well. Raw output is a signed world-space unit normal; OCT uses PackNormalOctQuadEncode -> remap -> PackFloat2To888. |

URP reference inspected: installed 17.6.0 LitDepthNormalsPass.hlsl lines 128-151 and DeclareNormalsTexture.hlsl lines 20-28. The lilToon reference's OctRect packing was **not** copied: it is not the format this URP decoder expects. Primary-only Endfield normal decoding still uses the existing ShadingModel selector in both Forward and DepthNormals.

### Files changed in this follow-up

- Shaders/FairyToonNormals.hlsl: remove the unconditional Endfield backface fallback helper.
- Shaders/FairyToonPasses.hlsl: remove the second Endfield-only DepthNormals correction; keep both URP encoding branches.
- Shaders/FairyToonEndfield.hlsl: unify N consumers, handle Face/Hair alternative normals, and close force-shadow view/eye/deeper-tone bypasses.
- Shaders/FairyToonCore.hlsl: Endfield-only flipped-back lighting copy; shared final BackfaceColor blend after emission.
- Shaders/FairyToon.shader: lilToon-named HDR BackfaceColor property, default (0,0,0,0).
- Shaders/FairyToonInput.hlsl: unconditional BackfaceColor constant; no texture, sampler or keyword added.
- Editor/FairyToonGUI.cs: expose BackfaceColor beside FlipNormal in Advanced Base settings.
- Tests~/VerifyLayers.cjs: replace the obsolete Endfield exemption with five regression groups for facing/depth, all-term routing and front guards, force-shadow bypasses, and color order/default/alpha semantics (including transparent premultiplied equivalence).
- Tests~/CompileShaders.ps1: add -OctNormals for explicit offline OCT DepthNormals compilation.
- README.md and this audit: current behavior, source discrepancy, per-term changes and validation limits.

FairyToonConverter.cs, its render-queue block and MaterialSetup.cs were not edited. Existing source values copy by name on future conversions because BackfaceColor is now a supported destination property. Already converted nonzero BackfaceColor values may need verification if a previous conversion discarded the unsupported property; no materials were modified here.

### Validation and remaining uncertainty

The old suite passed 40/40 before changes. The four new/replacement regression groups then failed for the expected missing behavior (39 passed, 4 failed). They pass after the shader changes. A fifth group then exposed the initial straight-RGB BackfaceColor blend as incompatible with lilToon transparent premultiplication (43 passed, 1 expected failure); it now passes using the existing transparent blend helper. Numerical checks execute extracted HLSL helper/guard arithmetic; source contracts cover the normal-routing sites. These are not render tests.

Front faces (including ordinary Cull Back single-sided materials) retain their original arithmetic: all new lighting substitutions are conditional on a flipped backface, force-shadow additions are back-only, and the color helper returns front RGB unchanged. Face front geometric/SDF lighting and Hair front sphere construction remain intact. Byte-identical GPU output has **not** been measured. Cull Front deliberately draws backfaces and therefore receives the requested backface behavior.

Transparent BackfaceColor matches lilToon post-premultiply blending for surface alpha >= 1/4096 when the output fits native half. Below that floor its inverse-alpha gain is bounded; at zero coverage finite SrcAlpha RGB cannot reproduce nonzero premultiplied light. The existing +/-65504 clamp can limit extreme HDR. This is a known straight-alpha representation limit, not exact parity at those endpoints.

No Unity Editor import/compile/play, Unity 6000.6.0f1 run, SRP Batcher runtime check, GPU image comparison, SSAO/depth-rim capture or screenshot was performed. The peach/brown hood symptom remains visually unverified. Warm authored ramps/SSS, light/probe colors, texture sampling, character shadows and SSAO may still contribute; this task does not retune those inputs. FlipNormal=0 Endfield backfaces intentionally change orientation and may change SSAO. No assets, Runtime code, other packages or converter render queues were modified.


Final follow-up results: **44/44 Node checks and 61/61 offline DX11 VS/PS pairs (122 entry-point compilations) pass on the final shader code**, including the corrected transparent BackfaceColor blend. Used UnityPerMaterial layouts match across each compile matrix; genuinely unused buffers strip. Maximum observed samplers: VS 1 / PS 10 (DX11 limit 16). Independent source review found no outstanding blocking issue. Converter SHA256 remains 4831a9cb278899eb5559138dba747f40f3aa0b55362abdec983a1f320ae2da16. Whitespace validation passes.

Reproduction commands from the project root (PowerShell; no Unity executable):

~~~powershell
node Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Cutout
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Opaque -NoLayers -NoNormalMap -NoReflection -SurfaceFeatures None
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Transparent -PassNames Forward,SeeThroughHair
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Cutout -Models Toon,Endfield -SurfaceFeatures None -NoLayers -PassNames Forward,DepthNormals,SeeThroughHair
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Cutout -Models Toon -PassNames DepthNormals -OctNormals
& 'Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1' -Coverage Opaque -Models Toon -PassNames DepthNormals -NoLayers -NoNormalMap -SurfaceFeatures None -OctNormals
~~~

Pair counts: 21 + 21 + 12 + 5 + 1 + 1. DepthNormals is shared by both models and uses the material selector for decoding, so it intentionally has no extra Endfield keyword compilation. Local logs: Tests~/OfflineCompiler~/backface-*.log. The task-only diff is saved beside them as backface-task.diff; pre-existing package edits are excluded from that diff. These offline checks do not certify Unity 6000.6.0f1 import or pixel-identical GPU output.
