# Offline regression for runtime branch placement; never starts Unity.
$ErrorActionPreference = 'Stop'
# Load the real compiler and validate one real pass before the isolated coordinate probes.
. (Join-Path $PSScriptRoot 'CompileShaders.ps1') -Shaders FairyToon -Models Toon -Coverage Cutout -NoLayers -NoOutline -PassNames DepthOnly | Out-Null

function Get-MatrixReads([string]$assembly) {
 $registers=@()
 foreach($buffer in [regex]::Matches($assembly,'(?s)// cbuffer (\w+).*?// }')) {
  $binding=[regex]::Match($assembly,'(?m)^//\s+'+$buffer.Groups[1].Value+'\s+cbuffer\b[^\r\n]*\b(cb\d+)\s+\d+')
  foreach($field in [regex]::Matches($buffer.Value,'float4x4\s+(unity_ObjectToWorld|unity_WorldToObject|unity_MatrixInvVP);\s*// Offset:\s*(\d+)\s+Size:\s*64')) {
   if(!$binding.Success) { continue }
   $first=[int]$field.Groups[2].Value / 16
   foreach($row in 0..3) { $registers+=$binding.Groups[1].Value+'['+($first+$row)+']' }
  }
 }
 $depth=0; $reads=0; $unguarded=0
 foreach($line in ($assembly -split '\r?\n')) {
  if($line -match '^\s*if_(?:nz|z)\b') { $depth++ }
  if($line -match '^\s*endif\b') { $depth-- }
  if($line -match '^\s*(//|dcl_)') { continue }
  foreach($register in $registers) {
   if($line.Contains($register)) { $reads++; if($depth -eq 0) { $unguarded++ } }
  }
 }
 return @{Reads=$reads;Unguarded=$unguarded}
}
$contexts=@(
 @{Name='Forward'; Defines='#define FAIRYTOON_PASS_FORWARD 1'; Matrix=$true},
 @{Name='Outline'; Defines='#define FAIRYTOON_PASS_OUTLINE 1'; Matrix=$true},
 @{Name='LayerCoverage'; Defines='#define FAIRYTOON_PASS_DEPTH 1'; Matrix=$true},
 @{Name='DepthOnly'; Defines=('#define FAIRYTOON_PASS_DEPTH 1'+$nl+'#undef _FT_MAIN_LAYERS')},
 @{Name='DepthNormals'; Defines=('#define FAIRYTOON_PASS_DEPTH 1'+$nl+'#define FAIRYTOON_PASS_DEPTHNORMALS 1'+$nl+'#undef _FT_MAIN_LAYERS')},
 @{Name='Shadow'; Defines=('#define FAIRYTOON_PASS_SHADOWCASTER 1'+$nl+'#undef _FT_MAIN_LAYERS')},
 @{Name='CharacterShadow'; Defines=('#define FAIRYTOON_PASS_CHARSHADOW 1'+$nl+'#undef _FT_MAIN_LAYERS')},
 @{Name='Motion'; Defines=('#define FAIRYTOON_PASS_MOTIONVECTORS 1'+$nl+'#undef _FT_MAIN_LAYERS')},
 @{Name='Mask'; Defines='#undef _FT_MAIN_LAYERS'; Include='FairyToonSeeThrough.hlsl'},
 @{Name='EarlyZ'; Defines=('#undef _FT_MAIN_LAYERS'+$nl+'#define FAIRYTOON_PASS_SEETHROUGH_EARLYZ 1'); Include='FairyToonSeeThrough.hlsl'},
 @{Name='FakeShadow'; Defines=('#undef _FT_MAIN_LAYERS'+$nl+'#define FAIRYTOON_PASS_FAKESHADOW 1'); Include='FairyToonSeeThrough.hlsl'}
)
$probes=0
foreach($context in $contexts) {
 foreach($mode in -1,0,1,2,3) {
  $probeSource=$prefix+$nl+'#define _ALPHATEST_ON 1'+$nl+$context.Defines+$nl+@'
#include "Packages/com.nw.fairytoon/Shaders/FairyToonInput.hlsl"
struct CostInput {
 float4 positionCS : SV_POSITION;
 float3 positionWS : TEXCOORD0;
 float3 layerPosition : TEXCOORD1;
 float4 uv0 : TEXCOORD2;
 #if defined(FT_DISSOLVE_DEPTHNORMALS_PACKED)
 float4 normalWS : TEXCOORD3;
 #else
 float3 normalWS : TEXCOORD3;
 #endif
 float3 dissolvePositionOS : TEXCOORD4;
 float4 positionCSNoJitter : TEXCOORD5;
 float4 previousPositionCSNoJitter : TEXCOORD6;
};
float4 CostProbe(CostInput input) : SV_Target { return float4(FT_DISSOLVE_POSITION_OS(input), 1); }
'@
  if($context.Include) { $probeSource=$probeSource.Replace('FairyToonInput.hlsl',$context.Include) }
  $overrides=Get-DissolveUniformOverrides @{Mode=$mode;Shape=0;Noise=1}
  $assembly=[FairyToonOfflineCompiler]::Compile($ProjectRoot,$PackageCache,$probeSource,'CostProbe','ps_5_0',$overrides)
  $readInfo=Get-MatrixReads $assembly
  if(!$context.Matrix -or $mode -in 0,1,2) {
   if($readInfo.Reads -ne 0) { throw ($context.Name+': packed/direct or inactive position mode still reads a matrix') }
  } else {
   if($readInfo.Reads -eq 0) { throw ($context.Name+': active position probe has no matrix reads') }
   if($mode -eq -1 -and $readInfo.Unguarded -gt 0) { throw ($context.Name+': runtime matrix arithmetic escaped the uniform branch') }
  }
  $probes++
 }
}
# Mutation check: the old ternary must fail the runtime-cost oracle on the real FXC.
$mutant=$prefix+$nl+@'
#define _ALPHATEST_ON 1
#define FAIRYTOON_PASS_FORWARD 1
#include "Packages/com.nw.fairytoon/Shaders/FairyToonInput.hlsl"
float4 CostProbe(float3 positionWS : TEXCOORD0) : SV_Target {
    return float4(FairyToonNeedsDissolvePosition() ? TransformWorldToObject(positionWS) : float3(0,0,0), 1);
}
'@
$mutantAssembly=[FairyToonOfflineCompiler]::Compile($ProjectRoot,$PackageCache,$mutant,'CostProbe','ps_5_0','')
if((Get-MatrixReads $mutantAssembly).Unguarded -eq 0) { throw 'Old ternary did not trigger the cost regression oracle' }
Write-Output ("Dissolve cost probes: $probes/55 passed; old ternary mutation detected (56 PS programs); one DepthOnly VS/PS setup passed.")
