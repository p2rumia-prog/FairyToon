# Changelog

## 0.2.1

- Inspector layout follows lilToon 2.4.1 where the shader has the property: version and help foldouts, rendering mode under search, feature toggles inside the section, nested shadow and rendering boxes, MatCap order, and blend-mode labels. Sections the shader does not implement stay absent. FairyToon-only sections stay.
- Inspector only. Advanced / Preset / Shader Setting replace Simple / Advanced. Search matches the drawn label and tooltip, not only the property name. Main Color 1st is a label and gear; Color Adjust shows the mask, then hue / saturation / value / gamma, with a reset. Alpha Mask puts its gear on the Mode row, including on Refraction, and shows only a warning on Opaque. Scalar reads follow ShaderLab: legacy `Int` is a float, `Integer` is a real int. Paste and Reset do nothing when the values already match. Keyword sync runs when a property write hits the asset, including animation mode with no callback, and is skipped only when `applyPropertyCallback` returns true. Texture scale still copies to the first target and can be undone. Rendering uses Unity's queue, instancing, and double-sided GI controls. Language can follow the editor. No shader or material-default change.

## 0.2.0 (2026-11-17)

- Endfield: **Exposure** on `FairyToonCharacter` (default 1) scales the normalised main light without changing the sun / shade state, plus **Follow Light Intensity** and **Reference Luminance** so a dim light gives a dim key. Global `_FT_EFExposure`.
- Endfield: **ambient cap** on `FairyToonCharacter` (off by default): Ambient Max Luminance, Ambient Saturation and Light Ceiling, so a strongly coloured scene probe cannot wash a character out. Global `_FT_EFAmbientCap`.
- Defaults and materials without `FairyToonCharacter` render as in 0.1.0. No material, keyword or sampler change.

## 0.1.0

- As developed in LiminalNW (`Packages/com.nw.fairytoon`, LiminalNW 106c8bd). That history stays in LiminalNW; this repository starts from it.
