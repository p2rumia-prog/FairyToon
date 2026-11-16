# Actual FairyToon and installed lilToon HLSL bodies with frozen samples, compiled by DX11.
# Constant output checks are not native texture/color uploads or Unity rendering.
$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path $PSScriptRoot -Parent
$inputText = [IO.File]::ReadAllText((Join-Path $packageRoot 'Shaders/FairyToonInput.hlsl'))
$sourceText = [IO.File]::ReadAllText((Join-Path $packageRoot '../jp.lilxyzw.liltoon/Shader/Includes/lil_common_functions.hlsl'))
function Get-HlslFunction([string]$text, [string]$name) {
    $match = [regex]::Match($text, '(?m)^(?:half|float)[234]?\s+' + $name + '\([^\r\n]*\)\s*\{')
    if (!$match.Success) { throw ('Missing actual HLSL helper: ' + $name) }
    $cursor = $match.Index + $match.Length; $depth = 1
    while ($depth -gt 0 -and $cursor -lt $text.Length) {
        if ($text[$cursor] -eq '{') { $depth++ }
        if ($text[$cursor] -eq '}') { $depth-- }
        $cursor++
    }
    if ($depth -ne 0) { throw ('Incomplete HLSL helper: ' + $name) }
    return $text.Substring($match.Index, $cursor - $match.Index)
}
$tone = Get-HlslFunction $inputText 'FairyToonToneCorrection'
$adjust = Get-HlslFunction $inputText 'FairyToonMainColorAdjust'
$sample = Get-HlslFunction $inputText 'FairyToonSampleMainColor'
$sourceTone = Get-HlslFunction $sourceText 'lilToneCorrection'
# Reuse compiler bootstrap and check one real depth pass, not a full matrix.
. (Join-Path $PSScriptRoot 'CompileShaders.ps1') -Shaders FairyToon -Models Toon -Coverage Opaque -NoLayers -NoOutline -PassNames DepthOnly -SurfaceFeatures None | Out-Null
function Get-ProbeValue([string]$code) {
    $assembly = [FairyToonOfflineCompiler]::Compile($ProjectRoot, $PackageCache, $code, 'Probe', 'ps_5_0', '')
    $literal = [regex]::Match($assembly, '(?m)^\s*mov o0\.xyzw, l\(([^)]+)\)')
    if (!$literal.Success) { throw ('Probe did not fold to a constant float4: ' + $assembly) }
    return @($literal.Groups[1].Value.Split(',') | ForEach-Object { [double]::Parse($_.Trim(), [Globalization.CultureInfo]::InvariantCulture) })
}
function Assert-Close($actual, $expected, [string]$label) {
    for ($component = 0; $component -lt 4; $component++) {
        if ([Math]::Abs($actual[$component] - $expected[$component]) -gt 0.00001) {
            throw ($label + ': component ' + $component + ', expected ' + $expected[$component] + ', got ' + $actual[$component])
        }
    }
}
function New-Probe([string]$rgb, [string]$hsvg, [string]$mask, [string]$tint, [string]$functionText, [string]$expression, [bool]$gamma) {
    $space = if ($gamma) { '#define UNITY_COLORSPACE_GAMMA 1' } else { '' }
    return @"
$space
static const float4 _MainTexHSVG = float4($hsvg);
static const float4 _Color = float4($tint);
float4 Sample_MainTex(float2 uv) { return float4($rgb, 0.37); }
float4 Sample_MainColorAdjustMask(float2 uv) { return float4($mask, 0, 0, 0); }
#define UNITY_BRANCH [branch]
#define SAMPLE_TEXTURE2D(textureName, samplerName, uv) Sample##textureName(uv)
$tone
$adjust
$functionText
$sourceTone
float4 Probe() : SV_Target { return $expression; }
"@
}
$cases = @(
    @{Name='AdultPlayer linear texture sample'; RGB='0.40197778,0.32777810,0.59720179'; HSVG='-0.095,0.58,1,1'},
    @{Name='Black'; RGB='0,0,0'; HSVG='0.2,0.58,1,1'},
    @{Name='White'; RGB='1,1,1'; HSVG='-0.095,0.58,1,1'},
    @{Name='Greyscale gamma'; RGB='0.2,0.2,0.2'; HSVG='-0.9,3,0.7,2.2'},
    @{Name='Red hue wrap'; RGB='1,0,0'; HSVG='1.25,1,1,1'},
    @{Name='Green saturation clamp'; RGB='0,1,0'; HSVG='-1.7,3,1,1'},
    @{Name='Blue brightness clamp'; RGB='0,0,1'; HSVG='0.1,0.9,2,0.5'},
    @{Name='HDR and negative authored input'; RGB='-0.1,2,0.5'; HSVG='-0.15,0.7,0.9,1.5'}
)
$count = 0
foreach ($case in $cases) {
    foreach ($gamma in @($false, $true)) {
        foreach ($mask in @('0', '0.5', '1', 'uv.x * 0.3 + uv.y * 0.7')) {
            $probeArgs = @($case.RGB, $case.HSVG, $mask, '0.4,0.8,0.2,0.6', $sample)
            $actual = Get-ProbeValue (New-Probe @probeArgs 'FairyToonSampleMainColor(float2(0.2,0.7))' $gamma)
            # Source fragment order: raw sample -> HSVG -> adjustment mask -> tint; alpha is untouched.
            $expectedExpr = 'float4(lerp(Sample_MainTex(float2(0.2,0.7)).rgb, lilToneCorrection(Sample_MainTex(float2(0.2,0.7)).rgb,_MainTexHSVG),Sample_MainColorAdjustMask(float2(0.2,0.7)).r),0.37)*_Color'
            $expected = Get-ProbeValue (New-Probe @probeArgs $expectedExpr $gamma)
            Assert-Close $actual $expected ($case.Name + ', mask ' + $mask + ', gamma ' + $gamma)
            $count++
        }
    }
}
# Native/default HSVG bypasses source V saturation to retain existing HDR/negative RGB.
foreach ($rgb in @('0.1,0.2,0.3', '3,2,-0.5')) {
    $probeArgs = @($rgb, '0,1,1,1', '0.75', '0.4,0.8,0.2,0.6', $sample)
    $actual = Get-ProbeValue (New-Probe @probeArgs 'FairyToonSampleMainColor(float2(0.2,0.7))' $false)
    $expected = Get-ProbeValue (New-Probe @probeArgs 'Sample_MainTex(float2(0.2,0.7))*_Color' $false)
    Assert-Close $actual $expected 'native identity retains HDR and alpha'
    $count++
}
# Reject the original missing adjustment and an incorrect tint-first port.
$fixtureArgs = @($cases[0].RGB, $cases[0].HSVG, '1', '0.4,0.8,0.2,0.6')
$correct = Get-ProbeValue (New-Probe @fixtureArgs $sample 'FairyToonSampleMainColor(float2(0.2,0.7))' $false)
$missing = $sample.Replace('col.rgb = FairyToonMainColorAdjust(col.rgb, uvMain);', '')
$wrongOrder = $sample.Replace('col.rgb = FairyToonMainColorAdjust(col.rgb, uvMain);', 'col *= _Color; col.rgb = FairyToonMainColorAdjust(col.rgb, uvMain);').Replace('return col * _Color;', 'return col;')
foreach ($mutant in @($missing, $wrongOrder)) {
    $mutated = Get-ProbeValue (New-Probe @fixtureArgs $mutant 'FairyToonSampleMainColor(float2(0.2,0.7))' $false)
    $detected = $false
    for ($component = 0; $component -lt 3; $component++) { if ([Math]::Abs($correct[$component] - $mutated[$component]) -gt 0.0001) { $detected = $true } }
    if (!$detected) { throw 'Regression failed to detect a broken adjustment pipeline' }
}
Write-Output ("AdultPlayer HSVG probes: $count passed; missing-adjustment and wrong-order mutations detected (135 constant PS programs); one DepthOnly VS/PS setup passed.")
