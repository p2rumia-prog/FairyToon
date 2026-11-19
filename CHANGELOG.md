# Changelog

## 0.2.1

- Inspector only. Advanced / Preset / Shader Setting replace Simple / Advanced. Search matches drawn rows, including mode popups and the render queue. Main Color 1st and Alpha Mask have their own gears, and Anisotropy sits inside Normal Map. The clipboard accumulates by property name, uses the inspector's MaterialProperty objects, and migrates the coverage queue when Base paste or reset changes the main-shader mode. Language can follow the editor. No shader or material-default change.

## 0.2.0 (2026-11-17)

- Endfield: **Exposure** on `FairyToonCharacter` (default 1) scales the normalised main light without changing the sun / shade state, plus **Follow Light Intensity** and **Reference Luminance** so a dim light gives a dim key. Global `_FT_EFExposure`.
- Endfield: **ambient cap** on `FairyToonCharacter` (off by default): Ambient Max Luminance, Ambient Saturation and Light Ceiling, so a strongly coloured scene probe cannot wash a character out. Global `_FT_EFAmbientCap`.
- Defaults and materials without `FairyToonCharacter` render as in 0.1.0. No material, keyword or sampler change.

## 0.1.0

- As developed in LiminalNW (`Packages/com.nw.fairytoon`, LiminalNW 106c8bd). That history stays in LiminalNW; this repository starts from it.
