# Mermaid material parity audit

2026-11-05. Source/material inspection and package repairs for Mermaid cloth, cables and fins. Existing captures predate these changes. No post-change Unity image, visual acceptance or measured improvement is claimed.

## Evidence and scope

All paths are relative to D:/UNITY_PROJECTS/LiminalNW. References use these aliases:

- `L/`: `Packages/jp.lilxyzw.liltoon/Shader/Includes/`.
- `F/`: `Packages/com.nw.fairytoon/Shaders/`.
- `E/`: `Packages/com.nw.fairytoon/Editor/`.
- `S/`: `Assets/BOOTH/YaokiSubWork/EvilFall Mermaid/0_Material/Type 1/`.
- `T/`: `Assets/BOOTH/YaokiSubWork/EvilFall Mermaid/0_Material/`.
- `M/`: `Assets/Lab/Materials/Mermaid/`.

| Material | Original | FairyToon copy | Endfield copy |
|---|---|---|---|
| Clothes | `S/EF5_M_Clothes.mat` | `M/FairyToon/050b917ee3e35254d9dcfa73670c96a5/EF5_M_Clothes.mat` | `M/Endfield/050b917ee3e35254d9dcfa73670c96a5/EF5_M_Clothes.mat` |
| Cables / transparent strips | `S/EF5_M_Transparent.mat` | `M/FairyToon/b5895e56eb2019040a369e0aa7a60390/EF5_M_Transparent.mat` | Reuses the FairyToon copy |
| Matching blur panels | `S/EF5_M_UnderwearBodysuit.mat` | `M/FairyToon/075c650e50aed30439565db90d1121e4/EF5_M_UnderwearBodysuit.mat` | Reuses the FairyToon copy |
| Fin transparency | `S/EF5_Trans.mat` | `M/FairyToon/fa16aead7704066428e39dfd05f071a5/EF5_Trans.mat` | `M/Endfield/fa16aead7704066428e39dfd05f071a5/EF5_Trans.mat` |
| Head transparency, residual limitation | `S/EF5_M_HeadTrans.mat` | `M/FairyToon/880f345022ae2934d94c465d20db0efa/EF5_M_HeadTrans.mat` | `M/Endfield/880f345022ae2934d94c465d20db0efa/EF5_M_HeadTrans.mat` |

`Assets/Lab/Editor/LabSilentCode.cs:320` reuses special-shader copies for Endfield. `Logs/Mermaid_convert.txt:3` and `Logs/Captures/ms2/ms_mdiff.csv` confirm the cable/underwear mapping; there is no separate Endfield cable material to tune.

Inspected pre-change images include `Logs/Captures/ms2/compare_chest.png`, `compare_close.png`, `compare_waist.png`, `ms_id_close.png`, `ms_mdiff_base_close.png`, `ms_mdiff_006_EF5_M_Transparent_close.png`, and `Logs/Captures/ms3/ms_liltoon_waist.png` / `ms_fairytoon_waist.png`. These establish symptoms/material association, not repair results.

The local lilToon package is project-patched: `L/lil_pass_forward_normal.hlsl:376` contains a GirlsExtraction clothing-damage hook, and the file also has VRChat light-volume integration. FairyToon ports only standalone lilToon texture, normal, blend and dissolve formulas. It adds no game-code dependency and does not copy the damage hook. Endfield intentionally replaces diffuse lighting; whole-image equality to lilToon is not its acceptance criterion.

## Data preservation and fragment order

`E/FairyToonConverter.cs:148` copies shared named properties, textures and transforms before setup. Comparing all original cable saved properties against its converted copy found only `_SrcBlend:1->5`, `_DstBlend:0->10`, `_TransparentMode:0->2` changed. All source textures/ST/scalars/colours otherwise survive. Both Clothes copies retain all 112 inspected normal/MatCap/reflection/emission/rim saved entries. Repairs change shader interpretation, not authored values.

The full source path is main/layer textures and layer alpha -> main alpha mask -> whole-material dissolve/coverage -> save layered albedo -> diffuse/shadows and unlit layer portions -> Rim Shade/backlight -> transparent premultiply -> refraction -> reflection -> MatCap 1 -> MatCap 2 -> rim/glitter -> emissions/dissolve edge -> backface colour/distance fade/fog/output.

Source anchors: `L/lil_pass_forward_normal.hlsl:343` (layers), `:360` (alpha mask), `:367` (dissolve), `:445` (saved albedo), `:487` (Rim Shade), `:503` (premultiply), `:507` (refraction), `:522` (reflection), `:529` (MatCaps); `L/lil_common_frag.hlsl:1963` (dissolve edge). FairyToon shares surface/coverage in `F/FairyToonInput.hlsl` and `F/FairyToonPasses.hlsl`, lighting/layers in `F/FairyToonCore.hlsl`, and optics in `F/FairyToonVariants.hlsl:231`.

## Cables: EF5_M_Transparent

The source is `Hidden/lilToonRefraction`, shader GUID `dce3f3e248acc7b4daeda00daf616b4d` (`Packages/jp.lilxyzw.liltoon/Shader/lts_ref.shader`). Its copy is `FairyToon/Refraction`, blur 0, queue 2900. This is distinct from the already-matching RefractionBlur underwear panels.

### Fixed: ignored alpha-mask ST

Source material `:26` and copy `:42` bind GUID `913435a630b698d4bb549d018b9dddc7`, `T/Mask&Normal/T_Noise_No.png (11).png`, scale (75,75), offset (0,0). Source `:274` / copy `:334` use Replace mode 1, scale 1, value .505. The converter preserved ST; the shader ignored it.

`L/lil_common_frag.hlsl:460` uses `LIL_SAMPLE_2D_ST`. FairyToon previously sampled raw uvMain with no `_AlphaMask_ST` declaration. `F/FairyToonInput.hlsl:30` now declares ST, and `FairyToonApplyAlphaMask` samples `uvMain * _AlphaMask_ST.xy + _AlphaMask_ST.zw`. The shared helper covers surface/base-alpha passes. Identity ST is unchanged; no new keyword or material override.

The image is black with white hexagonal cells, imported linear/bilinear/Repeat with mipmaps. Replace plus .505 gives black cells alpha .505 and white cells alpha 1. Main RGB is black, so opaque mask cells suppress the refracted scene. Sampling at 1x instead of 75x creates the large near-black cable/strap sections seen in the old chest capture, while the original shows fine lavender patterning. This active divergence matches the symptom shape; post-change visual confirmation remains required.

### Matched or neutral inputs

| Candidate | Actual state and conclusion |
|---|---|
| Main alpha | Source `:682`, copy `:814`: colour (0,0,0,.95686275). Replace mask overrides colour alpha. Do not recolour the material to compensate. |
| Refraction | Strength .05, Fresnel 4.5, white refraction colour, colour-from-main 0 (source `:546`, copy `:662`). Both URP paths sample `_CameraOpaqueTexture`: `L/lil_common_input.hlsl:41`, `F/FairyToonVariants.hlsl:41`. |
| Internal alpha | Source premultiplies at `L/lil_common_frag.hlsl:559`, then mixes scene at `:1296`; FairyToon `lerp(scene, col*s.alpha, s.alpha)` at `F/FairyToonVariants.hlsl:243` follows it. The double alpha on diffuse is intentional source behavior. |
| Reflection | UseReflection 1, ApplySpecular 1, metallic .408, smoothness 1, reflectance .505. **ApplyReflection 0**, source `:292` / copy `:352`: environment reflection is disabled. |
| MatCap 1 | Enabled, Add 1, blend 1, white alpha .6, lighting 1, shadow mask 0, main strength 0, normal strength 1, LOD 0, roll cancel 1. GUID `7e351f74ba46004419f97f5c6ead06cb`, `T/matcap/MAtcap 10.png`. |
| MatCap 2 | Enabled, Multiply 3, blend 1, white alpha .617, lighting 0, shadow mask 0, main strength 0, normal strength 1, LOD 0, roll cancel 0. GUID `08cbe0234429df54daccf618013ab1b3`, `T/matcap/Matcap 11.png`. |
| Post-optical layers | Source ignores MatCap ApplyTransparency under LIL_REFRACTION (`L/lil_common_frag.hlsl:1551`, `:1619`). FairyToon uses unit alpha/coverage (`F/FairyToonVariants.hlsl:183`, `:244`). Reflection/specular -> Add MatCap 1 -> Multiply MatCap 2 is matched. |
| Custom normals | Main bump is empty. Second MatCap custom normal is enabled, scale .1, but its bump map is empty; anisotropy is off. This historical reported loss is neutral with flat defaults here. Both custom layers are now implemented for the Clothes repair. |
| Other neutral inputs | MatCap UV1 blend vectors zero; VR parallax neutral in mono. Rim light, emission, main layers 2/3, backlight, glitter, POM/parallax and root dissolve disabled. |
| Blend/state | Source RGB One/Zero; copy SrcAlpha/OneMinusSrcAlpha with output alpha 1 (`F/FairyToonVariants.hlsl:261`): current framebuffer RGB is equivalent. Alpha-buffer contents differ when source environment reflection is off; not the cable RGB root cause. Cull 0, ZWrite 1 retained. |

### Unfixed: dormant refraction environment branch

`L/lil_common_frag.hlsl:1495` interpolates environment and scene with `alpha+(1-alpha)*pow(nvabs,abs(strength)*.5+.25)`, scales environment specular by material alpha, then sets alpha 1. FairyToon still uses ordinary environment reflection in `F/FairyToonCore.hlsl`; `F/FairyToonVariants.hlsl:185` has already set alpha 1.

This is dormant for the cable because ApplyReflection is 0. It is active for UnderwearBodysuit, whose blurred panels the user reported matching. No environment/blur composition change was made as a cable fix. The existing nine-tap URP blur approximation remains a known implementation difference, not a new diagnosis for the matching panels.

## Clothes: EF5_M_Clothes

### Fixed: custom MatCap normal .2 was discarded

Source first MatCap CustomNormal 1 / BumpScale .2 (`S/EF5_M_Clothes.mat:453`) contrasts with main BumpScale 1 (`:324`). Custom map `:190` is GUID `a346318c2275f3d47b781d67a350c771`, `T/Mask&Normal/EF5_M_Clothes_Normal_OpenGL.png`, identity ST. Both copies retain these values. Old FairyToon reused the mapped main normal, giving the MatCap full-strength rather than authored .2 detail.

`L/lil_common_frag.hlsl:1522` / `:1590` apply custom normal after normal-strength/anisotropy selection. New `F/FairyToonCore.hlsl:395` follows this order, samples main UV with custom ST, uses lilToon-compatible decoding and raw TBN preserved at `F/FairyToonPasses.hlsl:131`, then applies FlipNormal. Both layers are declared in all four property tables/shared input and exposed by the GUI. Existing generic conversion and FT_MATCAP setup retain them; no new keyword.

The explicit custom normal follows imported FlipNormal even in Endfield, whose ordinary surface normal intentionally faces the visible side. Custom-off behavior preserves the old interpolation. Empty custom textures use flat bump defaults. MatCap2 custom is off for Clothes, so its newly supported path is neutral for this material.

### Fixed: post-lighting tints used rim-darkened albedo

Source saves layered albedo before Rim Shade (`L/lil_pass_forward_normal.hlsl:445`, `:487`). MatCap main strength (`L/lil_common_frag.hlsl:1561`, `:1629`), metallic specular colour (`:1446`) and rim main strength use that saved value. FairyToon had already multiplied s.albedo by Rim Shade.

The saved pre-rim `s.emissionAlbedo` at `F/FairyToonCore.hlsl:735` is now also used by MatCap (`:430`), metallic specular (`:522`) and ordinary/optical rim (`:360`, `F/FairyToonVariants.hlsl:156`). Active Clothes inputs: MatCap2 MainStrength .2, metallic .121, Rim Shade grey .30188674 / border .596 / blur .407 / Fresnel 1.3. This removes an extra rim-darkened tint without changing diffuse. It does not explain the cable: its main RGB is black and MatCap main strengths are zero.

### Fixed: active main-layer/emission sampler ownership; dormant gaps retained

Main2nd and first emission now receive their own samplers instead of sampler_MainTex, matching the active Mermaid source paths at `L/lil_common_frag.hlsl:748`, `:1833` and the animated macros. FairyToon anchors: sampler declarations `F/FairyToonInput.hlsl:441`, `:461`, main-layer helper `:627`; emission helper/call `F/FairyToonCore.hlsl:633`, `:659`; optical call `F/FairyToonVariants.hlsl:212`. Mask sampling retains its source-defined sampler. Runtime/imported-mip impact is not measured offline.

Restoring all four owned samplers exceeded DX11's 16-sampler limit for a refraction configuration. The final implementation therefore restores only the two active target samplers (seven sampler declarations in FairyToonInput). Main3rd and Emission2nd remain on sampler_MainTex, retaining a known non-target parity gap relative to `L/lil_common_frag.hlsl:844` and the second-emission path. They are disabled on the audited target materials. Do not claim all layer samplers now match lilToon.

Clothes emission uses the linear hex image at ST300x300, RGBA emissive mask, HDR colour (32.446583,11.407398,58.886852), MainStrength 1, blink (.2,0,pi,0). It already used pre-rim albedo. No authored brightness or importer was changed.

### Inputs retained and remaining differences

| Feature | Authored state |
|---|---|
| MatCap1 | GUID `80458ab13ea28d5499f6dc4028192310`, `T/matcap/matCap 1.png`; mask `b20cd03a227c8d645a83abd7ee3799c4`, `T/Mask&Normal/EF5_M_Clothes_Roughness.png`; Add, alpha .584, LOD3, MainStrength0. |
| MatCap2 | GUID `0eeb6c59708498e49ada63ab47694006`, `T/matcap/MatCap 5.png`; mask `4cb97f1546d01674fa9552cf5209dea7`, `T/Mask&Normal/EF5_M_Clothes_Emissive.png`; Add, alpha1, LOD0, MainStrength.2. |
| Both | Blend1, NormalStrength1, Lighting1, ShadowMask0, ZRotCancel1, Perspective1. |
| Reflection | UseReflection/ApplyReflection/ApplySpecular/ApplySpecularFA all1, metallic.121, smoothness.9 with roughness map above, reflection alpha.633, no custom cube. |
| Importers | Shared source GUIDs/importers: normal linear/NormalMap; hex linear; MatCaps/emissive/roughness sRGB. All six inspected images bilinear/Repeat/mipmapped. No speculative gamma/importer change. |
| Unchanged difference | FairyToon direct specular uses combined l.shadowmix; lil selects pass/pipeline attenuation (`L/lil_common_frag.hlsl:1467`; `F/FairyToonCore.hlsl` specular section). Deliberate existing behavior, not changed. Endfield diffuse/probe context differs; no screenshot-proven environment correction. |

## Fins: EF5_Trans

### Fixed: active whole-material dissolve was missing

This is separate from the inactive Main2nd dissolve. Source root Params=(1,0,0,.668), `S/EF5_Trans.mat:684`, survives both converted copies. FairyToon previously lacked root dissolve properties/runtime and the converter reported it unsupported.

- Mask `:86`: GUID `fcce2b191d312184280dd224d838f1a2`, `T/Mask&Normal/EF5_Tran_Mask.png`, identity ST.
- Noise `:90`: GUID `f7281f7e9297b8f41891a1aa34d0e7f0`, `T/Mask&Normal/T_Noise_No.png (15).png`, ST7x7, scroll(.05,.2,0,0) at `:683`, strength1 at `:329`.
- Edge colour `:682`: (.67450976,.795309,2.9960787,1).

`L/lil_common_functions.hlsl:668` uses raw UV0 and each map ST. For `d=mask+(noiseSample-.5)*noiseStrength-threshold`, alpha multiplies `d>0`; edge=`1-saturate(abs(d)/width)`. `L/lil_common_frag.hlsl:1963` adds the edge after emissions, with normal transparency applying alpha once (`:1971`).

New `F/FairyToonInput.hlsl:691` evaluates mask mode1 after alpha-mask replacement (`:748`) in shared surface/coverage. The edge is added after emission in `F/FairyToonCore.hlsl:785` and optical layers at `F/FairyToonVariants.hlsl:223`. All four variants expose standalone properties. Eligible ordinary/refraction/Fur-body forward/depth/shadow/outline/motion paths share coverage. Opaque, Gem and Fur ribbon-shell dissolve remain inactive, matching their applicable source fragment paths (`F/FairyToonInput.hlsl:695`). Mode0 returns zero edge without sampling. GUI/converter distinguish mode1 from unsupported position modes. Dissolve adds no keyword, sampler or varying. Source edge colour alpha is not an intensity control; the stock edge uses RGB. Width0 is protected with a 1e-6 denominator.

The old ms3 original shows swirled holes/blue edges and its noise scroll is animated, while the converted sheet is uniformly translucent. This fits missing root dissolve. Fine hex texture is already visible in `ms_fairytoon_waist.png`; do not attribute the large swirls to a speculative default-texture sampler mismatch. Post-change captures must hold animation time/pose/camera/lighting constant.

### Fin UV/layer audit: retained and neutral

| Input | Source state and conclusion |
|---|---|
| Base | MainTex null (`:170`), HSVG(0,1,1,1), scroll0, gradation0, adjustment mask null; colour(.53417015,.5058824,1,.95686275). No tone correction/atlas fix needed. |
| Main2nd | Enabled (`:645`), hex GUID913435... (`:142`), ST75x75/offset0, angle -.17453294 (`:409`), UVMode0 (`:419`), Add1, Lighting1, AlphaMode0, Cull0. Colour(.7329843,.9367802,2,1). ST/angle already follow source decal UV algebra even when decal is disabled. |
| Masks | Main2nd mask fcce... (`:130`), same as Replace alpha mask (`:26`, `:274`), both identity ST. Cable AlphaMask-ST defect does not explain the fins. |
| Atlas/decal | Decal/copy/mirror/MSDF flags all0; animation(1,1,1,30), subparam(1,1,0,1) are identity. No atlas packing issue or UV rewrite. |
| Disabled | Main3rd0, Main2nd dissolve mode0, layer DistanceFade.z0, AudioLink0, POM/Parallax0. UseBump1 but bump texture empty, hence flat. |
| Sampler | Own Main2nd sampler restored as above. The hex image is linear/bilinear/Repeat, alpha255. Whether the former null-main sampler caused any runtime difference remains unproved. |
| Conversion | Both copies retain shared authored inputs. Ordinary transparent setup changes SrcBlend1->5 and TransparentMode0->2. |
| Emission | Map Gradient2.jpg (GUID ad88...), scroll(0,.3), HDR colour(2,2,2,1), MainStrength1; mask is the hex image at ST100x100. Both map and RGBA mask values/transforms survive. `L/lil_common_frag.hlsl:1815` / `:1839` and `F/FairyToonCore.hlsl:633` use raw UV0 for the animated mask, pre-rim albedo and sequential alpha-weighted emission. |
| ZWrite / depth | Cull0, queue3200 and ZWrite1 are retained. Source `lts_trans.shader:674` owns forward and auxiliary passes; ordinary transparent FairyToon disables camera depth/normals/motion at `E/FairyToonMaterialSetup.cs:193`. At queue3200 the ordinary opaque-only URP prepass does not draw either material; this does not explain missing surface patterns. Forward still writes authored depth. |
| Unfixed transparent shadows | Source `_ShadowReceive=1`, `_SubpassCutoff=.5` (`S/EF5_Trans.mat:587`, `:609`). `L/lil_common_frag_alpha.hlsl:99` clips normal alpha, then dithers transparent shadow coverage and clips against SubpassCutoff (`:106`-`:114`). `F/FairyToonPasses.hlsl:306` / `:336` only uses normal coverage/cutoff; it now includes dissolve holes but still lacks source shadow dithering/subpass cutoff. This may alter cast/self-shadow density; it is not the missing global-dissolve implementation. Left unchanged because changing all transparent shadow policy exceeds the isolated material repair and no controlled render established its contribution. |

The pre-rim tint repair also affects these fins: MatCap2 MainStrength .4 (`S/EF5_Trans.mat:443`), metallic .408 (`:463`) and enabled black Rim Shade (`:654`, `:733`) previously shared rim-darkened albedo. ApplySpecular is 1 but ApplyReflection is 0 (`:292`), so the dormant optical environment gap is not this material's cause. Both custom MatCap normals are off (`:440`, `:454`).

### Unsupported modes remain explicit

Whole-material UV mode2 and object-position mode3 remain inactive/reported. `S/EF5_M_HeadTrans.mat:691` uses (2,1,.175,.2); `S/EF5_M_BodyReplaceMaterial.mat:691` uses mode3 (3,1,-1,1.1). They are not supported merely because mask mode1 was added. Layer-specific dissolve is separate and remains unsupported when active. GUI `E/FairyToonGUI.cs:131` and converter `E/FairyToonConverter.cs:379` explicitly identify the boundary. Project-specific damage/inversion behavior is outside the standalone port.

## Neck/choker and change boundaries

Existing images retain the lavender neck rim on both original and FairyToon. Material-ID/substitution images show small nearby differences but do not isolate the reported line. No neck/choker-specific shader, colour, mesh or queue edit was justified. Controlled close-up and material-slot isolation remain necessary before assigning a cause.

No general hair tint/lighting adjustment is justified by these findings. Already-matching blur panels are regression cases, not a reason to change optical environment composition. The package repair does not edit artist materials, textures/importers, scenes, prefabs, meshes or project settings.

## Validation and remaining acceptance

- Targeted MatCap offline run: 4 passed, 0 failed (three new groups plus existing MatCap blend). Covers all variants/layers' schemas, setup/converter preservation, numeric evaluation of the actual helper against lil decoder, custom-off/null map behavior, ST, precedence, both layers and mirrored/back-facing TBN.
- Final Node regression suite: **100/100** in VerifyLayers.cjs; offline-tool regressions **4/4** in VerifyOfflineTools.cjs (**104/104 total**, zero failures/skips).
- Final DX11 compilation: **142/142 pass/model configurations, 296/296 stage programs**. All reflected per-shader UnityPerMaterial layouts and stage interfaces passed. Maximum PS resources across these runs: 50 textures / 15 samplers (limits 128 / 16); VS/GS each at most 1 texture / 1 sampler. Material keyword dimensions did not increase.

| Successful offline matrix | Configurations | Stage programs |
|---|---:|---:|
| All four shaders, Toon + Endfield, Transparent, MermaidMatrix, cascaded shadows / Forward+; both refraction blur states | 74 | 160 |
| FairyToon, all six existing shading models, Opaque, MermaidMatrix, cascaded shadows / Forward+ | 45 | 90 |
| FairyToon + Refraction, Toon + Endfield, Cutout (Refraction keeps its transparent definition), screen shadows / Forward+ probe atlas | 23 | 46 |

- Unity import/compile, Editor C# compilation, play mode, visual captures and Vulkan: **not run by Codex**.
- Post-change image comparison/metric: **not available**.

Visual acceptance still needs cable pattern/translucency, Clothes MatCap highlight shape and rim tint, fin dissolve/hex detail at controlled time, unchanged blur panels, and a neck/choker close-up. Use the project's authorized Unity owner/editor lock workflow. Offline assertions/compiler success do not establish visual completion.

### Reproduce the successful checks

From the project root, in PowerShell (no Unity executable is invoked):

```powershell
node --test Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs
node --test Packages/com.nw.fairytoon/Tests~/VerifyOfflineTools.cjs
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Transparent -MermaidMatrix
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Coverage Opaque -MermaidMatrix
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon,Refraction -Models Toon,Endfield -Coverage Cutout -MainLightShadows Screen -LightingPaths ForwardPlusAtlas
```

MermaidMatrix compiles full surface features, MatCap without primary/secondary normal maps or anisotropy, and MatCap disabled. Custom-normal and dissolve switches are material uniforms; CPU numerical/source checks cover their values, while DX11 compiles both runtime branches. Two earlier compiler failures (optical MatCap argument count, then the 17-sampler combination) were repaired before all successful runs above. Temporary baselines/compiler files/logs are removed after recording these results. No Git command or Unity process was used.

Changed package files: README.md; Editor/FairyToonConverter.cs, FairyToonGUI.cs, FairyToonMaterialSetup.cs; Shaders/FairyToon.shader, FairyToonRefraction.shader, FairyToonFur.shader, FairyToonGem.shader, FairyToonInput.hlsl, FairyToonCore.hlsl, FairyToonPasses.hlsl, FairyToonVariants.hlsl; Tests~/CompileShaders.ps1, VerifyLayers.cjs and this audit.


## Round 2

2026-11-05. Package-only follow-up. The user reports that Round 1 compiled cleanly in Unity 6000.6.7f1, cable/choker appearance now matches in Toon and Endfield, and fin dissolve swirls are visible. Those are user-provided capture results, not a Unity run by Codex. This section supersedes Round 1's claims about the fur dissolve guard and Endfield custom-normal facing. No Git command, Unity process, material/asset edit, lilToon edit or GirlsExtraction damage port was performed.

### A. Review repairs

| Issue / cause | Repair and regression evidence |
|---|---|
| Fur shell dissolve exclusion used a late include guard. VariantInput loads Input before Fur.hlsl defines FAIRYTOON_FUR_INCLUDED, so the guard was false when the helper compiled. | F/FairyToonFur.shader:613,672 defines FAIRYTOON_FUR_SHELL in each shell pass before VariantInput; F/FairyToonInput.hlsl:695 excludes that marker. The normal Fur body retains dissolve. Tests~/VerifyLayers.cjs:2109 follows actual ShaderLab pass/common includes and include guards up to the helper, covering both shells and Forward/ShadowCaster/DepthOnly/DepthNormals/MotionVectors in transparent/cutout coverage. Replacing Input with the old version in memory makes the shell test fail. Tests~/CompileShaders.ps1:309 additionally rejects dissolve texture bindings reflected from compiled shell pixel shaders. |
| Endfield custom MatCap normals obeyed FlipNormal while ordinary/geometric normals faced the visible side. Clothes has Cull=0, FlipNormal=0 and CustomNormal=1. | F/FairyToonCore.hlsl:414 transforms the custom normal using the raw TBN, then applies the visible-side rule under _FT_ENDFIELD (:416); Toon still calls FairyToonFacingNormal. Both MatCap layers use this helper. Tests~/VerifyLayers.cjs:1832 now exercises both shading models, both faces, FlipNormal 0/1, both custom layers, normal encodings, mirrored TBN and bump strengths. |
| Opaque/Gem displayed dissolve controls and conversion treated mask mode 1 as supported without checking target coverage. | E/FairyToonGUI.cs:142 adds an informational HelpBox, including mixed selections. E/FairyToonConverter.cs:177,393 reports active source dissolve ignored by the final Opaque/Gem target after profile and material setup. Final material mode matters because Opaque/Cutout share one shader. Existing unsupported mode 2/3 reporting is retained. Test: Tests~/VerifyLayers.cjs:2133. |

### B. Fin colour: blend hypothesis and active colour-input defect

The One-to-SrcAlpha rewrite is real, but **it is not an extra-alpha bug for this fin's active settings**. Existing FairyToon helpers already translate lilToon's premultiplied layer operations to straight RGB. No blend state, queue, opacity, cutoff, material value or texture importer was changed in Round 2.

The exact relevant source sequence is:

1. Main/Main2nd layering, then Replace alpha mask (L/lil_pass_forward_normal.hlsl:343,363). S/EF5_Trans.mat:275 uses mode 1 with scale 1, offset 0; this replaces the main tint's .95686275 alpha.
2. Texture dissolve (:375), then transparent cutoff (:413). Params=(1,0,0,.668), noise strength 1 and the existing animated noise/ST are retained. FairyToon does the same in F/FairyToonInput.hlsl:747,748,765. Alpha is clipped at .001, not remapped.
3. Save layered albedo (:445), diffuse/shadow and unlit layer portions (:448-483), Rim Shade (:487), backlight (:494), then premultiply (:504; L/lil_common_frag.hlsl:559). FairyToon keeps straight RGB through F/FairyToonCore.hlsl:733. Its existing Rim Shade implementation multiplies albedo before lighting; that order is not generally interchangeable with source post-lighting Rim Shade, and was not changed as a speculative fin fix.
4. Reflection/specular, MatCap1, MatCap2, rim/glitter, emission, dissolve edge (:525,532,537,543,551,559,571). Both fin MatCaps are Add and both ApplyTransparency values are 1 (S/EF5_Trans.mat:436,439,450,453); ReflectionApplyTransparency=1 (:542). ApplyReflection=0 (:293), so environment reflection is disabled; direct specular remains enabled. Source multiplies those post-layer weights by alpha (L/lil_common_frag.hlsl:1453,1552,1620). FairyToonTransparentBlend (F/FairyToonCore.hlsl:336) already compensates this. For Add with ApplyTransparency=1, source P'=alpha*C+alpha*w*layer; FairyToon C'=C+w*layer gives the identical framebuffer contribution alpha*C'. Metallic diffuse reduction also commutes with premultiply.
5. Emission Add explicitly weights by alpha in source (L/lil_common_frag.hlsl:1863); FairyToonEmissionBlend (:628) returns the corresponding straight RGB. The dissolve edge is then added with alpha once (source :1963,1971; FairyToon :790). Backface tint/distance fade are inactive for this fin. Both paths retain the same output alpha and One/OneMinusSrcAlpha alpha-channel blending; FairyToon returns s.alpha at F/FairyToonPasses.hlsl:190. Fog/output are outside the frozen CPU colour comparison.

This reasoning applies to Toon and Endfield: they call the same post-layer helpers. Tests~/VerifyLayers.cjs:2180 executes actual FairyToon mask, dissolve, transparent-blend and emission helpers against installed lilToon blend arithmetic in a combined sequence with frozen samples, both lighting contexts and black/dark/light backgrounds. Existing tests separately cover Normal/Add/Screen/Multiply and ApplyTransparency off/on. Unmasked terms at alpha zero or below the helper's 1/4096 division bound remain a known straight-output limitation; this material has ApplyTransparency on and clips below .001, so that limitation does not cause its reported appearance.

**Active defect: Gamma HDR colours had native HDR shader flags.** The saved values were copied correctly, but their shader input interpretation was not:

| Property | Source saved RGB | Source declaration | Old FairyToon declaration |
|---|---|---|---|
| _Color2nd | (.7329843,.9367802,2) | Shader/lts_trans.shader:55, [lilHDR] Color | [HDR] Color |
| _DissolveColor | (.67450976,.795309,2.9960787) | Shader/lts_trans.shader:466, [lilHDR] Color | [HDR] Color |

S/EF5_Trans.mat:681,683 and both converted copies retain these values (Endfield copy :812,814). The project is Linear (ProjectSettings/ProjectSettings.asset:49). lilHDR is an editor drawer explicitly labelled "Gamma HDR" (Packages/jp.lilxyzw.liltoon/Editor/lilToonPropertyDrawer.cs:12-27), not the native HDR property flag. lilToon's own optimizer uses Color.linear for non-native-HDR Color properties (Editor/lilOptimizer.cs:242,294-306). Unity documents the [Color property conversion](https://docs.unity.cn/2022.3/Documentation/Manual/SL-PropertiesInPrograms.html) and [native property flags](https://docs.unity.cn/6000.0/Documentation/ScriptReference/Rendering.ShaderPropertyFlags.html). FairyToon's native HDR declarations bypassed the source colour interpretation, weakening blue relative to red/green in the blue HDR Main2nd and dissolve edge. The Main2nd mismatch also feeds its saved-albedo-dependent emission. This is a concrete active mismatch consistent with pale fins; its visual contribution is not yet measured in Unity.

Repair: all four F/FairyToon*.shader property tables at :36,40,60 now use [FairyToonHDR] for _DissolveColor, _Color2nd and _Color3rd. The package drawer E/FairyToonGUI.cs:8 provides HDR editing while preserving native Color flags and raw saved values. Main layer texture rows use TexturePropertyWithHDRColor (:1122) for the same reason. This editor method explicitly supports HDR editing without changing the property declaration ([Unity reference implementation](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Inspector/MaterialEditorGUIHelpers.cs)). No manual HLSL gamma conversion or reconversion/migration of existing copies is needed. _Color3rd receives the same contract correction for the parallel layer, though it is disabled on this fin. Emission1/2 keep their source-matching native HDR flags. Test: Tests~/VerifyLayers.cjs:2147 compares all four shader declarations and Gamma/Linear upload models, including >1 RGB and unchanged alpha. CPU transfer numbers are illustrative, not measured native Unity uploads.

Endfield differs because it replaces the diffuse lighting and supplies a different light context to shared MatCaps. This fin's Endfield copy has _EFShadowSaturation=.2 (:443), _EFRampOn=1 (:432); F/FairyToonEndfield.hlsl:39 boosts colour saturation, :442 applies ramp hue and :454,609 supply ambient/top/direct light. These active differences can make Endfield bluer despite the same incorrect colour flags. Round 2 corrects the shared colour contract in both modes; it does not force Endfield diffuse to match Toon.

**Ordinary FairyToon materials:** transparent blending and alpha remain unchanged. In Linear projects, active _Color2nd/_Color3rd/_DissolveColor now follow lilToon's gamma-authored Color interpretation, including existing non-converted materials. Their appearance can change; they previously differed from lilToon in the same way. This also applies to active layers on opaque/cutout materials. Disabled layers, neutral black/white values, Gamma-space projects and native HDR emissions retain their previous interpretation. Opaque/Gem still ignore root dissolve. The five other native-HDR metadata differences (_BackfaceColor, _BacklightColor, _ReflectionColor, _ReflectionCubeColor, _GlitterColor) remain outside this focused repair; they are disabled or black/white neutral on this fin.

### Round 2 verification

Node: **108/108 passed** (104 VerifyLayers + 4 VerifyOfflineTools). These include real include-order regression, Endfield/Toon custom normals, target-mode notices, gamma-HDR metadata and frozen fin alpha/layer composition. Independent read-only review found no blocking defect in the repairs and verified that substituting the old dissolve guard makes the new shell test fail.

All DX11 offline runs succeeded, targeting the script's UNITY_VERSION=600060 with installed URP headers:

| Matrix | Pass/model configurations | Stage programs |
|---|---:|---:|
| All four shaders; Toon + Endfield; Transparent; MermaidMatrix; cascaded shadows / Forward+; both refraction blur states; **ProbeVolumes L1** | 74 | 160 |
| FairyToon; all six shading models; Opaque; MermaidMatrix; cascaded shadows / Forward+ | 45 | 90 |
| FairyToon + Refraction; Toon + Endfield; Cutout; screen shadows / ForwardPlusAtlas | 23 | 46 |
| FurForward + FurSecond; Toon + Endfield; Transparent; **ProbeVolumes L1**; compiled dissolve-resource rejection | 4 | 12 |
| **Total** | **146** | **308** |

Per-shader UnityPerMaterial layouts match wherever used; varying/interface checks pass. The maximum reflected sampler count is **16/16**, including probe-volume optics; no sampler limit was exceeded. No shader keyword dimension, CBUFFER field, sampler or varying was added. The standalone shell run specifically exercises the new DX11 resource regression.

Reproduce from project root, without invoking Unity:

~~~powershell
node --test Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs Packages/com.nw.fairytoon/Tests~/VerifyOfflineTools.cjs
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Transparent -MermaidMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Coverage Opaque -MermaidMatrix
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon,Refraction -Models Toon,Endfield -Coverage Cutout -MainLightShadows Screen -LightingPaths ForwardPlusAtlas
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders Fur -Models Toon,Endfield -Coverage Transparent -PassNames FurForward,FurSecond -ProbeVolumes
~~~

Not run / still unverified: Unity import or compilation on either 6000.6.0f1 or 6000.6.7f1, Editor C# compilation/drawer behavior, actual native colour-uniform uploads, play mode, Vulkan, Round 2 captures or image metrics. Offline HLSL success does not compile ShaderLab property flags or Editor C#. A fixed-time/pose/camera/light capture remains required for the blue fin over the black bodysuit, dissolve edges and emission; also check Clothes inner-face custom MatCaps, dissolve-free fur shells with dissolving body, and unchanged cable/choker/blur-panel appearance. Round 1's remaining sampler, transparent-shadow and dormant optical-environment gaps remain unverified/unrepaired. No screenshot-proven colour match is claimed.

Round 2 changed files (all package-relative): Editor/FairyToonConverter.cs, Editor/FairyToonGUI.cs; Shaders/FairyToon.shader, FairyToonRefraction.shader, FairyToonFur.shader, FairyToonGem.shader, FairyToonInput.hlsl, FairyToonCore.hlsl; Tests~/CompileShaders.ps1, VerifyLayers.cjs, MermaidParityAudit.md. Temporary Round 2 logs/baselines remain under Tests~/Round2Temp~: automatic approval review rejected the verified package-local cleanup command with "blocked by policy". No cleanup bypass was attempted. The separate OfflineCompiler~ scratch directory is empty.


## Round 3: Dissolve

2026-11-05. Package-only implementation of whole-material modes 2/3 and Main Color 2nd/3rd dissolve. This supersedes the unsupported-mode/layer statements in rounds 1?2. No Git command, Unity process, source lilToon edit, material/scene edit or GirlsExtraction clothing-damage hook was used. No new capture or visual match is claimed.

### Source contracts and implementation

| Feature | lilToon source | FairyToon implementation / result |
|---|---|---|
| Mode/shape rounding; mask threshold; UV point/line; object point/line | L/lil_common_functions.hlsl:626?664,668?716 | F/FairyToonInput.hlsl:758: shared FairyToonCalcDissolve; strict value > threshold; root wrapper :789; no material keyword added. |
| UV source, ST and noise animation | L/lil_common_frag.hlsl:492,510,765,782,861,878; L/lil_common_functions.hlsl:692,694 | F/FairyToonInput.hlsl:758: raw UV0 for every dissolve family, independent of main ST, layer UV selection/decal transform. Each dissolve texture applies its own ST; noise adds frac(scroll.xy*time). Noise rotation ZW remains unused, matching source. |
| Distinct UV-line formulas | L/lil_common_functions.hlsl:654 versus :703 | F/FairyToonInput.hlsl:772: no-noise uses centered rotation by Pos.w; noisy uses dot(uv0,normalize(Pos.xy)), even when strength is zero. Point modes use distance; object line uses normalized XYZ in both helpers. |
| Noise feature selection | L/lil_common_frag.hlsl:487,761,857; Editor/lilToonSetting.cs:650,655,695 | F/FairyToonInput.hlsl:778; three _FT_*UseNoise uniforms default 1 in all property tables. They reproduce the installed source's unconditional generated noise defines without keywords. Null textures and zero strength do not choose the no-noise helper. GUI can disable a helper explicitly for a source build whose noise feature was excluded. |
| Layer alpha placement | L/lil_common_frag.hlsl:748?805,844?901 | F/FairyToonInput.hlsl:810?853: each enabled layer dissolves after texture/blend mask and before AlphaMode and lit blending. Layer culling already occurs in FairyToonMainLayer; its zero-alpha multiplication commutes with dissolve. Edge is independent of that alpha, as in source. Alpha-mask replacement and root dissolve follow both layers. |
| Layer edge placement and alpha | L/lil_pass_forward_normal.hlsl:571?584; L/lil_common_frag.hlsl:1963,1969?1975 | F/FairyToonCore.hlsl:790?793 and F/FairyToonVariants.hlsl:223?225: sum root and both layer RGB edges after emissions, before backface/fog. It is emission addition, not a layer-RGB lerp; layer tint alpha, layer lighting, blend mask, cull and edge-color alpha do not multiply the edge. Ordinary transparent output weights it once by final material alpha; refraction adds it after scene composition with unit optical coverage. |
| Object-space coordinates / skinning | L/lil_common_appdata.hlsl:72; L/lil_common_vert.hlsl:103,117,119,214 | F/FairyToonInput.hlsl:566?591 reuses forward/world layer coordinates and inverse object transform, recovering the current post-skin POSITION coordinate, not bind pose or prior-frame position. Unbiased coverage world positions are used for shadow/depth; no shadow bias leaks into dissolve. Only auxiliary coverage without an existing position gets TEXCOORD12. There is no added forward varying or Gem/shell varying. |
| Outline and motion hull | L/lil_vert_outline.hlsl:22?24; L/lil_common_vert.hlsl:119,214 | F/FairyToonPasses.hlsl:245?265,489?500,512: ordinary URP source copies OS after outline modification. FairyToon uses its rendered outline hull, including authored Z bias. Motion uses the same authored bias and excludes its additional motion-only camera offset. The existing FairyToon outline geometry formula itself is unchanged. |
| Properties, conversion, GUI | Shader/lts.shader:74?80,105?111,462?468; Editor/lilToonPropertyDrawer.cs:850,958; Editor/lilMainInspectorGUI.cs:670,716,1232 | All four F/FairyToon*.shader tables: root Pos :32, layer sets :59,:87; E/FairyToonConverter.cs:148?161,391?427 copies values and ST, preserves honest exclusions; E/FairyToonGUI.cs:77,176?319 puts root dissolve in its own advanced foldout after Outline and before Refraction/Fur, and layer dissolve inside each layer; its mode/shape/threshold/width/position/mask/noise/edge ordering uses local controls instead of a lilToon assembly dependency. All three edge colors use [FairyToonHDR], retaining Gamma HDR Color flags, defaults and RGB-only edge use. |

All mask/noise sampling shares sampler_MainTex, matching L/lil_common_frag.hlsl:503,517,815,911. Four layer textures were added; no sampler, keyword dimension or external dependency was added. Runtime mode 0 returns before texture sampling. The existing whole-material mode-1 formula and finite width protection remain; defaults do not infer a noise feature from a texture's dimensions or assignment. Position reconstruction is conditional on an active mode 3.

### Existing material walkthroughs

**HeadTrans:** S/EF5_M_HeadTrans.mat:691?692 has Params=(2,1,.175,.2), Pos=(1,-.1,0,0). This source uses Shader/lts_trans.shader:674 ? ltspass_transparent/FORWARD; the latter defines the noise feature at :770. ProjectSettings/lilToonSetting.json:94 also enables it. Contrary to the no-noise rotation interpretation of W=0, the actual source therefore follows the XY projection formula.

Source noise at :90 has GUID f7281f7e9297b8f41891a1aa34d0e7f0, ST=(3,12,0,0); strength **.5** at :329, scroll=(2,.2) at :690. With raw UV0=(u,v), time t and sampled red N:

~~~text
noiseUV = (3u,12v) + frac((2,.2)*t)
d = (u - .1v)/sqrt(1.01) + .5*(N(noiseUV)-.5) - .175
alpha' = alpha * (d > 0)
edge = 1 - saturate(abs(d)/.2)
~~~

At neutral noise, the boundary is u=.175*sqrt(1.01)+.1v ? .175873+.1v; increasing U survives. Noise pushes this slightly slanted UV boundary into animated irregular holes/patches. On surviving fragments, the authored Gamma HDR blue-violet edge (:689: 1.0152977,.866098,3.9066548) is strongest at the boundary and falls off over .2 projection units. The removed side is clipped by the ordinary transparent cutoff (.001). This describes the UV effect; no mesh UV inspection/render is claimed to identify an anatomical boundary. Both FairyToon and Endfield copies retain the authored Pos/Params/noise already (FairyToon copy :819?820, Endfield :820?821), so recompiling changes their behavior from ignored dissolve to this effect without rewriting the materials. Toon/Endfield diffuse remains intentionally different. If the noise helper is explicitly disabled, the source-compatible alternative is d=u-.175; that is not the current source-material path.

**BodyReplaceMaterial:** S/EF5_M_BodyReplaceMaterial.mat:691?692 has Params=(3,1,-1,1.1), Pos=(0,1,0,0). Its GUID resolves to Shader/lts_ref_blur.shader, whose :714 defines root noise and :463 defaults it to gray. The material has no assigned mask/noise (:86,:90), noise strength .1 (:329), zero scroll (:690). Neutral gray contributes zero, so d=positionOS.y+1, alpha'=alpha*(y>-1), edge=1-saturate(abs(y+1)/1.1). A zero contribution assumes the default gray red is .5; actual Unity texture upload was not measured. The transition plane remains at y=-1 in renderer object space and follows the renderer transform; current skinned vertices deform through that plane. Edge is 1 at y=-1, .5 at y=-.45 (and y=-1.55), zero at y=.1 (and y=-2.1).

This is **RefractionBlur**, not ordinary transparent clipping: source L/lil_pass_forward_normal.hlsl:407 excludes refraction from transparent discard; L/lil_common_frag.hlsl:1969?1975 leaves optical edges unmasked. On the dissolved side the diffuse body contribution is removed and refracted scene remains; the symmetric edge band can remain on either side of the plane. FairyToon retains that existing optical dissolve path. This source also enables environment reflection (material :292,:659), so the pre-existing special refraction/environment composition gap at L/lil_common_frag.hlsl:1495?1498 is active here; matching dissolve does not establish full optical parity. No converted BodyReplaceMaterial file was found under Assets/Lab/Materials; its arithmetic is a source-only fixture, not a verified converted asset. No new asset was created outside the allowed package.

The Mermaid/Silent Code materials do not exercise active layer dissolve. Synthetic tests cover both layers, all modes, point/line shape, noise on/off, alpha modes, opaque coverage and late edge addition. Root mode-1 fins retain their existing parameters/formula; existing materials with root/layer mode 0 gain no dissolve sampling. Previously dormant layer parameters now take effect when a supported body layer is enabled.

### Retained boundaries and unverified behavior

- Root Opaque skip matches L/lil_pass_forward_normal.hlsl:369. Opaque **layer** dissolve is supported because source layer calls :345,:353 have no opaque exclusion.
- Gem visible forward has neither layer nor root dissolve (L/lil_pass_forward_gem.hlsl:144 onward); fur ribbon forward goes from main to fur without either (L/lil_pass_forward_fur.hlsl:147,152). Fur body uses normal forward (Shader/lts_fur.shader:847), and remains supported.
- **Retained Gem auxiliary policy differs from source:** L/lil_common_frag_alpha.hlsl:12,45,53,66,72 runs nonopaque layer/root alpha coverage without a Gem guard. FairyToon continues ignoring dissolve across Gem passes, preserving its existing consistent-visible-body policy as requested. No silent coverage-policy change was made. Fur auxiliary geometry/noise/shadow differences already documented in rounds 1?2 remain.
- Width 0 keeps FairyToon's existing max(width,1e-6) protection; source divides directly by width. Widths >=1e-6 match. Negative widths, nonfinite parameters and zero-length normalized directions are not a promised parity domain; zero direction retains the source's undefined normalization rather than inventing a fallback.
- Global/optimized lilToon feature exclusions are not inferable from saved material properties. Converter reports the enabled-noise default; a source build with a noise feature excluded requires the corresponding explicit FairyToon helper toggle. No automatic source-shader parsing or lilToon runtime dependency is added.
- Existing AudioLink, layer distance fade, decal atlas animation, source custom vertex deformation/ID-mask dissolve inversion, main UV animation and project-specific clothing damage remain outside this port. Other sampler, transparent-shadow and refraction-environment limitations from earlier rounds remain. No GirlsExtraction hook was copied.
- Not run: Unity import/ShaderLab or Editor C# compilation, Unity 6000.6.0f1/6000.6.7f1 execution, play mode, actual skinned renderer deformation captures, Vulkan, native color/texture uploads, fixed-time pose/camera/light captures or image comparisons. Mathematical skinning/coordinate and DX11 interface checks do not establish GPU visual parity. README lilToon/SeeThroughHair credits are retained.

### Round 3 verification

Node: **115/115 passed**, zero failures/skips (111 VerifyLayers + 4 VerifyOfflineTools). Tests execute the installed lilToon and actual FairyToon helper bodies, including raw UV/ST/scroll/sample ownership, rounded selectors, all modes/shapes/noise paths, strict boundaries, actual Mermaid parameter fixtures, both layers/AlphaMode order, independent late edges, Opaque/Gem/shell guards, mirrored nonuniform object transforms, authored outline Z bias, multi-selection component preservation and source-sensitive mutation checks. All four property tables retain source defaults and Gamma HDR edge-color behavior. These are offline numeric/source tests, not native Unity color uploads or skinned renders.

All final DX11 runs passed, using installed URP headers and the compiler script's UNITY_VERSION=600060:

| Successful matrix (all with ProbeVolumes L1 / cascaded shadows / Forward+) | Configurations | Stage programs | Max samplers |
|---|---:|---:|---:|
| All four shaders; Toon + Endfield; Transparent; runtime + five mode/shape cases ? noise off/on; both refraction blur states | 342 | 688 | 16 |
| Same shaders/models/coverage; Disabled supplement | 42 | 88 | 16 |
| FairyToon; Toon + Endfield; Opaque; runtime + ten active cases | 53 | 106 | 15 |
| Opaque Disabled Forward + SeeThroughHair supplements (2 + 2 configurations) | 4 | 8 | 15 |
| All four shaders; Toon + Endfield; Cutout; no main/emission layers, no outline; runtime + Disabled + ten active cases | 372 | 748 | 14 |
| Gem dedicated compiled resource guards, every pass; Toon + Endfield | 8 | 16 | 16 |
| FairyToon Opaque Forward dedicated root-resource guard; Toon | 1 | 2 | 15 |
| **Total successful matrix executions** | **822** | **1656** | **16/16** |

These count executed configurations/stages, including repeated inactive Gem/shell runtime checks in the Disabled supplement, not unique Unity keyword variants. Initial smoke checks and interrupted/failed test-harness experiments are excluded. The final matrix can run all twelve dissolve cases in one invocation; the recorded Transparent run was split into eleven cases plus Disabled. Test-only specialization inserts literal uniform values after CBUFFER_END in memory, preserving actual property layouts and source assets. No new shader keywords are generated.

All per-shader UnityPerMaterial layouts and compiled interfaces passed. Maximum resources were **62/128 textures and 16/16 samplers**; VS/GS stayed at most 1 texture/1 sampler. Runtime paths compile alongside specialized mode 2/3 and synthetic layer paths. Resource assertions reject dissolve textures when disabled, mask textures in position modes, noise textures in no-noise cases, all dissolve textures in Gem/Fur shells, and root dissolve textures in Opaque. Auxiliary TEXCOORD12 transport is checked against VS/PS signatures; forward/Gem/shell paths add none. The no-layers/no-outline matrix specifically exercises raw OS coverage and body motion without relying on existing layer geometry. Existing shader keyword counts did not change.

Reproduce the recorded coverage without Unity (the final no-filter DissolveMatrix also includes Disabled):

~~~powershell
node --test Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs Packages/com.nw.fairytoon/Tests~/VerifyOfflineTools.cjs
$cases = 'Runtime','Mask-Noise0','UVRadial-Noise0','UVDirectional-Noise0','ObjectRadial-Noise0','ObjectDirectional-Noise0','Mask-Noise1','UVRadial-Noise1','UVDirectional-Noise1','ObjectRadial-Noise1','ObjectDirectional-Noise1'
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Transparent -DissolveMatrix -DissolveCases $cases -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Transparent -DissolveMatrix -DissolveCases Disabled -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,Endfield -Coverage Opaque -DissolveMatrix -DissolveCases $cases -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,Endfield -Coverage Opaque -PassNames Forward,SeeThroughHair -DissolveMatrix -DissolveCases Disabled -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Cutout -NoLayers -NoOutline -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders Gem -Models Toon,Endfield -Coverage Transparent -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon -Coverage Opaque -PassNames Forward -ProbeVolumes
~~~

Changed files, all package-relative: README.md; Editor/FairyToonGUI.cs, FairyToonConverter.cs; Shaders/FairyToon.shader, FairyToonRefraction.shader, FairyToonFur.shader, FairyToonGem.shader, FairyToonInput.hlsl, FairyToonPasses.hlsl, FairyToonCore.hlsl, FairyToonVariants.hlsl; Tests~/VerifyLayers.cjs, CompileShaders.ps1, MermaidParityAudit.md. No commits or material migration were performed.


Cleanup: resolved both targets under the authorized package Tests~ directory before requesting deletion. Automatic approval review rejected the native PowerShell Remove-Item cleanup with "blocked by policy". Tests~/Round3Temp~ therefore retains 10 temporary logs; Tests~/OfflineCompiler~ remains empty. No alternative deletion mechanism or approval bypass was attempted.


## Round 4: Dissolve cost follow-up

2026-11-05. The user reports clean Round 3 compilation on Unity 6000.6.7f1 and matching HeadTrans UV-line fins; these are user results, not a new Codex capture. Package-only follow-up; no Unity, Git, lilToon edits or GirlsExtraction hook. Gem behavior is retained: lilToon's Gem forward omits dissolve while its shadow/depth/motion paths support it; FairyToon deliberately skips both.

- Forward/Outline and layer-coverage inverse transforms now run inside `UNITY_BRANCH if`. The real DX11 coordinate probes verify guarded matrix instructions in runtime shaders and zero matrix reads in specialized modes 0/1/2; replacing the expression with the old ternary is detected.
- Without main layers, DepthOnly/DepthNormals/EarlyZ reconstruct OS only for mode 3 from raw raster XY, device Z and current inverse VP. Raw UV avoids URP's additional `_ScaleBiasRt` texture flip. Existing layer/world position paths remain unchanged. A supplied nonuniform-raster support define retains direct OS transport.
- Shadow/CharacterShadow reuse their existing unused normal output for unbiased OS. Motion reuses current/previous clip Z and UV.z; clip XY/W, UV.xy, authored outline Z bias and exclusion of the motion-only camera offset are preserved.
- SeeThroughMask/FakeShadow store pre-raster-bias clip Z/W in the unused UV.zw components. Their quotient recovers unbiased NDC depth; FakeShadow then adds back the same uniform world translation used by its vertex program. No TEXCOORD12 remains in ordinary DX11 passes, including runtime mode-0 materials. Shadow, motion and biased see-through still use previously spare components in runtime programs; this is not a claim of identical pre-Round-3 scalar transport/instruction counts. No material keyword, sampler or CBUFFER property was added.

Measured DX11 packed **user float4 registers**, excluding rasterizer system values (SV_POSITION/front face). ?VS / PS? means declared VS output registers / actually used PS input registers. Runtime includes both active and inactive uniform branches; setting mode 0 in a material does not change that interface. The Disabled column is an offline literal-uniform specialization, not a new Unity variant. Before = saved Round 3 sources, after = Round 4; FairyToon/Toon/Cutout, main/emission layers off, outline off, ProbeVolumes L1.

| Pass | Runtime VS / PS before | Runtime VS / PS after | Disabled PS before ? after |
|---|---:|---:|---:|
| Forward / SeeThroughHair | 4 / 4 | 4 / 4 | 4 ? 4 |
| Outline | 3 / 3 | 3 / 3 | 3 ? 3 |
| ShadowCaster | 4 / 2 | 3 / 2 | 1 ? 1 |
| CharacterShadow | 4 / 2 | 3 / 2 | 1 ? 1 |
| DepthOnly | 4 / 2 | 3 / 1 | 1 ? 1 |
| DepthNormals | 4 / 4 | 3 / 3 | 3 ? 3 |
| MotionVectors | 4 / 4 | 3 / 3 | 3 ? 3 |
| SeeThroughMask | 2 / 2 | 1 / 1 | 1 ? 1 |
| SeeThroughFakeShadow | 2 / 2 | 1 / 1 | 1 ? 1 |
| SeeThroughEarlyZ | 2 / 2 | 1 / 1 | 1 ? 1 |

The separate motion-hull check (outline enabled) also changes runtime VS/PS 4/4 ? 3/3, with Disabled PS unchanged at 3. The counter handles sparse masks such as `xy w`, distinguishes declarations from used inputs, and does not mistake TEXCOORD12 for thirteen registers.

Node: **121/121 passed** (115 VerifyLayers + 6 VerifyOfflineTools), zero failures/skips. New tests execute the coordinate helpers and installed URP reconstruction math for perspective/orthographic, projection flip, jitter, mirrored/nonuniform object transforms, depth mapping and biased see-through; actual motion/clip-depth packing and mutation checks preserve UV/XYW. The Node suite also runs **30/30 DX11 coordinate probes**, detects the old ternary mutation (31 PS programs), and validates one DepthOnly VS/PS setup; these 33 stage programs are separate from the matrix totals below.

All complete DX11 executions below passed, with ProbeVolumes L1, cascaded shadows and Forward+:

| Matrix | Configurations | Stage programs | Max samplers | Max VS / GS / PS user registers |
|---|---:|---:|---:|---:|
| Four shaders, Toon + Endfield, Transparent, full 12-case dissolve matrix, both refraction blur states | 372 | 748 | 16 | 8 / 6 / 6 |
| Four shaders, Toon + Endfield, requested Cutout, no layers/outline, full dissolve matrix | 372 | 748 | 14 | 6 / 4 / 4 |
| FairyToon, Toon + Endfield, Opaque, full dissolve matrix | 57 | 114 | 15 | 6 / 0 / 6 |
| Final Mask/FakeShadow rechecks: Cutout no-layers, Transparent no-layers/layers, Opaque; plus runtime/Disabled motion-hull comparison | 76 | 152 | 3 | 4 / 0 / 4 |
| **Successful executions** | **877** | **1762** | **16/16** | **8 / 6 / 6** |

VS/GS counts are declared packed registers; PS counts are actually used registers. Maximum textures: **62/128**. All material layouts and stage interfaces passed. Counts include repeated configurations in the final rechecks, not unique Unity variants; those rechecks supersede earlier Mask/FakeShadow entries after the final clip-depth packing change. Interrupted runs, baseline measurements and smoke experiments are excluded. Shader keyword declarations/counts are unchanged.

Reproduce from project root (no Unity):

~~~powershell
node --test Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs Packages/com.nw.fairytoon/Tests~/VerifyOfflineTools.cjs
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Transparent -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Cutout -NoLayers -NoOutline -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,Endfield -Coverage Opaque -DissolveMatrix -ProbeVolumes
# Final affected-pass checks and motion comparison:
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,Endfield -Coverage Cutout -NoLayers -NoOutline -PassNames SeeThroughMask,SeeThroughFakeShadow -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,Endfield -Coverage Transparent -NoLayers -NoOutline -PassNames SeeThroughMask,SeeThroughFakeShadow -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,Endfield -Coverage Transparent -PassNames SeeThroughMask,SeeThroughFakeShadow -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,Endfield -Coverage Opaque -PassNames SeeThroughMask,SeeThroughFakeShadow -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon -Coverage Cutout -PassNames MotionVectors -DissolveMatrix -DissolveCases Runtime,Disabled -ProbeVolumes
~~~

Not run: Unity import/ShaderLab or Editor C# compilation on 6000.6.0f1 or 6000.6.7f1, play mode, GPU timing, new screenshots, Vulkan or XR rendering. Offline compilation uses installed URP 17.6.0 headers with UNITY_VERSION=600060; it is not an Editor compatibility result. Reconstruction introduces normal FP32 inverse-projection precision limits near dissolve thresholds; numeric projection tests and compiled interfaces do not replace fixed-time visual/skinned-render checks.

Changed files (package-relative): Shaders/FairyToon.shader, FairyToonInput.hlsl, FairyToonPasses.hlsl, FairyToonSeeThrough.hlsl; Tests~/CompileShaders.ps1, VerifyLayers.cjs, VerifyOfflineTools.cjs, VerifyDissolveCost.ps1, MermaidParityAudit.md.

Cleanup: automatic approval review rejected the guarded native PowerShell Remove-Item request before execution with "blocked by policy". A subsequent read-only path check confirmed Tests~/Round4Temp~ and Tests~/OfflineCompiler~ remain inside the package. Temporary logs/baselines are retained; no alternate deletion mechanism or approval bypass was attempted.


## Round 5: Camera-independent dissolve coordinates

2026-11-05 to 2026-11-06 (JST). Continued the interrupted Round 5 edits against the saved Round 4 baseline. The user reports clean Round 4 compilation on Unity 6000.6.7f1 and unchanged Mermaid/Silent Code captures from Round 3; no new render is claimed here. Package-only work, with no Unity, Git, lilToon edits or GirlsExtraction hook.

- Removed all raster/inverse-VP dissolve reconstruction, including DepthNormals, EarlyZ, Mask and FakeShadow. DepthOnly's mismatched backbuffer VP/I_VP can no longer mirror the mode-3 cut. DepthOnly reuses its unused normal output; DepthNormals packs OS.xy in UV.zw and OS.z in normal.w, preserving UV.xy, normal.xyz and tangent.xyzw.
- Mask/FakeShadow/EarlyZ have only two spare scalars, so they directly carry the original post-skinning OS in a second register. FakeShadow's clip translation and raster depth bias never modify that payload. Layered passes keep their existing interpolated world-position path and guarded world-to-object transform, independent of target orientation, viewport origin and render scale.
- Removed the ineffective nonuniform-foveation guard. This supersedes Round 4's reconstruction/foveated-fallback claim and inverse-projection precision caveat; it does not establish XR rendering support. No new keyword, sampler, material property or pass was added. Motion packing now explicitly forbids Application SpaceWarp's Z-reading helper; tests inspect the installed ordinary/ASW helpers and the actual motion fragment call.

Measured DX11 user float4 registers, excluding rasterizer system values, with the same Round 4 settings: FairyToon/Toon/Cutout, main/emission layers off, outline off, ProbeVolumes L1. VS = declared outputs; PS = actually used inputs. Runtime interfaces also apply to materials set to mode 0; unchanged register counts do not imply unchanged scalar occupancy (DepthNormals uses three previously spare components). Disabled is an offline literal-uniform specialization, not a Unity variant.

| Pass | Round 4 runtime VS / PS | Round 5 runtime VS / PS | Disabled PS, R4 -> R5 |
|---|---:|---:|---:|
| Forward / SeeThroughHair | 4 / 4 | 4 / 4 | 4 -> 4 |
| Outline | 3 / 3 | 3 / 3 | 3 -> 3 |
| ShadowCaster / CharacterShadow | 3 / 2 | 3 / 2 | 1 -> 1 |
| DepthOnly | 3 / 1 | 3 / 2 | 1 -> 1 |
| DepthNormals | 3 / 3 | 3 / 3 | 3 -> 3 |
| MotionVectors | 3 / 3 | 3 / 3 | 3 -> 3 |
| SeeThroughMask | 1 / 1 | 2 / 2 | 1 -> 1 |
| SeeThroughFakeShadow | 1 / 1 | 2 / 2 | 1 -> 1 |
| SeeThroughEarlyZ | 1 / 1 | 2 / 2 | 1 -> 1 |

Node: **123/123 passed** (117 VerifyLayers + 6 VerifyOfflineTools), zero failures/skips. Regressions execute the actual transport macros across perspective/orthographic projection, Y flip, jitter, viewport origin, render scale, mirrored/nonuniform object transforms and fake-shadow displacement; a backbuffer mismatch fixture demonstrates the old mirrored height cut and rejects a mirrored OS mutation. They also preserve normal-map/UV inputs, motion XY/W and the guarded world-to-object path. The suite includes **55/55 DX11 coordinate probes**, the detected old-ternary mutation (56 PS programs total), and a DepthOnly VS/PS setup: **58 stage programs**, separate from the matrix totals below.

Dissolve-off baseline check: all **11/11 FairyToon passes** have identical PS executable declarations/instructions after literal mode-0 specialization (Cutout, no layers/outline, ProbeVolumes L1); only disassembly metadata/comments are excluded. The four-shader Transparent comparison with layers/outline enabled also matches **42/42 PS executables**, including inherently dissolve-free Gem/Fur shell runtime cases. Across both comparisons, **53/53 PS executables** match. Baseline/current comparison builds are excluded from matrix totals. Extra live transport in runtime variants is reported above. This checks executable parity, not captured pixels.

All six DX11 matrix runs completed with exit code 0, ProbeVolumes L1, cascaded shadows and Forward+. Stage interfaces and UnityPerMaterial layouts passed throughout.

| Matrix | Configurations | Stage programs | Max samplers | Max VS / GS / PS user registers |
|---|---:|---:|---:|---:|
| Four shaders, Toon + Endfield, Transparent, layers/outline, full dissolve matrix, both refraction blur states | 372 | 748 | 16 | 8 / 6 / 6 |
| Four shaders, Toon + Endfield, requested Cutout, no layers/outline, full dissolve matrix | 372 | 748 | 14 | 6 / 4 / 4 |
| FairyToon, Toon + Endfield, Opaque, full dissolve matrix | 57 | 114 | 15 | 6 / 0 / 6 |
| Four shaders, Transparent, no layers/outline, five affected depth/see-through passes | 110 | 220 | 3 | 3 / 0 / 3 |
| Four shaders, DepthNormals, no layers/outline, geometric normals | 37 | 74 | 1 | 3 / 0 / 2 |
| Four shaders, DepthNormals, no layers/outline, OCT + Normal2nd UV1-3 | 37 | 74 | 3 | 5 / 0 / 5 |
| **Successful executions** | **985** | **1978** | **16/16** | **8 / 6 / 6** |

Maximum textures: **62/128**. Main matrix maxima match Round 4. Totals count successful configurations, including targeted repeats, not unique Unity variants. They exclude interrupted prior-run logs, baseline/current comparison builds and the 58 Node-associated stage programs. ShaderLab files and keyword declarations are byte-identical to the saved Round 4 baseline.

Reproduce from project root (no Unity):

~~~powershell
node --test Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs Packages/com.nw.fairytoon/Tests~/VerifyOfflineTools.cjs
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Transparent -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Cutout -NoLayers -NoOutline -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,Endfield -Coverage Opaque -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Transparent -NoLayers -NoOutline -PassNames DepthOnly,DepthNormals,SeeThroughMask,SeeThroughFakeShadow,SeeThroughEarlyZ -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Coverage Cutout -NoLayers -NoOutline -PassNames DepthNormals -NoNormalMap -SurfaceFeatures None -DissolveMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Coverage Cutout -NoLayers -NoOutline -PassNames DepthNormals -Normal2ndUV UV1To3 -OctNormals -DissolveMatrix -ProbeVolumes
~~~

Not run: Unity import/ShaderLab or Editor C# compilation, play mode, GPU timing, new screenshots/pixel comparisons, Vulkan or XR rendering. These are offline DX11 results against installed URP 17.6.0 headers with UNITY_VERSION=600060, not a new Unity-version compatibility result or a fixed-time/skinned-render acceptance capture.

Changed files (package-relative): Shaders/FairyToonInput.hlsl, FairyToonPasses.hlsl, FairyToonSeeThrough.hlsl; Tests~/CompileShaders.ps1, VerifyDissolveCost.ps1, VerifyLayers.cjs, VerifyOfflineTools.cjs, MermaidParityAudit.md.

Cleanup: after verifying both resolved targets stay directly inside the package Tests~ directory and contain no reparse points, automatic approval review rejected the native PowerShell Remove-Item action before execution with "blocked by policy". Tests~/Round5Temp~ (logs, baseline and assembly comparisons) and Tests~/OfflineCompiler~ are retained. No alternative deletion mechanism or approval bypass was attempted.

## Round 6: AdultPlayer

2026-11-06 (JST). Package-only repairs from the supplied AdultPlayer captures and real serialized material values. No Unity process, Git command, Assets write, lilToon write, or GirlsExtraction hook port. The images are **pre-change evidence**, not acceptance captures. The user's approximately 40% MAE reduction is their ap1/ap3 SSAO experiment; it was not remeasured after these changes.

### Evidence and material identities

Viewed `Logs/Captures/Morning2/AdultPlayer_face_sunsweep.png`, `AdultPlayer_neck.png`, `AdultPlayer_ssao_test.png`, `ap1/ap_sunsweep_close.png`, ap3 lilToon/FairyToon close and back-light pairs, and Endfield side/top close images. Used `Logs/AdultPlayer_convert.txt`, `ap1/report.txt`, `ap3/report.txt` and `ap2/ap_mdiff.csv` to identify the active inputs. Paths below are project-relative; `L/`, `F/`, `E/` retain the aliases at the top of this audit.

| Surface | Source under `Assets/BOOTH/AdultPlayer/` | Copy GUID directory under `Assets/Lab/Materials/AdultPlayer/{FairyToon,Endfield}/` |
|---|---|---|
| Kitsune_Miko hair | `3_Hair/#MOMORI・｡/Kitsune_Miko/_Material/Hair 1/Lavender.mat` | `244723ffbf064dc448cbb3667bd8a4c3` |
| Asmodeus hair 1 | `3_Hair/#MOMORI・｡/Asmodeus/Material/Hair 1/Snow.mat` | `449e87d19450b2240b53dadb188fcc38` |
| Asmodeus hair 2 | `3_Hair/#MOMORI・｡/Asmodeus/Material/Hair 2/Snow.mat` | `a52ec278a5c7fd7499627571cd1bd137` |
| Bunny ears | `1_ACC/禁断領域 (KINDAN RYOUIKI)/Ace Bunny/Material/UV3/UV3_Black.mat` | `98a46cef0fb8065419992b1c704d4f60` |

The ear assignment is confirmed by `ap1/report.txt:748`, prefab `Adult 1.prefab:13710` and converter log `:243`. It is **UV3_Black**, not either unrelated Black cloth material. All three hair sources use `Hidden/lilToonTransparentOutline`, queue 2460. The present ear copy has queue 3000 versus source 2450; no queue rule was changed here. The existing converter preserves the effective source queue on reconversion.

### 1. Imported Toon SSAO defaults

lilToon's forward lighting sequence (`L/lil_pass_forward_normal.hlsl:449`) has no matching URP screen-space AO term. FairyToon independently applies `_SSAOLitStrength` in `F/FairyToonCore.hlsl:141`, and its native shader defaults remain ambient 1 / lit 0.2 (`F/FairyToon.shader:144`). Copying shared properties cannot supply a lilToon value for these FairyToon-only controls.

`E/FairyToonConverter.cs:187,201` now writes **0 / 0 for lilToon -> Toon conversions by default**. The final profile-selected shading model is used. Successfully applied explicit profile overrides retain precedence, independently for either AO field; `ApplyProfiles` returns those property names (`:463`). Endfield destinations preserve their current values, including intentionally authored 0 / 0. Native material setup and shader AO defaults are unchanged. Existing Toon copies require reconversion or an explicit manual 0 / 0 change; loading the shader alone does not rewrite material floats.

**Endfield choice and integration limit:** preserve Endfield's authored SSAO behavior, not a global shader bypass or automatic zero-to-default reset. The Lab converter currently creates Endfield *after* package conversion by cloning the Toon copy (`Assets/Lab/Editor/LabPhase2E.cs:472,478`) and then setting `_ShadingModel=1` (`:489`). On a future Lab reconversion, it will therefore inherit the new 0 / 0 unless that caller restores the existing Endfield AO values (or 1 / 0.2 for a new Endfield copy). No package code can distinguish this inherited zero from a deliberate Endfield zero without adding persistent policy state. This caller fix is outside the authorized package-only scope. Current saved Endfield materials were not modified; Endfield AO behavior after Lab reconversion is **not** claimed unchanged.

### 2. Toon hair: active HSVG plus RimShade order

The values below are shared by all three inspected hair sources and retained by the copies (rounding only; a few color components differ below 1e-6):

| Input | Actual value / enabled path | lilToon versus FairyToon disposition |
|---|---|---|
| Main tint | `_Color=(1,1,1,1)` | Preserved; sampled color must be adjusted before this tint. |
| Main HSVG | `(-0.095,0.58,1,1)` | **Previously ignored.** Converter log `:346,361,376` explicitly reported this loss. Source `.mat:718`, copy `:863` retained the serialized vector even when the shader had no property. |
| Adjust mask / gradation | Mask null -> white; gradation null, strength 0 | Mask is now supported using main UV and main sampler, without mask ST. Gradation is inactive here and remains unsupported/reported for other sources. |
| Main2nd/3rd, MatCap1/2, Reflection, Emission | All toggles 0 | Their branches do not contribute to this hair hue. |
| Lighting limits | Min .05, Max 1, Monochrome 0, AsUnlit 0 | `L/lil_common_macro.hlsl:2081` versus `F/FairyToonCore.hlsl:94` already match. |
| Exposure | `_BeforeExposureLimit=10000` | Only used by lilToon's HDRP branch (`L/lil_common_macro.hlsl:2100`); not this URP capture. |
| Shadows | Strength 1; first `(0.751249,0.725490,0.811765,1)`, second `(0.807633,0.792157,0.835294,1)`, third alpha 0; MainStrength .464 | Authored purple-grey colors are preserved. RimShade must not modify the albedo used by the MainStrength term. |
| RimShade | `(0.517463,0.454902,0.592157,0.784314)`, border .899, blur 1 | **Order repaired** as described below. |
| Rim | Color `(5.377066,5.546565,6.339626,1)`, indirect `(3.945077,4.191876,5.886794,1)`, ApplyTransparency 1 | Active, with source-compatible non-native-HDR color flags; retains its source-authored cool hue. |
| Backlight | Color `(7.377214,7.377214,7.377214,1)`, enabled | Shares the color-upload repair in section 3. |
| Outline | Color approximately `(0.237443,0.223529,0.301961,1)`, width .046; texture null, outline HSVG `(0,1,1,1)` | No active outline tone adjustment in these sources; general outline HSVG remains unsupported. |
| Transparent output | Source One with explicit premultiply; FairyToon SrcAlpha with straight RGB | Source `L/lil_pass_forward_normal.hlsl:503` versus `F/FairyToonPasses.hlsl:190`. Existing additive-layer transparency compensation remains; no blend/coverage rewrite was needed. |

**HSVG repair:** `L/lil_common_functions.hlsl:347` applies `pow(abs(rgb),gamma)`, HSV hue offset, saturated S/V scaling, and conversion back to RGB. `L/lil_common_frag.hlsl:331,357` applies that adjustment and mask *before* `_Color`. The installed source pass actually enables this feature (`Shader/ltspass_transparent.shader:680,723`, used by `lts_trans_o.shader:674`). FairyToon now follows those pure formulas at `F/FairyToonInput.hlsl:834,846,857`; both base consumers at `:864,874` use them before layers and coverage. All four shader schemas declare lilToon's Vector `_MainTexHSVG` and white `[NoScaleOffset] _MainColorAdjustMask`; GUI exposes both. No keyword or sampler was added. There is one additional texture binding when adjustment is active.

There is deliberately **no extra sRGB conversion** around HSVG. The identity vector bypass preserves native RGB, including negative/HDR values, and avoids the mask sample. That compatibility path differs from lilToon's always-executed identity correction for HDR values above one (source saturates V); ordinary identity LDR values match. A sampled Lavender source pixel, sRGB `(170,155,203)`, moves to approximately `(177,185,203)` under the authored HSVG when evaluated in linear sampled space; this is a CPU illustration, not a new captured pixel.

**RimShade repair:** source saves layered albedo, shades, composites unlit main-layer portions, then multiplies RimShade (`L/lil_pass_forward_normal.hlsl:445,467,487`; `L/lil_common_frag.hlsl:1118`). FairyToon previously multiplied `s.albedo` first, so its `_ShadowMainStrength=.464` path (`F/FairyToonCore.hlsl:257`) could tint the shadow twice. `F/FairyToonCore.hlsl:738` now leaves Toon albedo intact and multiplies completed diffuse/unlit color once, before Backlight. `FairyToonMainLayersUnlit` (`:676`) passes unit tint for Toon so partial/unlit Normal/Add/Screen layers are not darkened twice. Endfield retains its previous pre-light albedo and layer tint; independent Fur shell order is unchanged. Post-light layers continue to consume saved `emissionAlbedo`. This supersedes Round 2's deliberately deferred Toon RimShade-order caveat.

### 3. Bunny ear highlight: Backlight color upload, not Reflection

UV3_Black has `_UseReflection=0`, `_UseBacklight=1`, Backlight Directivity 12, MainStrength .6, NormalStrength 1, ReceiveShadow 1, and RGB **7.377215** with alpha 1 (source `:685`, Toon copy `:820`, Endfield copy `:821`). The white upper-ear patch under 180/35 light has a concrete active Backlight defect; direct Reflection specular is disabled.

Source `Shader/lts_trans_o.shader:159` declares `[lilHDR] _BacklightColor`, a gamma-authored Color without Unity's native HDR flag (`Editor/lilToonPropertyDrawer.cs:12`; `lilOptimizer.cs:242,294`). FairyToon's four schemas used native `[HDR]`, despite preserving the same serialized color. Linear upload semantics therefore differed before the otherwise matching Backlight calculation (`L/lil_common_frag.hlsl:1244,1260` versus `F/FairyToonCore.hlsl:701,718,719`). Using the existing test's >1 approximation gives approximately 81.164 versus 7.377, an approximately 11x input difference; this is **not a measurement of native Unity upload or pixels**.

All four `_BacklightColor` declarations now use the existing `[FairyToonHDR]` drawer (`F/FairyToon.shader:247`, same property in Refraction/Fur/Gem). No manual HLSL decode, material-value rewrite, or unrelated HDR-flag change was added. MatCap and Rim color flags already match for the ear. Fresh fixed-camera back-light captures and Backlight-off ablation remain necessary to establish the rendered contribution and any remaining sorting/lighting differences.

### 4. Dissolve warning

`F/FairyToonInput.hlsl:634` now initializes a local zero OS result, assigns it inside the same uniform `UNITY_BRANCH`, and returns once. The prior early returns covered both paths mathematically, but triggered the supplied D3D12 potentially-uninitialized warning during compiler lowering. No coordinate, mode guard, varying, keyword or matrix-work placement changed. Existing numerical and DX11 branch-cost probes still verify zero matrix reads for inactive modes and guarded world-to-object work for the active position mode. This is a source-level warning repair; **the D3D12 warning's disappearance is not verified without the prohibited Unity/D3D12 rerun**. The separate pre-existing BackfaceColor warnings in ap1 were not altered.

### 5. Endfield orange/purple fringe: Lab misclassification remains outside scope

`ap1/report.txt:92–94` and `ap3/report.txt:92–94` show Lavender and both Snow materials with **`_EFPart=0` (Cloth)**, no `_FT_EF_HAIR`, no HairMask and no SkinLUT. `Assets/Lab/Editor/LabSilentCode.cs:431–437` identifies hair only when the material name contains `hair`; these color names fall through. Although `:345` passes `neutralHair:true`, the Hair-only branch in `LabPhase2E.cs:521` is never entered. Current `_EFHairRootAO=0` and `_EFHairColor2.a=0` further exclude Mayo root/second-band tuning as the active cause.

The actual `_DiffuseRamp` (`.mat:101–102`, GUID `f60e8ef3de6a19c409a38125d0f71298`) resolves to `Assets/Lab/Textures/Endfield/Ramp_Cloth_RD.png`. Its authored keys in `E/FairyToonTextureTools.cs:45–49` include purple `(0.62,.58,.92)` and orange `(1,.72,.60)` at U=.47. `F/FairyToonEndfield.hlsl:337,361,440–444` samples this by N.L and applies its hue. This explains the sun-dependent orange/purple pattern; it is **the intentional Cloth ramp applied to misclassified hair**, not evidence of an intended Endfield hair style. HSVG now corrects the base texture in Endfield too, but does not neutralize this wrong ramp.

The proper follow-up is in the forbidden Assets scope: classify these three proven GUIDs/renderer slots as hair, preserve source queue 2460, and choose genuinely neutral resources for `neutralHair`. Merely changing `part=3` is insufficient: `LabPhase2E.cs:367–377,492,544,592` still chooses warm hair ramps/specular/LUT, and AdultPlayer placement (`LabSilentCode.cs:496–530`) has no head-frame provider, triggering the hair shader's object-origin fallback (`F/FairyToonEndfield.hlsl:175–187`). Do not infer every asset in `3_Hair` is hair (accessories also live there). No global Cloth-ramp desaturation or model-name hack was added to FairyToon. This classification/resource repair and its side/top re-captures remain outstanding.

### Existing material behavior and remaining acceptance

- Reconvert Silent Code and Mermaid Toon copies to receive the new SSAO defaults. Explicit conversion-profile AO values still win. Saved materials were not rewritten in this round.
- HSVG, Toon RimShade order and Backlight color flags change shader interpretation immediately for existing copies; reconversion is not required for already serialized HSVG. This can also change non-target materials with these features enabled. Endfield diffuse/RimShade/SSAO algorithms were preserved, but shared HSVG/Backlight corrections affect Endfield surface inputs.
- **Mayo SSAO defaults/setup are unaffected; Mayo's entire rendered appearance is not guaranteed unchanged.** Existing Mayo Hair_Back/Hair_Front/Kemomimi and their Endfield `_Authored` copies have saved HSVG `(0,.9,1,1)` that now takes effect. Mayo Dress/Shoes/Stocking/Underwear have enabled Backlight with values around `(.698,.725,.851)`, so corrected linear upload darkens that contribution. New native identity-HSVG materials retain the previous base-color path. Verify these existing native looks in later captures.
- Not run: Unity import, ShaderLab/Editor C# compilation, native HDR-property upload, D3D12 warning check, play mode, GPU timings, new screenshots/MAE, Vulkan or XR. Offline shader checks use installed URP 17.6.0 headers and `UNITY_VERSION=600060`; this is not a new successful Unity 6000.6.0f1 editor run.

### Round 6 offline verification

Final Node result: **131/131 passed** (120 VerifyLayers + 7 VerifyOfflineTools + 4 VerifyAdultPlayer), zero failures/skips. Node uses actual helper execution/constant DX11 probes in addition to source contracts. The expanded matrix exposes `-MainTone Runtime|Identity|AdultPlayer` (literal uniform specialization, not Unity variants) and `-NoShadowRamp`; the latter checks reflected `_ShadowColorTex` in the real Toon forward program so the authored color-shadow branch cannot silently fall back to the ramp-only matrix.

The new HSVG probes cover **66 cases / 135 constant PS programs** against the actual installed lilToon helper, plus a real DepthOnly VS/PS setup: **137 stage programs**. They test real hair HSVG, hue wrapping, S/V clamps, gamma exponent, black/white/HDR inputs, masks 0/.5/1/UV-dependent, alpha, tint order and the identity compatibility branch. Missing-adjustment and tint-first mutations are detected. Existing dissolve tests still pass **55 coordinate/cost probes**, the original ternary mutation and a DepthOnly VS/PS setup (**58 stage programs**). These **195 Node-associated stage programs** are separate from the matrix total below. Converter regressions cover Toon defaults, Endfield preserved values, and independent explicit profile overrides; RimShade tests execute Toon/Endfield shade order and both unlit layers across all four blend modes. Backlight tests compare all four native property flags with lilToon, Gamma/Linear upload models and alpha, without double decode.

All six final matrix invocations exited 0. Every row uses **ProbeVolumes L1**, installed URP headers, reflected stage-interface checks and per-shader UnityPerMaterial layout checks. Identity cases reject any compiled adjustment-mask binding; AdultPlayer forward cases require it. Color-shadow cases require the actual `_ShadowColorTex` binding.

| Matrix | Configurations | Stage programs | Max textures / samplers |
|---|---:|---:|---:|
| Four shaders, Toon + Endfield, Transparent, full features, both refraction blur states | 42 | 88 | 63 / 16 |
| Four shaders, Toon + Endfield, requested Cutout, no layers/outline (special modes retain their actual coverage) | 42 | 88 | 50 / 14 |
| FairyToon, Toon + Endfield, Opaque, color shadows | 13 | 26 | 63 / 15 |
| FairyToon forward, Toon + Endfield + EndfieldHair, Transparent, real AdultPlayer HSVG, no layers/reflection/ramp, three normal/MatCap configurations | 9 | 18 | 44 / 13 |
| FairyToon forward, Toon + EndfieldHair, Transparent, identity HSVG, color shadows, Screen shadows with Forward / ForwardPlusAtlas | 4 | 8 | 41 / 9 |
| FairyToon forward, Toon + EndfieldHair, Transparent, real AdultPlayer HSVG, color shadows, Gamma | 2 | 4 | 42 / 9 |
| **Successful final executions** | **112** | **232** | **63/128 textures, 16/16 samplers** |

Four shader pass/pragma declarations are byte-identical to the Round 6 starting snapshot: no keyword dimension or pass increase. HSVG adds one Vector material uniform and one texture declaration, reusing the main sampler. An initial full Transparent dissolve Cartesian run was intentionally interrupted; its partial output is excluded from these successful totals. The full Round 5 Cartesian matrix was not repeated; runtime dissolve paths are included above and the dedicated coordinate/branch tests passed. No post-change rendered parity result is claimed.

Reproduce from project root (no Unity):

~~~powershell
node --test Packages/com.nw.fairytoon/Tests~/VerifyLayers.cjs Packages/com.nw.fairytoon/Tests~/VerifyOfflineTools.cjs Packages/com.nw.fairytoon/Tests~/VerifyAdultPlayer.cjs
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Transparent -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Models Toon,Endfield -Coverage Cutout -NoLayers -NoOutline -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,Endfield -Coverage Opaque -NoShadowRamp -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,Endfield,EndfieldHair -Coverage Transparent -OnlyForward -NoLayers -NoReflection -NoShadowRamp -MainTone AdultPlayer -MermaidMatrix -ProbeVolumes
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,EndfieldHair -Coverage Transparent -OnlyForward -NoLayers -NoReflection -NoShadowRamp -MainTone Identity -ProbeVolumes -MainLightShadows Screen -LightingPaths Forward,ForwardPlusAtlas
& Packages/com.nw.fairytoon/Tests~/CompileShaders.ps1 -Shaders FairyToon -Models Toon,EndfieldHair -Coverage Transparent -OnlyForward -NoLayers -NoReflection -NoShadowRamp -MainTone AdultPlayer -ColorSpace Gamma -ProbeVolumes
~~~

Changed files (14, package-relative): `Editor/FairyToonConverter.cs`, `Editor/FairyToonGUI.cs`; `Shaders/FairyToon.shader`, `FairyToonRefraction.shader`, `FairyToonFur.shader`, `FairyToonGem.shader`, `FairyToonInput.hlsl`, `FairyToonCore.hlsl`; `Tests~/CompileShaders.ps1`, `VerifyLayers.cjs`, `VerifyOfflineTools.cjs`, new `VerifyAdultPlayer.cjs`, new `VerifyAdultPlayer.ps1`, and `MermaidParityAudit.md`.

Cleanup: both resolved targets were verified to be direct children of the package `Tests~` directory with no reparse points (Round6Temp~: 118 entries including the root; OfflineCompiler~: empty). Automatic approval review rejected native PowerShell `Remove-Item -LiteralPath ... -Recurse -Force` before execution with **"blocked by policy"**. `Tests~/Round6Temp~` (starting snapshot and validation logs) and `Tests~/OfflineCompiler~` are retained. No alternate deletion method or approval bypass was attempted.
