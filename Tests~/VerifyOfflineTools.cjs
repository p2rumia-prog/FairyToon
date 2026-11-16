'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { parseShader, countProgram } = require('./ReportVariants.cjs');

test('variant counts separate material groups, pipeline groups and shader stages', () => {
    const source = `
        #pragma shader_feature_local _LAYERS
        #pragma shader_feature_local_fragment _ _NORMAL _NORMAL_UV
        #pragma multi_compile _ _SHADOW
        #pragma multi_compile_instancing
        #pragma multi_compile_fog
        #pragma dynamic_branch _ FOG_LINEAR FOG_EXP FOG_EXP2
    `;
    const actual = countProgram(source, 'test.shader', () => { throw new Error('unexpected include'); }, ['vertex', 'geometry', 'fragment']);
    assert.equal(actual.material, '6');
    assert.equal(actual.pipeline, '16');
    assert.equal(actual.combined, '96');
    assert.deepEqual(actual.stages.vertex, { material: '2', pipeline: '16', combined: '32' });
    assert.deepEqual(actual.stages.geometry, actual.stages.vertex);
    assert.deepEqual(actual.stages.fragment, { material: '6', pipeline: '16', combined: '96' });
});

test('pragma includes are resolved recursively once and plain includes do not add variants', () => {
    const includes = [];
    const files = {
        'Fog.hlsl': '#pragma multi_compile_fog\n#include_with_pragmas "Probes.hlsl"',
        'Probes.hlsl': '#pragma multi_compile _ PROBE_L1 PROBE_L2',
    };
    const actual = countProgram('#include_with_pragmas "Fog.hlsl"\n#include_with_pragmas "Probes.hlsl"\n#include "Ignored.hlsl"', 'test.shader', name => {
        includes.push(name);
        return { file: name, source: files[name] };
    });
    assert.equal(actual.material, '1');
    assert.equal(actual.pipeline, '12');
    assert.deepEqual(includes, ['Fog.hlsl', 'Probes.hlsl', 'Probes.hlsl']);
});

test('ShaderLab parser applies shared keyword declarations to each owned pass', () => {
    const shader = `Shader "Test" { SubShader {
        HLSLINCLUDE
        #pragma shader_feature_local _ _CUTOUT _TRANSPARENT
        ENDHLSL
        Pass { Name "Forward" HLSLPROGRAM
        #pragma vertex Vert
        #pragma geometry Geom
        #pragma fragment Frag
        ENDHLSL }
        Pass { Name "Depth" HLSLPROGRAM
        #pragma vertex DepthVert
        #pragma fragment DepthFrag
        ENDHLSL }
    } }`;
    const passes = parseShader(shader);
    assert.deepEqual(passes.map(pass => pass.name), ['Forward', 'Depth']);
    assert.deepEqual(passes[0].stages, ['vertex', 'geometry', 'fragment']);
    assert.deepEqual(passes[1].stages, ['vertex', 'fragment']);
    for (const pass of passes) assert.equal(countProgram(pass.source, 'test.shader', () => {}).material, '3');
    assert.throws(() => parseShader('UsePass "Other/Forward"'), /UsePass/);
});


test('offline stage setup distinguishes declared atlas-off from atlas-on using installed URP preprocessing', { skip: process.platform !== 'win32' }, () => {
    const fs = require('node:fs');
    const path = require('node:path');
    const { spawnSync } = require('node:child_process');
    const script = fs.readFileSync(path.join(__dirname, 'CompileShaders.ps1'), 'utf8');
    const begin = script.indexOf('function Get-StageProgram(');
    assert.ok(begin >= 0);
    const start = script.indexOf('{', begin);
    let depth = 1, end = start + 1;
    for (; depth && end < script.length; end++) { if (script[end] === '{') depth++; if (script[end] === '}') depth--; }
    assert.equal(depth, 0, 'complete source-builder function');
    // Evaluate only this pure source-builder function: never import Add-Type or run shader compilation.
    const fixture = script.slice(begin, end) + String.raw`
$ErrorActionPreference='Stop'
$nl=[Environment]::NewLine
$results=@(foreach($declared in @($false,$true)) {
 foreach($atlas in @($false,$true)) {
  foreach($stage in @('Vertex','Fragment')) {
   $configuration='#define _CLUSTER_LIGHT_LOOP 1'+$nl
   if($atlas) { $configuration+='#define _REFLECTION_PROBE_ATLAS 1'+$nl }
   $pass=@{Pragmas=$(if($declared){'#pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS'}else{'#pragma vertex Vert'});Program='// unit fixture'}
   [pscustomobject]@{Declared=$declared;Atlas=$atlas;Stage=$stage;Source=(Get-StageProgram $configuration $pass $stage)}
  }
 }
})
ConvertTo-Json -Depth 3 -Compress -InputObject $results
`;
    const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(fixture, 'utf16le').toString('base64')], { encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr || String(result.error));
    const cache = path.resolve(__dirname, '../../../Library/PackageCache');
    const universal = fs.readdirSync(cache).find(name => name.startsWith('com.unity.render-pipelines.universal@'));
    assert.ok(universal, 'installed URP required');
    const clustering = fs.readFileSync(path.join(cache, universal, 'ShaderLibrary/Clustering.hlsl'), 'utf8');
    const expression = clustering.match(/^#define CLUSTER_HAS_REFLECTION_PROBES (.+)$/m)?.[1];
    assert.ok(expression, 'installed URP cluster probe selection');
    const cases = JSON.parse(result.stdout.replace(/^\uFEFF/, ''));
    assert.equal(cases.length, 8);
    for (const entry of cases) {
        const defines = new Set([...entry.Source.matchAll(/^#define\s+(\w+)/gm)].map(m => m[1]));
        assert.equal(defines.has('_REFLECTION_PROBE_ATLAS_KEYWORD_DECLARED'), entry.Declared);
        const enabled = new Function('return ' + expression.replace(/defined\((\w+)\)/g, (_, name) => defines.has(name) ? 'true' : 'false'))();
        assert.equal(enabled, !entry.Declared || entry.Atlas, JSON.stringify(entry));
    }
});


test('interpolator counts include sparse masks and distinguish unused system values', {skip:process.platform!=='win32'}, () => {
    const fs=require('node:fs'),path=require('node:path'),{spawnSync}=require('node:child_process');
    const script=fs.readFileSync(path.join(__dirname,'CompileShaders.ps1'),'utf8');
    const functions=script.slice(script.indexOf('function Get-StageSignature('),script.indexOf('function Test-StageInterface('));
    const fixture=functions+[
        "$assembly=@'",
        '// Input signature:',
        '// Name Index Mask Register SysValue Format Used',
        '// POSITION_CS_NO_JITTER 0 xy w 1 NONE float xy w',
        '// PREV_POSITION_CS_NO_JITTER 0 xyzw 2 NONE float xy w',
        '// TEXCOORD 0 xy 3 NONE float xy',
        '// TEXCOORD 12 xyz 4 NONE float',
        '// SV_POSITION 0 xyzw 0 POS float z',
        '// SV_IsFrontFace 0 x 5 FFACE uint x',
        '// Output signature:',
        "'@",
        "Get-InterpolatorUsage $assembly 'Input'"
    ].join('\n');
    const result=spawnSync('powershell.exe',['-NoProfile','-NonInteractive','-EncodedCommand',Buffer.from(fixture,'utf16le').toString('base64')],{encoding:'utf8'});
    assert.equal(result.status,0,result.stderr);
    assert.equal(result.stdout.trim(),'declared/used 4/3');
});

test('DX11 position arithmetic is branched and detects the original ternary regression', {skip:process.platform!=='win32'}, () => {
    const path=require('node:path'),{spawnSync}=require('node:child_process');
    const result=spawnSync('powershell.exe',['-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',path.join(__dirname,'VerifyDissolveCost.ps1')],{encoding:'utf8',timeout:60000});
    assert.equal(result.status,0,result.stderr||String(result.error));
    assert.match(result.stdout,/55\/55 passed; old ternary mutation detected \(56 PS programs\)/);
});


test('offline main tone cases select runtime, native identity and authored AdultPlayer HSVG', { skip: process.platform !== 'win32' }, () => {
    const fs = require('node:fs'), path = require('node:path');
    const { spawnSync } = require('node:child_process');
    const script = fs.readFileSync(path.join(__dirname, 'CompileShaders.ps1'), 'utf8');
    const helper = script.match(/function Get-MainToneUniformOverrides[^]*?\n}/)?.[0];
    assert.ok(helper, 'matrix exposes actual main-tone specialization');
    const result = spawnSync('powershell.exe', ['-NoProfile','-Command', helper +
        '\n$nl=[Environment]::NewLine; @(foreach($case in @("Runtime","Identity","AdultPlayer")){Get-MainToneUniformOverrides $case}) | ConvertTo-Json -Compress'], {encoding:'utf8'});
    assert.equal(result.status,0,result.stderr);
    const actual=JSON.parse(result.stdout);
    assert.deepEqual(actual.map(x=>x.trim()), ['', '#define _MainTexHSVG float4(0,1,1,1)', '#define _MainTexHSVG float4(-0.095,0.58,1,1)']);
    assert.match(script, /\$uniformOverrides\s*\+=\s*Get-MainToneUniformOverrides \$MainTone/);
});
