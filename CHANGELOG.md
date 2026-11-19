# Changelog

## 0.2.1

- Inspector only. Advanced / Preset / Shader Setting replace Simple / Advanced, with lilToon's search field and parent/child copy blocks. Labels go through English, Japanese, and Simplified Chinese. The clipboard accumulates by property name the way lilToon does. No shader or material-default change.

## 0.2.0 (2026-11-17)

- Endfield: **Exposure** on `FairyToonCharacter` (default 1) scales the normalised main light without changing the sun / shade state, plus **Follow Light Intensity** and **Reference Luminance** so a dim light gives a dim key. Global `_FT_EFExposure`.
- Endfield: **ambient cap** on `FairyToonCharacter` (off by default): Ambient Max Luminance, Ambient Saturation and Light Ceiling, so a strongly coloured scene probe cannot wash a character out. Global `_FT_EFAmbientCap`.
- Defaults and materials without `FairyToonCharacter` render as in 0.1.0. No material, keyword or sampler change.

## 0.1.0

- As developed in LiminalNW (`Packages/com.nw.fairytoon`, LiminalNW 106c8bd). That history stays in LiminalNW; this repository starts from it.
