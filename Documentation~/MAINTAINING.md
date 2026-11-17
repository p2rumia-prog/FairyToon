# Maintaining FairyToon

FairyToon is maintained in this repository (private, `projectnwofficial-dev/FairyToon`) since 2026-11-17, by Claude for the owner. It used to live in LiminalNW (`Packages/com.nw.fairytoon`); that copy is no longer the source and nothing is ported back to it.

- The package sits at the repository root, so Unity can use it as a Git package: `"com.nw.fairytoon": "https://github.com/projectnwofficial-dev/FairyToon.git#<commit or tag>"`. Consumers pin a commit or tag, never a moving branch.
- Name `com.nw.fairytoon`, namespace `NW.FairyToon`. It depends only on URP, never on game code, and must compile on Unity 6000.6.
- Every change bumps `package.json` (semver), adds a `CHANGELOG.md` entry and updates the README section it touches. New settings default to the previous output; say so when a change alters an existing look.
- Every file Unity can see needs a committed `.meta` (Git packages are read-only in the consumer, so Unity cannot make them). Folders ending in `~` (like this one and `Tests~`) are ignored by Unity.
- Plain Git only, no Git LFS (the account's LFS budget is used up). Keep textures out of the package or small.
- Work on a branch with a pull request into `main`. Before merging: `node Tests~/VerifyLayers.cjs` run from a checkout that sits in a project's `Packages/` next to `jp.lilxyzw.liltoon` (it reads lilToon's sources; 20 checks that read LiminalNW lab files fail without them, and that set must not grow), `Tests~/CompileShaders.ps1` on Windows, and a compile plus stills in a consumer project (ProjectGE).
- Credits and licences: see the README's Credits section (lilToon MIT and the others named there).
