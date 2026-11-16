// AdultPlayer hair regressions. Never starts Unity.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { test } = require('node:test');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8').replace(/\r\n/g, '\n');
const input = read('Shaders/FairyToonInput.hlsl');

test('four shaders expose source Vector HSVG and white adjustment mask without a new sampler', () => {
    for (const suffix of ['', 'Fur', 'Gem', 'Refraction']) {
        const shader = read('Shaders/FairyToon' + suffix + '.shader');
        assert.match(shader, /_MainTexHSVG\s*\("[^"]*",\s*Vector\)\s*=\s*\(0,\s*1,\s*1,\s*1\)/);
        assert.match(shader, /\[NoScaleOffset\]\s*_MainColorAdjustMask\s*\("[^"]*",\s*2D\)\s*=\s*"white"/);
    }
    assert.match(input, /float4\s+_MainTexHSVG\s*;/);
    assert.match(input, /TEXTURE2D\(_MainColorAdjustMask\)/);
    assert.doesNotMatch(input, /SAMPLER\(sampler_MainColorAdjustMask\)|_MainColorAdjustMask_ST/);
});

test('both main paths adjust before tint and layers while preserving separate coverage logic', () => {
    assert.match(input, /half4\s+FairyToonSampleMainColor\(float2 uvMain\)/);
    const body = input.slice(input.indexOf('half4 FairyToonSampleMainColor('), input.indexOf('half4 FairyToonBaseColor('));
    assert.ok(body.indexOf('FairyToonMainColorAdjust(') < body.indexOf('* _Color'), 'adjust before tint');
    // Forward body: adjusted. Outline colour / alpha-only clip paths: raw main colour (lilToon applies main tone
    // correction only in lil_pass_forward_normal, never to the outline colour).
    for (const [name, sampler] of [['FairyToonBaseColor', 'FairyToonSampleMainColorRaw'], ['FairyToonLayeredBase', 'FairyToonSampleMainColor']]) {
        const offset = input.indexOf('half4 ' + name + '(');
        const section = input.slice(offset, input.indexOf('\n}', offset));
        assert.match(section, new RegExp('half4 col = ' + sampler + '\\(uvMain\\);'));
        assert.doesNotMatch(section, /SAMPLE_TEXTURE2D\(_MainTex/);
        assert.ok(section.indexOf(sampler + '(') < section.indexOf('FairyToonApplyAlphaMask(') || name === 'FairyToonLayeredBase');
    }
    // Gem forward and Fur shells never run main tone correction (lil_pass_forward_gem.hlsl:144, lil_pass_forward_fur.hlsl:147).
    const sampleBody = input.slice(input.indexOf('half4 FairyToonSampleMainColor('), input.indexOf('half4 FairyToonBaseColor('));
    assert.match(sampleBody, /#if defined\(FAIRYTOON_GEM\) \|\| defined\(FAIRYTOON_FUR_SHELL\)\s*\n\s*return FairyToonSampleMainColorRaw\(uvMain\);/);
    const raw = input.slice(input.indexOf('half4 FairyToonSampleMainColorRaw('), input.indexOf('half4 FairyToonSampleMainColor('));
    assert.doesNotMatch(raw, /FairyToonMainColorAdjust|_MainColorAdjustMask/);
    const gui = read('Editor/FairyToonGUI.cs');
    assert.match(gui, /P\("_MainTexHSVG",\s*"Hue[^"\n]*"\)/);
    assert.match(gui, /Tex\("_MainColorAdjustMask",\s*null,/);
});

test('real Lavender and Snow source/copy fixtures preserve active HSVG and disabled alternative layers', () => {
    const base = '../../Assets/BOOTH/AdultPlayer/3_Hair/#MOMORI｡/';
    for (const [src, guid, name] of [
        ['Kitsune_Miko/_Material/Hair 1/Lavender.mat', '244723ffbf064dc448cbb3667bd8a4c3', 'Lavender'],
        ['Asmodeus/Material/Hair 1/Snow.mat', '449e87d19450b2240b53dadb188fcc38', 'Snow'],
        ['Asmodeus/Material/Hair 2/Snow.mat', 'a52ec278a5c7fd7499627571cd1bd137', 'Snow'],
    ]) {
        for (const material of [read(base + src), read('../../Assets/Lab/Materials/AdultPlayer/FairyToon/' + guid + '/' + name + '.mat')]) {
            assert.match(material, /_MainTexHSVG: \{r: -0\.095, g: 0\.58, b: 1, a: 1\}/);
            assert.match(material, /_MainColorAdjustMask:\n\s+m_Texture: \{fileID: 0\}/);
            for (const property of ['_MainGradationStrength', '_UseMain2ndTex', '_UseMain3rdTex', '_UseMatCap', '_UseMatCap2nd'])
                assert.match(material, new RegExp(property + ': 0\\n'));
        }
    }
});

test('compiled actual HSVG and sampling match source, mask/tint order, alpha and native HDR identity', () => {
    const output = execFileSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(__dirname, 'VerifyAdultPlayer.ps1')],
        { encoding: 'utf8', timeout: 120000, maxBuffer: 1024 * 1024 });
    assert.match(output, /AdultPlayer HSVG probes: \d+ passed; missing-adjustment and wrong-order mutations detected/);
});
