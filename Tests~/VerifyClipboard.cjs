// Simulates lilToon's accumulating clipboard against FairyToon's membership table.
// Copy adds/overwrites and does not clear. Paste writes only that block's names already in the dictionary,
// skipping textures unless Paste (with texture).
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const blocksCs = fs.readFileSync(path.join(root, 'Editor/FairyToonPropertyBlocks.cs'), 'utf8');
const clipCs = fs.readFileSync(path.join(root, 'Editor/FairyToonClipboard.cs'), 'utf8');
const errors = [];

if (!/Dictionary<string, MaterialProperty> CopiedProperties/.test(clipCs))
  errors.push('clipboard must be a static Dictionary<string, MaterialProperty> keyed by property name');
if (!/Dictionary<string, Vector4> CopiedVectorProperties/.test(clipCs))
  errors.push('texture _ST must be stored in Dictionary<string, Vector4> CopiedVectorProperties');
const copyFn = clipCs.slice(clipCs.indexOf('void CopyProperties'), clipCs.indexOf('void PasteProperties'));
if (copyFn.includes('.Clear(')) errors.push('CopyProperties must not clear the clipboard');
if (!copyFn.includes('CopiedProperties[entry.Name] = prop')) errors.push('Copy must overwrite by property name');
const pasteFn = clipCs.slice(clipCs.indexOf('void PasteProperties'), clipCs.indexOf('void ResetProperties'));
if (!pasteFn.includes('!shouldCopyTex && entry.IsTexture')) errors.push('Paste must skip textures unless Paste (with texture)');
if (!pasteFn.includes('CopiedVectorProperties')) errors.push('Paste with texture must apply stored _ST');

const table = {};
const re = /new\("(_[^"]+)", (true|false), ((?:PropertyBlock\.\w+,?\s*)+)\)/g;
let m;
while ((m = re.exec(blocksCs))) {
  table[m[1]] = { texture: m[2] === 'true', blocks: [...m[0].matchAll(/PropertyBlock\.(\w+)/g)].map(x => x[1]) };
}

function names(block) {
  return Object.keys(table).filter(n => table[n].blocks.includes(block));
}

// Value id stands in for the stored MaterialProperty. Later Copy of the same name overwrites it.
const clipboard = new Map();
let stamp = 0;
function copy(block, present) {
  for (const name of names(block)) {
    if (!present.has(name)) continue;
    clipboard.set(name, { texture: table[name].texture, stamp: ++stamp, from: block });
  }
}
function paste(block, present, withTex) {
  const written = [];
  for (const name of names(block)) {
    if (!present.has(name)) continue;
    const item = clipboard.get(name);
    if (!item) continue;
    if (!withTex && item.texture) continue;
    written.push(name);
  }
  return written.sort();
}

const matcap = new Set(names('MatCaps'));
const rim = new Set(names('RimLight'));
if (!matcap.has('_MatCapTex') || !table._MatCapTex.texture) errors.push('MatCap texture flag');
if (!rim.has('_RimColor') || table._RimColor.texture) errors.push('Rim color should be a non-texture');
copy('MatCaps', matcap);
copy('RimLight', rim);
if (!clipboard.has('_MatCapColor') || !clipboard.has('_RimColor') || !clipboard.has('_MatCapTex'))
  errors.push('copying Rim after MatCap must keep MatCap entries');
const pastedMat = paste('MatCaps', matcap, false);
const pastedRim = paste('RimLight', rim, false);
if (pastedMat.includes('_MatCapTex')) errors.push('Paste without texture wrote _MatCapTex');
if (!pastedMat.includes('_MatCapColor')) errors.push('Paste MatCap missed _MatCapColor');
if (pastedMat.some(n => rim.has(n) && !matcap.has(n))) errors.push('Paste MatCap wrote a Rim-only property');
if (!pastedRim.includes('_RimColor')) errors.push('Paste Rim missed _RimColor');
if (pastedRim.some(n => matcap.has(n) && !rim.has(n))) errors.push('Paste Rim wrote a MatCap-only property');
const withTex = paste('MatCaps', matcap, true);
if (!withTex.includes('_MatCapTex')) errors.push('Paste with texture skipped _MatCapTex');

// Multi-block property: copying either block stores it; pasting either block writes it; the other block's unique names stay put.
const both = ['_ShadowEnvStrength', '_Metallic', '_RefractionStrength'];
for (const name of both) {
  if (!table[name] || table[name].blocks.length < 2) errors.push(name + ' should belong to two blocks');
}
clipboard.clear();
copy('Shadow', new Set(names('Shadow')));
copy('Lighting', new Set(names('Lighting')));
if (!clipboard.has('_ShadowEnvStrength')) errors.push('multi-block _ShadowEnvStrength was not stored');
const fromShadow = paste('Shadow', new Set(names('Shadow')), false);
const fromLight = paste('Lighting', new Set(names('Lighting')), false);
if (!fromShadow.includes('_ShadowEnvStrength') || !fromLight.includes('_ShadowEnvStrength'))
  errors.push('_ShadowEnvStrength must paste from both Shadow and Lighting');
if (fromLight.includes('_ShadowColor')) errors.push('Lighting paste must not write shadow-only properties');

copy('Reflection', new Set(names('Reflection')));
copy('Gem', new Set(names('Gem')));
const refl = paste('Reflection', new Set(names('Reflection')), false);
const gem = paste('Gem', new Set(names('Gem')), false);
if (!refl.includes('_Metallic') || !gem.includes('_Metallic')) errors.push('_Metallic must paste from Reflection and Gem');
if (refl.includes('_MetallicGlossMap')) errors.push('Reflection paste without texture wrote _MetallicGlossMap');
if (paste('Reflection', new Set(names('Reflection')), true).includes('_MetallicGlossMap') === false)
  errors.push('Reflection paste with texture missed _MetallicGlossMap');

if (names('Fur').includes('_FurMode')) errors.push('Fur block must not include _FurMode');
if (names('Fur').includes('_FurCull')) errors.push('lilToon tags _FurCull as Rendering, not Fur');

if (errors.length) {
  console.error(errors.join('\n'));
  process.exit(1);
}
console.log('clipboard scenario ok', clipboard.size);
