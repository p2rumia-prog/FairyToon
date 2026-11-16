// Source-declared upper bounds, before Unity keyword stripping. Does not start Unity.
'use strict';
const fs = require('node:fs');
const path = require('node:path');

function parseShader(text) {
    if (/\bUsePass\b/.test(text.replace(/\/\/[^\n]*/g, ''))) throw new Error('UsePass is not supported by the variant report');
    const common = [...text.matchAll(/\bHLSLINCLUDE\b([\s\S]*?)\bENDHLSL\b/g)].map(m => m[1]).join('\n');
    return [...text.matchAll(/\bPass\s*\{([\s\S]*?)\bENDHLSL\s*\}/g)].map(([, block]) => ({
        name: block.match(/\bName\s+"([^"]+)"/)?.[1] ?? '(unnamed)',
        stages: ['vertex', 'geometry', 'fragment'].filter(stage => new RegExp('#pragma\\s+' + stage + '\\s+').test(block)),
        source: common + '\n' + block.split(/\bHLSLPROGRAM\b/)[1],
    }));
}

function includeResolver(projectRoot, packageCache) {
    const packages = new Map(fs.readdirSync(packageCache, { withFileTypes: true })
        .filter(entry => entry.isDirectory()).map(entry => [entry.name.split('@')[0], path.join(packageCache, entry.name)]));
    return (name, origin) => {
        let file = name.startsWith('Packages/') ? path.join(projectRoot, name) : path.resolve(path.dirname(origin), name);
        if (!fs.existsSync(file) && name.startsWith('Packages/')) {
            const [, packageName, ...rest] = name.split('/');
            if (packages.has(packageName)) file = path.join(packages.get(packageName), ...rest);
        }
        return { file, source: fs.readFileSync(file, 'utf8') };
    };
}

function countProgram(source, origin, resolveInclude, stages = ['vertex', 'fragment']) {
    const included = new Set();
    const expand = (text, from) => text.replace(/#include_with_pragmas\s+"([^"]+)"/g, (_, name) => {
        const include = resolveInclude(name, from);
        if (included.has(include.file)) return '';
        included.add(include.file);
        return expand(include.source, include.file);
    });
    const expanded = expand(source, origin).replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/[^\n]*/g, '');
    const groups = new Map();
    for (const [, kind, rest] of expanded.matchAll(/^\s*#pragma\s+(shader_feature(?:_local)?(?:_(?:vertex|fragment|geometry))?|multi_compile(?:_(?:vertex|fragment|geometry|fog|instancing))?)\b([^\n]*)/gm)) {
        let choices;
        if (kind === 'multi_compile_fog') choices = 4;
        else if (kind === 'multi_compile_instancing') choices = 2;
        else {
            const options = rest.trim().split(/\s+/).filter(Boolean);
            if (!options.length) continue;
            choices = options.length + (kind.startsWith('shader_feature') && options.length === 1 && !/^_+$/.test(options[0]) ? 1 : 0);
        }
        const stage = kind.match(/_(vertex|fragment|geometry)$/)?.[1];
        groups.set(kind + ' ' + rest.trim(), { kind: kind.startsWith('shader_feature') ? 'material' : 'pipeline', stage, choices });
    }
    const multiply = (kind, stage) => [...groups.values()]
        .filter(group => group.kind === kind && (!stage || !group.stage || group.stage === stage))
        .reduce((count, group) => count * BigInt(group.choices), 1n);
    const material = multiply('material');
    const pipeline = multiply('pipeline');
    return {
        material: material.toString(), pipeline: pipeline.toString(), combined: (material * pipeline).toString(),
        stages: Object.fromEntries(stages.map(stage => {
            const m = multiply('material', stage), p = multiply('pipeline', stage);
            return [stage, { material: m.toString(), pipeline: p.toString(), combined: (m * p).toString() }];
        })),
    };
}

function report(projectRoot, packageCache, shaders) {
    const resolveInclude = includeResolver(projectRoot, packageCache);
    return shaders.map(shader => {
        const file = path.join(projectRoot, 'Packages/com.nw.fairytoon/Shaders', shader === 'FairyToon' ? 'FairyToon.shader' : 'FairyToon' + shader + '.shader');
        const passes = parseShader(fs.readFileSync(file, 'utf8')).map(pass => ({
            name: pass.name, ...countProgram(pass.source, file, resolveInclude, pass.stages),
        }));
        if (!passes.length) throw new Error('No HLSL passes found in ' + file);
        return { shader: shader === 'FairyToon' ? shader : 'FairyToon/' + shader, passes,
            combinedPassSum: passes.reduce((sum, pass) => sum + BigInt(pass.combined), 0n).toString() };
    });
}

if (require.main === module) {
    const args = process.argv.slice(2);
    const arg = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
    const projectRoot = path.resolve(arg('--project-root', path.join(__dirname, '../../..')));
    const packageCache = path.resolve(arg('--package-cache', path.join(projectRoot, 'Library/PackageCache')));
    const shaders = arg('--shaders', 'FairyToon,Refraction,Fur,Gem').split(',');
    const data = report(projectRoot, packageCache, shaders);
    if (args.includes('--json')) console.log(JSON.stringify(data, null, 2));
    else {
        console.log('Variant counts: source upper bounds before Unity stripping; conditional pragma branches included, dynamic branches excluded. Counts are declarations, not the number compiled by the smoke matrix.');
        console.log('The main shader declares three coverage choices. Special shaders can remove common choices; only Refraction Forward adds its blur choice. Runtime scalar selectors do not multiply variants.');
        for (const shader of data) {
            for (const pass of shader.passes) console.log(`${shader.shader}/${pass.name}: material ${pass.material} x pipeline ${pass.pipeline} = ${pass.combined}`);
            console.log(`${shader.shader}: sum across passes ${shader.combinedPassSum}`);
        }
    }
}

module.exports = { parseShader, countProgram, includeResolver, report };
