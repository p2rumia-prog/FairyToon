# Heavy rendering variants implementation plan

Goal: preserve lilToon material settings in separate Refraction, Fur and Gem shaders, with the existing toon and Endfield shading paths. The supplied task is the design and authorization for this package-only implementation.

Architecture: duplicate the main ShaderLab property contract in each separate shader; reuse its HLSL passes and inspector. A default-empty material-buffer macro extends UnityPerMaterial only in the new shaders. Refraction and Gem wrap the shared surface shading. Fur uses lilToon's three URP tags for separate body, fur and optional second fur draws. This avoids adding passes or keyword dimensions to FairyToon.shader. Optical shaders use constant transparent coverage; cheap Endfield fur has no part-specific keyword group.

Constraints: Unity 6000.6.0f1, URP 17.6, PC DX11/Vulkan; no Unity process, Git commit/checkout/reset, or writes outside this package. Read dependencies from the main project's package cache only. Preserve property names/defaults and all texture transforms. Keep stage interfaces conditional, fragment-only keywords scoped to fragments, and every shader's material buffer identical in its own passes.

- [x] Add failing source/behavior contracts against installed lilToon; extend offline compiler with PackageCache and geometry-stage coverage.
- [x] Implement variant properties, shared buffer extension, refraction roughness blur, gem optics, fur geometry and lighting; fresh Unity metadata for imported assets.
- [x] Implement shader routing, mode state, pass enabling, inspector sections and converter mappings, including actual outline/Multi names.
- [x] Run Node contracts and offline shader compilation, review source and report per-pass resources/variant counts and limitations.

Review focus: zero-alpha additive terms; returning to the main shader without value loss; Multi mode and outline routing; two-pass fur overlap and draw tags; material-buffer layout and unused vertex interfaces; texture/sampler budgets with all features enabled; Opaque Texture availability and lack of transparent background capture.

## Review corrections (2026-11-05)

The supplied review is the authorization and specification for this correction pass.

- [x] Add failing state/compositing/lighting regressions in `VerifyLayers.cjs` against installed lilToon.
- [x] Correct Gem PRE/additive passes, Cull Off defaults and value preservation in shader, variant HLSL and material setup.
- [x] Correct Fur body coverage/state/auxiliary passes per mode, PRE alpha-to-coverage and cheap vertex-normal shell lighting; remove unused shell keywords.
- [x] Preserve Refraction ZWrite (default On), unlock inspector editing, exit GUI on shader swaps; document absent smoothness ST support.
- [x] Delete the four requested Tests~ metadata files; update READMEs/report; run Node, offline DX11 and standalone Editor C# checks. No Unity or Git integration.
