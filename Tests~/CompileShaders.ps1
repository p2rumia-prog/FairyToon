# Offline DX11 compilation via Windows D3DCompiler. Does not start Unity.
param(
 [string]$ProjectRoot = (Resolve-Path "$PSScriptRoot/../../..").Path,
 [string]$PackageCache = (Join-Path $ProjectRoot 'Library/PackageCache'),
 [ValidateSet('FairyToon','Refraction','Fur','Gem')][string[]]$Shaders = @('FairyToon','Refraction','Fur','Gem'),
 [string[]]$Models = @('Toon','Endfield','EndfieldSkin','EndfieldFace','EndfieldHair','EndfieldEye'),
 [ValidateSet('Opaque','Cutout','Transparent')][string]$Coverage = 'Cutout',
 [switch]$NoLayers,
 [ValidateSet('Both','Main','Emission','None')][string]$LayerFeatures = 'Both',
 [switch]$NoOutline,
 [switch]$ProbeAtlas,
 [ValidateSet('None','Main','Cascade','Screen')][string[]]$MainLightShadows = @('Cascade'),
 [ValidateSet('Forward','ForwardPlus','ForwardPlusAtlas')][string[]]$LightingPaths = @('ForwardPlus'),
 # Cover screen/cascade shadows and legacy, clustered and atlas reflection probes on every forward shader.
 [switch]$ReviewMatrix,
 # Exercise shared Mermaid layers with main normals, custom MatCap-only normals, and MatCaps disabled.
 [switch]$MermaidMatrix,
 # Compile uniform-specialized dissolve branches as well as the unmodified runtime program.
 [switch]$DissolveMatrix,
 [string[]]$DissolveCases,
 [switch]$ProbeVolumes,
 # Literal uniforms exercise the new main-colour branch without adding Unity keywords.
 [ValidateSet('Runtime','Identity','AdultPlayer')][string]$MainTone = 'Runtime',
 [switch]$OnlyForward,
 [string[]]$PassNames,
 [switch]$NoReflection,
 # Wet variants are fragment-only in the main shader colour and outline passes.
 [ValidateSet('Both','Off','On')][string]$Wetness = 'Both',
 # Iris ray-space normals are independent of the Endfield part selector.
 [ValidateSet('Both','Off','On')][string]$Iris = 'Both',
 # Verify runtime layer, legacy fold and surface-depth specialization in every camera pass.
 [switch]$IrisMatrix,
 # Specialize shared optics without adding shader keywords; Runtime keeps the real material uniforms.
 [ValidateSet('Runtime','Defaults','LabDefaults','Stress')][string]$IrisAdjustment = 'Runtime',
 [switch]$NoShadowRamp,
 [ValidateSet('All','None','Normal2nd','Anisotropy','Backlight','Glitter')][string]$SurfaceFeatures = 'All',
 [ValidateSet('UV0','UV1To3')][string]$Normal2ndUV = 'UV0',
 [ValidateSet('UV0','UV1To3')][string]$GlitterUV = 'UV0',
 [switch]$NoNormalMap,
 [ValidateSet('Linear','Gamma')][string]$ColorSpace = 'Linear',
 [switch]$NativeHDR,
 [switch]$OctNormals,
 [ValidateSet('Both','Off','On')][string]$RefractionBlur = 'Both'
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$PackageCache = (Resolve-Path -LiteralPath $PackageCache).Path
$compilerScratch = Join-Path $PSScriptRoot 'OfflineCompiler~'
[void][System.IO.Directory]::CreateDirectory($compilerScratch)
$previousCompilerTemp = $env:TEMP
$previousCompilerTmp = $env:TMP
try {
$env:TEMP = $compilerScratch
$env:TMP = $compilerScratch
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class FairyToonOfflineCompiler {
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int OpenFn(IntPtr self,int kind,IntPtr file,IntPtr parent,out IntPtr data,out uint bytes);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CloseFn(IntPtr self,IntPtr data);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr BlobPtr(IntPtr self);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate UIntPtr BlobSize(IntPtr self);
 [DllImport("d3dcompiler_47.dll")] static extern int D3DCompile(byte[] data,UIntPtr size,string source,IntPtr defs,IntPtr inc,string entry,string target,uint flags,uint flags2,out IntPtr code,out IntPtr errors);
 [DllImport("d3dcompiler_47.dll")] static extern int D3DPreprocess(byte[] data,UIntPtr size,string source,IntPtr defs,IntPtr inc,out IntPtr code,out IntPtr errors);
 [DllImport("d3dcompiler_47.dll")] static extern int D3DDisassemble(IntPtr data,UIntPtr size,uint flags,string comments,out IntPtr result);
 static IntPtr Data(IntPtr b) { return ((BlobPtr)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(b),3*IntPtr.Size),typeof(BlobPtr)))(b); }
 static UIntPtr Size(IntPtr b) { return ((BlobSize)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(b),4*IntPtr.Size),typeof(BlobSize)))(b); }
 static string Text(IntPtr b) { return b==IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(Data(b)); }
 public static string Compile(string root,string packageCache,string source,string entry,string target,string uniformOverrides) {
  var paths=new Dictionary<IntPtr,string>();var packages=new Dictionary<string,string>();
  foreach(string dir in Directory.GetDirectories(packageCache)) packages[Path.GetFileName(dir).Split('@')[0]]=dir;
  OpenFn open=delegate(IntPtr self,int kind,IntPtr filename,IntPtr parent,out IntPtr data,out uint length) {
   data=IntPtr.Zero;length=0;
   try {
    string name=Marshal.PtrToStringAnsi(filename).Replace('/',Path.DirectorySeparatorChar),path;
    if(name.StartsWith("Packages"+Path.DirectorySeparatorChar)) {
     path=Path.Combine(root,name);
     if(!File.Exists(path)) { string[] parts=name.Split(Path.DirectorySeparatorChar); if(packages.ContainsKey(parts[1])) path=Path.Combine(packages[parts[1]],string.Join(Path.DirectorySeparatorChar.ToString(),parts,2,parts.Length-2)); }
    } else path=Path.Combine(paths.ContainsKey(parent) ? Path.GetDirectoryName(paths[parent]) : root,name);
    string includeText=File.ReadAllText(path).Replace("#include_with_pragmas","#include");
    // Test constants follow declarations: the reflected UnityPerMaterial layout remains unchanged.
    // Only this in-memory include changes; no shader asset or keyword is generated.
    if(Path.GetFileName(path)=="FairyToonInput.hlsl" && !string.IsNullOrEmpty(uniformOverrides)) {
     const string marker="CBUFFER_END";
     int end=includeText.IndexOf(marker,StringComparison.Ordinal);
     if(end<0) throw new Exception("Missing material cbuffer for offline uniform specialization");
     includeText=includeText.Insert(end+marker.Length,"\n"+uniformOverrides+"\n");
    }
    byte[] bytes=Encoding.UTF8.GetBytes(includeText);
    data=Marshal.AllocHGlobal(bytes.Length);Marshal.Copy(bytes,0,data,bytes.Length);length=(uint)bytes.Length;paths[data]=path;return 0;
   } catch(Exception e) { Console.Error.WriteLine("Include "+Marshal.PtrToStringAnsi(filename)+": "+e.Message);return unchecked((int)0x80004005); }
  };
  CloseFn close=delegate(IntPtr self,IntPtr data) { paths.Remove(data);Marshal.FreeHGlobal(data);return 0; };
  IntPtr table=Marshal.AllocHGlobal(2*IntPtr.Size),inc=Marshal.AllocHGlobal(IntPtr.Size),code=IntPtr.Zero,errors=IntPtr.Zero,disasm=IntPtr.Zero;
  Marshal.WriteIntPtr(table,0,Marshal.GetFunctionPointerForDelegate(open));Marshal.WriteIntPtr(table,IntPtr.Size,Marshal.GetFunctionPointerForDelegate(close));Marshal.WriteIntPtr(inc,table);
  try {
   byte[] bytes=Encoding.UTF8.GetBytes(source);IntPtr preprocessed, preprocessErrors;
   int phr=D3DPreprocess(bytes,(UIntPtr)bytes.Length,"FairyToonOffline",IntPtr.Zero,inc,out preprocessed,out preprocessErrors);
   if(phr<0) throw new Exception(Text(preprocessErrors));
   bytes=Encoding.UTF8.GetBytes(Text(preprocessed)); Marshal.Release(preprocessed); if(preprocessErrors!=IntPtr.Zero) Marshal.Release(preprocessErrors);
   int hr=D3DCompile(bytes,(UIntPtr)bytes.Length,"FairyToonOffline",IntPtr.Zero,inc,entry,target,1u<<15,0,out code,out errors);
   if(hr<0) throw new Exception(entry+" "+target+"\n"+Text(errors));
   if(D3DDisassemble(Data(code),Size(code),0,null,out disasm)<0) throw new Exception("Disassembly failed");return Text(disasm);
  } finally {
   if(disasm!=IntPtr.Zero) Marshal.Release(disasm);if(errors!=IntPtr.Zero) Marshal.Release(errors);if(code!=IntPtr.Zero) Marshal.Release(code);
   foreach(var pointer in paths.Keys) Marshal.FreeHGlobal(pointer);Marshal.FreeHGlobal(inc);Marshal.FreeHGlobal(table);GC.KeepAlive(open);GC.KeepAlive(close);
  }
 }
}
'@
} finally {
 $env:TEMP = $previousCompilerTemp
 $env:TMP = $previousCompilerTmp
}
$prefix = @'
#define UNITY_UNIFIED_SHADER_PRECISION_MODEL 1
#define SHADER_API_D3D11 1
#define SHADER_TARGET 45
#define UNITY_VERSION 600060
#define UNITY_COMPILER_HLSL 1
#define _FT_MAIN_LAYERS 1
#define _FT_EMISSION 1
#define _FT_REFLECTION 1
#define _FT_MATCAP 1
#define _FT_RIM 1
#define _FT_RIMSHADE 1
#define _FT_SHADOW_RAMP 1
#define _ADDITIONAL_LIGHTS 1
#define _ADDITIONAL_LIGHT_SHADOWS 1
#define _REFLECTION_PROBE_BLENDING 1
#define _REFLECTION_PROBE_BOX_PROJECTION 1
#define _SHADOWS_SOFT_HIGH 1
#define _SCREEN_SPACE_OCCLUSION 1
#define _LIGHT_COOKIES 1
#define _LIGHT_LAYERS 1
'@
if($ColorSpace -eq 'Gamma') { $prefix += "`n#define UNITY_COLORSPACE_GAMMA 1`n" }
if($NativeHDR) { $prefix += "`n#define UNITY_USE_NATIVE_HDR 1`n" }
if($ReviewMatrix) {
 $MainLightShadows = @('Cascade','Screen')
 $LightingPaths = @('Forward','ForwardPlus','ForwardPlusAtlas')
}
$featureConfigurations = @(@{ Name='Requested'; NormalMap=(!$NoNormalMap); Surface=$SurfaceFeatures; MatCap=$true })
if($MermaidMatrix) {
 $featureConfigurations = @(
  @{ Name='FullSurface'; NormalMap=$true; Surface='All'; MatCap=$true },
  @{ Name='CustomMatCapOnly'; NormalMap=$false; Surface='None'; MatCap=$true },
  @{ Name='NoMatCap'; NormalMap=$true; Surface='All'; MatCap=$false }
 )
}
$lightingConfigurations=@(foreach($shadowMode in $MainLightShadows) {
 foreach($lightingPath in $LightingPaths) {
  @{ ShadowMode=$shadowMode; LightingPath=$lightingPath }
 }
})
$nl=[Environment]::NewLine
$dissolveConfigurations=@(@{Name='Runtime';Mode=-1;Shape=0})
if($DissolveMatrix) {
 $dissolveConfigurations+=@{Name='Disabled';Mode=0;Shape=0;Noise=1}
  foreach($noise in @(0,1)) {
  $dissolveConfigurations+=@(
   @{Name="Mask-Noise$noise";Mode=1;Shape=0;Noise=$noise},
   @{Name="UVRadial-Noise$noise";Mode=2;Shape=0;Noise=$noise},
   @{Name="UVDirectional-Noise$noise";Mode=2;Shape=1;Noise=$noise},
   @{Name="ObjectRadial-Noise$noise";Mode=3;Shape=0;Noise=$noise},
   @{Name="ObjectDirectional-Noise$noise";Mode=3;Shape=1;Noise=$noise}
  )
 }
}
if($DissolveCases) {
 foreach($caseName in $DissolveCases) {
  if($caseName -notin $dissolveConfigurations.Name) { throw ('Unknown dissolve case '+$caseName+'; use -DissolveMatrix for specialized cases') }
 }
 $dissolveConfigurations=@($dissolveConfigurations | Where-Object { $_.Name -in $DissolveCases })
}
function Get-MainToneUniformOverrides([string]$case) {
 if($case -eq 'Runtime') { return '' }
 $hsvg = if($case -eq 'AdultPlayer'){'-0.095,0.58,1,1'}else{'0,1,1,1'}
 return '#define _MainTexHSVG float4('+$hsvg+')'+$nl
}
function Get-DissolveUniformOverrides([hashtable]$configuration) {
 if($configuration.Mode -lt 0) { return '' }
 # Activate both layers; literal Params force FXC through the chosen mode/shape branch.
 # Position, UV, mask/noise textures and noise strength still perform real fragment work.
 $result='#define _UseMain2ndTex 1.0'+$nl+'#define _UseMain3rdTex 1.0'+$nl
 foreach($prefixName in @('_','_Main2nd','_Main3rd')) {
  $name=$prefixName+'DissolveParams'
  $noiseName=if($prefixName -eq '_'){'_FT_DissolveUseNoise'}else{'_FT'+$prefixName+'DissolveUseNoise'}
  $result+='#define '+$noiseName+' '+$configuration.Noise+'.0'+$nl
  $result+='#define '+$name+' float4('+ $configuration.Mode+'.0, '+$configuration.Shape+'.0, 0.175, 0.2)'+$nl
 }
 return $result
}
$referenceLayouts=@{}
$stageCount=0
$programCount=0
function Get-OfflineProgram([string]$program) {
 # Includes in a ShaderLab program are relative to its shader asset, not ProjectRoot.
 $program=[regex]::Replace($program,'(?m)^\s*#pragma\b[^\r\n]*','')
 return [regex]::Replace($program,'(#include(?:_with_pragmas)?\s+")([^"\r\n]+)(")',{
  param($match)
  if($match.Groups[2].Value.StartsWith('Packages/')) { return $match.Value }
  return $match.Groups[1].Value+'Packages/com.nw.fairytoon/Shaders/'+$match.Groups[2].Value+$match.Groups[3].Value
 })
}
function Get-StageResources([string]$assembly,[string]$label) {
 $textures=@([regex]::Matches($assembly,'(?m)^//\s+(\S+)\s+texture\b[^\r\n]*?\s+(\d+)\s*$'))
 $samplers=@([regex]::Matches($assembly,'(?m)^//\s+(\S+)\s+sampler(?:_c)?\b[^\r\n]*?\s+(\d+)\s*$'))
 $textureCount=0; $samplerCount=0
 foreach($binding in $textures) { $textureCount += [int]$binding.Groups[2].Value }
 foreach($binding in $samplers) { $samplerCount += [int]$binding.Groups[2].Value }
 if($textureCount -gt 128) { throw ($label+': DX11 per-stage texture limit exceeded: '+$textureCount+'/128') }
 if($samplerCount -gt 16) { throw ($label+': DX11 per-stage sampler limit exceeded: '+$samplerCount+'/16') }
 return $label+' textures '+$textureCount+'/128 samplers '+$samplerCount+'/16'
}
function Get-StageSignature([string]$assembly,[string]$kind) {
 $start=$assembly.IndexOf('// '+$kind+' signature:')
 if($start -lt 0) { throw ('Missing '+$kind+' signature') }
 $result=$assembly.Substring($start)
 if($kind -eq 'Input') {
  $end=$result.IndexOf('// Output signature:')
  if($end -ge 0) { $result=$result.Substring(0,$end) }
 }
 return $result
}
function Get-LayerSemantics([string]$signature) {
 return @([regex]::Matches($signature,'(?m)^//\s+TEXCOORD\s+(8|9|10|11)\s+') | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object)
}
function Get-InterpolatorUsage([string]$assembly,[string]$kind) {
 $signature=Get-StageSignature $assembly $kind
 # Packed float4 registers, excluding rasterizer system values; semantic index is not a count.
 $rows=@([regex]::Matches($signature,'(?m)^//\s+(\w+)\s+\d+\s+[xyzw][xyzw ]*?\s+(\d+)\s+NONE\s+\w+[^\S\r\n]*([xyzw ]*)[^\S\r\n]*$'))
 $declared=@($rows | ForEach-Object { $_.Groups[2].Value } | Select-Object -Unique).Count
 $used=@($rows | Where-Object { $_.Groups[3].Value.Trim().Length -gt 0 } | ForEach-Object { $_.Groups[2].Value } | Select-Object -Unique).Count
 return 'declared/used '+$declared+'/'+$used
}
function Test-StageInterface([hashtable]$stages,[int[]]$expected,[bool]$needsUV,[string]$label,[bool]$needsDissolveOS=$false) {
 $vertexOutputs=Get-StageSignature $stages.VS 'Output'
 $actual=@(Get-LayerSemantics $vertexOutputs)
 if(($actual -join ',') -ne ($expected -join ',')) { throw ($label+': expected VS layer semantics ['+($expected -join ',')+'], got ['+($actual -join ',')+']') }
 $vertexInputs=Get-StageSignature $stages.VS 'Input'
 $extraInputs=[regex]::Matches($vertexInputs,'(?m)^//\s+TEXCOORD\s+(1|2|3)\s+').Count
 if($extraInputs -ne $(if($needsUV){3}else{0})) { throw ($label+': unexpected UV1-3 mesh inputs: '+$extraInputs) }
 $pixelInputs=Get-StageSignature $stages.PS 'Input'
 $pixelLayers=@(Get-LayerSemantics $pixelInputs)
 $vertexDissolve=[regex]::IsMatch($vertexOutputs,'(?m)^//\s+TEXCOORD\s+12\s+')
 $pixelDissolve=[regex]::IsMatch($pixelInputs,'(?m)^//\s+TEXCOORD\s+12\s+')
 if($vertexDissolve -ne $needsDissolveOS) { throw ($label+': unexpected object-position VS TEXCOORD12: '+$vertexDissolve) }
 if($pixelDissolve -and !$vertexDissolve) { throw ($label+': PS consumes missing object-position TEXCOORD12') }
 if($stages.ContainsKey('GS')) {
  foreach($kind in @('Input','Output')) {
   if([regex]::IsMatch((Get-StageSignature $stages.GS $kind),'(?m)^//\s+TEXCOORD\s+12\s+')) {
    throw ($label+': Fur shell unexpectedly carries object-position TEXCOORD12')
   }
  }
 }
 foreach($semantic in $pixelLayers) {
  if($semantic -notin $expected) { throw ($label+': PS consumes unavailable layer TEXCOORD'+$semantic) }
 }
 $result='object OS12 VS/PS '+$vertexDissolve+'/'+$pixelDissolve+'; mesh UV1-3 '+$extraInputs+'; VS layer ['+($actual -join ',')+']; PS layer ['+($pixelLayers -join ',')+']; interpolators VS '+(Get-InterpolatorUsage $stages.VS 'Output')+' PS '+(Get-InterpolatorUsage $stages.PS 'Input')
 if($stages.ContainsKey('GS')) {
  $geometryInputs=Get-StageSignature $stages.GS 'Input'
  $geometryOutputs=Get-StageSignature $stages.GS 'Output'
  foreach($signature in @($geometryInputs,$geometryOutputs)) {
   $actualGeometry=@(Get-LayerSemantics $signature)
   if(($actualGeometry -join ',') -ne ($expected -join ',')) { throw ($label+': GS layer semantics differ from VS: ['+($actualGeometry -join ',')+']') }
  }
  # Fur carries its seed ID to the generator only; it must not reserve an extra interpolator after GS.
  foreach($signature in @($vertexOutputs,$geometryInputs)) {
   if([regex]::Matches($signature,'(?m)^//\s+TEXCOORD\s+6\s+').Count -ne 1) { throw ($label+': missing Fur vertex ID on VS-GS TEXCOORD6') }
  }
  foreach($signature in @($geometryOutputs,$pixelInputs)) {
   if([regex]::IsMatch($signature,'(?m)^//\s+TEXCOORD\s+6\s+')) { throw ($label+': Fur vertex ID leaked into GS-PS TEXCOORD6') }
  }
  $result+='; GS layer ['+($expected -join ',')+']; vertex ID VS-GS only; interpolators GS '+(Get-InterpolatorUsage $stages.GS 'Output')
 }
 return $result
}
function Get-StageProgram([string]$configuration,[hashtable]$pass,[string]$stage) {
 $result='#define SHADER_STAGE_'+$stage.ToUpper()+' 1'+$nl+$configuration
 # Unity does not define fragment-only material keywords while compiling VS/GS. Match the actual pragmas.
 $available=@([regex]::Matches($pass.Pragmas,'#pragma\s+shader_feature(?:_local)?(?:_(vertex|fragment|geometry))?\s+([^\r\n]+)') | ForEach-Object {
  if(!$_.Groups[1].Success -or $_.Groups[1].Value -eq $stage.ToLower()) { $_.Groups[2].Value -split '\s+' }
 })
 foreach($keyword in @([regex]::Matches($configuration,'#define\s+(_FT_\w+|_NORMALMAP|_ALPHATEST_ON|_SURFACE_TYPE_TRANSPARENT)\b') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)) {
  if($keyword -notin $available) { $result+='#undef '+$keyword+$nl }
 }
 # Unity provides this declaration marker for the actual pass pragma. URP uses it to distinguish
 # clustered lights with legacy probe blending from the optional clustered probe atlas.
 if($pass.Pragmas -match '#pragma\s+multi_compile(?:_fragment)?\s+[^\r\n]*\b_REFLECTION_PROBE_ATLAS\b') {
  $result+='#define _REFLECTION_PROBE_ATLAS_KEYWORD_DECLARED 1'+$nl
 }
 $result+=$pass.Program
 $result+=@'

#if USE_CLUSTER_LIGHT_LOOP && defined(_REFLECTION_PROBE_ATLAS_KEYWORD_DECLARED)
    #if defined(_REFLECTION_PROBE_ATLAS) && !CLUSTER_HAS_REFLECTION_PROBES
        #error Offline atlas configuration did not enable clustered reflection probes.
    #elif !defined(_REFLECTION_PROBE_ATLAS) && CLUSTER_HAS_REFLECTION_PROBES
        #error Offline legacy-probe configuration unexpectedly enabled the atlas.
    #endif
#endif
'@
 return $result
}
foreach($shaderName in $Shaders) {
 $shaderFile=if($shaderName -eq 'FairyToon'){'FairyToon.shader'}else{'FairyToon'+$shaderName+'.shader'}
 $shaderPath=Join-Path $ProjectRoot ('Packages/com.nw.fairytoon/Shaders/'+$shaderFile)
 $shaderText=Get-Content -LiteralPath $shaderPath -Raw
 $common=([regex]::Matches($shaderText,'(?s)\bHLSLINCLUDE\b(.*?)\bENDHLSL\b') | ForEach-Object { $_.Groups[1].Value }) -join $nl
 $passes=@([regex]::Matches($shaderText,'(?s)\bPass\s*\{(.*?)\bENDHLSL\s*\}') | ForEach-Object {
  $block=$_.Groups[1].Value
  @{
   Name=[regex]::Match($block,'\bName\s+"([^"]+)"').Groups[1].Value
   Vertex=[regex]::Match($block,'#pragma\s+vertex\s+(\w+)').Groups[1].Value
   Fragment=[regex]::Match($block,'#pragma\s+fragment\s+(\w+)').Groups[1].Value
   Geometry=[regex]::Match($block,'#pragma\s+geometry\s+(\w+)').Groups[1].Value
   Pragmas=$common+$nl+$block
   Program=(Get-OfflineProgram ($common+$nl+($block -split '\bHLSLPROGRAM\b',2)[1]))
  }
 })
 if(!$passes.Count) { throw ('No HLSL passes found in '+$shaderFile) }
 $blurOptions=if($shaderName -eq 'Refraction' -and $RefractionBlur -eq 'Both'){@($false,$true)}else{@($shaderName -eq 'Refraction' -and $RefractionBlur -eq 'On')}
foreach($blur in $blurOptions) {
foreach($model in $Models) {
foreach($pass in $passes) {
 $passName=$pass.Name
 if($OnlyForward -and $passName -notin @('Forward','GemPre','FurForward','FurSecond')) { continue }
 if($PassNames -and $passName -notin $PassNames) { continue }
 if($model -ne 'Toon' -and $passName -notin @('Forward','SeeThroughHair','FurForward','FurSecond')) { continue }
 # The second blur choice only needs the affected forward program.
 if($blur -and $RefractionBlur -eq 'Both' -and $passName -ne 'Forward') { continue }
 # Auxiliary passes do not shade probes or direct specular; compile their interfaces once.
 $passLightingConfigurations=if($passName -in @('Forward','SeeThroughHair','FurForward','FurSecond')){$lightingConfigurations}else{@($lightingConfigurations[0])}
 # Auxiliary interfaces do not consume MatCaps; run them once, with the first feature configuration.
 $passFeatureConfigurations=if($passName -in @('Forward','SeeThroughHair','FurForward','FurSecond')){$featureConfigurations}else{@($featureConfigurations[0])}
 # Gem, Fur shells and opaque output-only passes cannot execute dissolve. Keep their real
 # runtime guards/resource checks once, rather than duplicating identical specializations.
 $passCoverage=if($pass.Program -match '#define\s+_SURFACE_TYPE_TRANSPARENT\b'){'Transparent'}elseif($pass.Program -match '#define\s+_ALPHATEST_ON\b'){'Cutout'}else{$Coverage}
 $canDissolve=$shaderName -ne 'Gem' -and $passName -notin @('FurForward','FurSecond') -and
  ($passCoverage -ne 'Opaque' -or ($passName -in @('Forward','SeeThroughHair') -and !$NoLayers -and $LayerFeatures -in @('Both','Main')))
 $passDissolveConfigurations=if($canDissolve){$dissolveConfigurations}else{@(@{Name='Runtime';Mode=-1;Shape=0})}
 $canWet=$shaderName -eq 'FairyToon' -and $passName -in @('Forward','SeeThroughHair','Outline')
 $passWetness=if($canWet -and $Wetness -eq 'Both'){@($false,$true)}else{@($canWet -and $Wetness -eq 'On')}
 # Outline has no model keyword; its existing Toon representative exercises its material-model guard.
 $canIris=$pass.Pragmas -match '#pragma\s+shader_feature_local\s+_\s+_FT_EF_IRIS\b'
 if($passName -in @('ShadowCaster','CharacterShadow') -and $canIris) { throw ('Light-view pass declares iris shift: '+$passName) }
 $passIris=if($canIris -and $Iris -eq 'Both'){@($false,$true)}else{@($canIris -and $Iris -eq 'On')}
 foreach($wet in $passWetness) {
 foreach($irisEnabled in $passIris) {
 $pupilModes=if($IrisMatrix -and $irisEnabled){@('Runtime','Layer','Fold','Surface')}else{@('Runtime')}
 foreach($pupilMode in $pupilModes) {
 foreach($featureConfiguration in $passFeatureConfigurations) {
 $activeSurfaceFeatures = $featureConfiguration.Surface
 foreach($lightingConfiguration in $passLightingConfigurations) {
 foreach($dissolveConfiguration in $passDissolveConfigurations) {
 $uniformOverrides=Get-DissolveUniformOverrides $dissolveConfiguration
 $uniformOverrides+=Get-MainToneUniformOverrides $MainTone
 switch($pupilMode) {
  'Layer' { $uniformOverrides+=$nl+'#define _EFPupilLayer 1.0'+$nl+'#define _EFPupilDepth 0.5'+$nl }
  'Fold' { $uniformOverrides+=$nl+'#define _EFPupilLayer 0.0'+$nl }
  'Surface' { $uniformOverrides+=$nl+'#define _EFPupilLayer 1.0'+$nl+'#define _EFPupilDepth 0.0'+$nl }
 }
 switch($IrisAdjustment) {
  'Defaults' { $uniformOverrides+=$nl+'#define _EFIrisEdgeFade 0.0'+$nl+'#define _EFIrisVertical 1.0'+$nl+'#define _EFIrisMaxShift 2.0'+$nl }
  'LabDefaults' { $uniformOverrides+=$nl+'#define _EFIrisEdgeFade 0.4'+$nl+'#define _EFIrisVertical 0.6'+$nl+'#define _EFIrisMaxShift 0.5'+$nl }
  'Stress' { $uniformOverrides+=$nl+'#define _EFIrisEdgeFade 1.0'+$nl+'#define _EFIrisVertical 0.0'+$nl+'#define _EFIrisMaxShift 0.05'+$nl }
 }
 $activeCoverage=if($pass.Program -match '#define\s+_SURFACE_TYPE_TRANSPARENT\b'){'Transparent'}elseif($pass.Program -match '#define\s+_ALPHATEST_ON\b'){'Cutout'}else{$Coverage}
 $targetMatch=[regex]::Match($pass.Pragmas,'#pragma\s+target\s+([0-9.]+)')
 $shaderTarget=if($targetMatch.Success){[int](10 * [double]::Parse($targetMatch.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture))}else{45}
 $source=$prefix.Replace('#define SHADER_TARGET 45',('#define SHADER_TARGET '+$shaderTarget))+$nl
 if($model -ne 'Toon') { $source+='#define _FT_ENDFIELD 1'+$nl }
 if($model -eq 'EndfieldSkin') { $source+='#define _FT_EF_SKIN 1'+$nl }
 if($model -eq 'EndfieldFace') { $source+='#define _FT_EF_FACE 1'+$nl }
 if($model -eq 'EndfieldHair') { $source+='#define _FT_EF_HAIR 1'+$nl }
 if($model -eq 'EndfieldEye') { $source+='#define _FT_EF_EYE 1'+$nl }
 if($irisEnabled) { $source+='#define _FT_EF_IRIS 1'+$nl }
 if($activeCoverage -eq 'Cutout') { $source+='#define _ALPHATEST_ON 1'+$nl }
 if($activeCoverage -eq 'Transparent') { $source+='#define _SURFACE_TYPE_TRANSPARENT 1'+$nl }
 $mainLayers = !$NoLayers -and $LayerFeatures -in @('Both','Main') -and $pass.Pragmas -match '#pragma shader_feature[^\r\n]*\b_FT_MAIN_LAYERS\b'
 $emission = !$NoLayers -and $LayerFeatures -in @('Both','Emission') -and $pass.Pragmas -match '#pragma shader_feature[^\r\n]*\b_FT_EMISSION\b'
 if(!$mainLayers) { $source+='#undef _FT_MAIN_LAYERS'+$nl }
 if(!$emission) { $source+='#undef _FT_EMISSION'+$nl }
 if($NoReflection) { $source+='#undef _FT_REFLECTION'+$nl }
 if($wet) { $source+='#define _FT_WET 1'+$nl }
 if($NoShadowRamp) { $source+='#undef _FT_SHADOW_RAMP'+$nl }
 if(!$featureConfiguration.MatCap) { $source+='#undef _FT_MATCAP'+$nl }
 switch($lightingConfiguration.ShadowMode) {
  'Main' { $source+='#define _MAIN_LIGHT_SHADOWS 1'+$nl }
  'Cascade' { $source+='#define _MAIN_LIGHT_SHADOWS_CASCADE 1'+$nl }
  'Screen' { $source+='#define _MAIN_LIGHT_SHADOWS_SCREEN 1'+$nl }
 }
 if($lightingConfiguration.LightingPath -ne 'Forward') { $source+='#define _CLUSTER_LIGHT_LOOP 1'+$nl }
 if($ProbeAtlas -or $lightingConfiguration.LightingPath -eq 'ForwardPlusAtlas') { $source+='#define _REFLECTION_PROBE_ATLAS 1'+$nl }
 if($ProbeVolumes) { $source+='#define PROBE_VOLUMES_L1 1'+$nl }
 if($featureConfiguration.NormalMap) { $source+='#define _NORMALMAP 1'+$nl }
 $forward = $passName -in @('Forward','SeeThroughHair','FurForward','FurSecond')
 $normal2nd = $activeSurfaceFeatures -in @('All','Normal2nd') -and $pass.Pragmas -match '#pragma shader_feature[^\r\n]*\b_FT_NORMAL2ND\b'
 $glitter = $activeSurfaceFeatures -in @('All','Glitter') -and $pass.Pragmas -match '#pragma shader_feature[^\r\n]*\b_FT_GLITTER\b'
 if($passName -eq 'DepthNormals') {
  $source+='#define FAIRYTOON_PASS_DEPTHNORMALS 1'+$nl
  if($OctNormals) { $source+='#define _GBUFFER_NORMALS_OCT 1'+$nl }
 }
 if($normal2nd) { $source+='#define '+$(if($Normal2ndUV -eq 'UV0'){'_FT_NORMAL2ND'}else{'_FT_NORMAL2ND_UV'})+' 1'+$nl }
 if($glitter) { $source+='#define '+$(if($GlitterUV -eq 'UV0'){'_FT_GLITTER'}else{'_FT_GLITTER_UV'})+' 1'+$nl }
 if($forward -and $activeSurfaceFeatures -in @('All','Anisotropy')) { $source+='#define _FT_ANISOTROPY 1'+$nl }
 if($forward -and $activeSurfaceFeatures -in @('All','Backlight')) { $source+='#define _FT_BACKLIGHT 1'+$nl }
 if(!$NoOutline) { $source+='#define _FT_OUTLINE 1'+$nl }
 if($blur -and $forward) { $source+='#define _FT_REFRACTION_BLUR 1'+$nl }
 $vs=[FairyToonOfflineCompiler]::Compile($ProjectRoot,$PackageCache,(Get-StageProgram $source $pass 'Vertex'),$pass.Vertex,'vs_5_0',$uniformOverrides)
 $ps=[FairyToonOfflineCompiler]::Compile($ProjectRoot,$PackageCache,(Get-StageProgram $source $pass 'Fragment'),$pass.Fragment,'ps_5_0',$uniformOverrides)
 $stages=@{VS=$vs;PS=$ps}
 # The reflected VS resources prove the keyword reached the vertex stage and depth-zero/fold never sample it.
 $movingIris=$irisEnabled -and $pupilMode -in @('Runtime','Layer')
 $vertexFrame=$vs -match '(?m)^//\s+_EFIrisFrame\s+texture\b'
 if($vertexFrame -ne $movingIris) { throw ('Iris VS frame mismatch in '+$shaderName+'/'+$passName+'/'+$pupilMode) }
 if($movingIris) {
  $signature=Get-StageSignature $vs 'Input'
  foreach($semantic in @('POSITION','NORMAL','TANGENT','TEXCOORD')) {
   if($signature -notmatch ('(?m)^//\s+'+$semantic+'\s+0\s+')) { throw ('Iris VS missing skinned mesh input '+$semantic+' in '+$passName) }
  }
 }
 # Wet globals must survive only in opted-in fragment programs; vertex interfaces stay dry.
 $hasWetGlobals=$ps -match '(?m)^//\s+float4\s+_FT_Wet0;'
 if($hasWetGlobals -ne $wet) { throw ('Wet globals mismatch in '+$shaderName+'/'+$passName+'/wet='+$wet) }
 if($vs -match '(?m)^//\s+float4\s+_FT_Wet[01];') { throw ('Wet globals leaked into vertex stage in '+$passName) }
 if($pass.Geometry) {
  $stages.GS=[FairyToonOfflineCompiler]::Compile($ProjectRoot,$PackageCache,(Get-StageProgram $source $pass 'Geometry'),$pass.Geometry,'gs_5_0',$uniformOverrides)
 }
 # Verify constant cases in the actual forward program, beyond isolated helper probes.
 if($MainTone -eq 'Identity' -and $ps -match '(?m)^//\s+_MainColorAdjustMask\s+texture\b') {
  throw ('Identity HSVG still samples its adjustment mask in '+$shaderName+'/'+$passName)
 }
 if($MainTone -eq 'AdultPlayer' -and $shaderName -eq 'FairyToon' -and $forward -and
    $ps -notmatch '(?m)^//\s+_MainColorAdjustMask\s+texture\b') {
  throw ('AdultPlayer HSVG adjustment mask missing from '+$passName)
 }
 # Confirm the real lilToon colour-shadow branch is exercised by AdultPlayer coverage.
 if($NoShadowRamp -and $shaderName -eq 'FairyToon' -and $model -eq 'Toon' -and $forward -and
    $ps -notmatch '(?m)^//\s+_ShadowColorTex\s+texture\b') {
  throw ('Colour-shadow branch missing from '+$passName)
 }
 # Check compiled resources after ShaderLab includes, not a helper with synthetic pass defines.
 if($shaderName -eq 'Fur' -and $passName -in @('FurForward','FurSecond') -and
    $ps -match '(?m)^//\s+_(?:Main(?:2nd|3rd))?Dissolve(?:Noise)?Mask\s+texture\b') {
  throw ('Fur shell unexpectedly samples material dissolve in '+$passName)
 }
 if($shaderName -eq 'Gem' -and
    $ps -match '(?m)^//\s+_(?:Main(?:2nd|3rd))?Dissolve(?:Noise)?Mask\s+texture\b') {
  throw ('Gem unexpectedly samples dissolve textures in '+$passName)
 }
 if($activeCoverage -eq 'Opaque' -and
    $ps -match '(?m)^//\s+_Dissolve(?:Noise)?Mask\s+texture\b') {
  throw ('Opaque unexpectedly samples whole-material dissolve textures in '+$shaderName+'/'+$passName)
 }
 if($dissolveConfiguration.Mode -eq 0 -and
    $ps -match '(?m)^//\s+_(?:Main(?:2nd|3rd))?Dissolve(?:Noise)?Mask\s+texture\b') {
  throw ('Disabled dissolve unexpectedly samples textures in '+$shaderName+'/'+$passName)
 }
 if($dissolveConfiguration.Mode -ge 2 -and
    $ps -match '(?m)^//\s+_(?:Main(?:2nd|3rd))?DissolveMask\s+texture\b') {
  throw ('Position dissolve unexpectedly samples mask textures in '+$shaderName+'/'+$passName)
 }
 if($dissolveConfiguration.Mode -ge 0 -and $dissolveConfiguration.Noise -eq 0 -and
    $ps -match '(?m)^//\s+_(?:Main(?:2nd|3rd))?DissolveNoiseMask\s+texture\b') {
  throw ('No-noise dissolve unexpectedly samples noise textures in '+$shaderName+'/'+$passName)
 }
 $programCount++; $stageCount+=$stages.Count
 # FXC retains unused VS outputs. Check actual signatures, not assumptions about PS stripping.
 $hull = $passName -eq 'Outline' -or ($passName -eq 'MotionVectors' -and !$NoOutline -and $pass.Pragmas -match '#pragma shader_feature[^\r\n]*\b_FT_OUTLINE\b')
 $coverageLayers = !$forward -and !$hull -and $mainLayers -and $activeCoverage -ne 'Opaque'
 $needsUV = !$hull -and $(if($forward){$mainLayers -or $emission -or ($normal2nd -and $Normal2ndUV -ne 'UV0') -or ($glitter -and $GlitterUV -ne 'UV0')}else{$coverageLayers -or ($normal2nd -and $Normal2ndUV -ne 'UV0')})
 $expectedSemantics = @()
 if($needsUV) { $expectedSemantics += 8,9 }
 if($coverageLayers) {
  if($passName -ne 'DepthNormals') { $expectedSemantics += 10 }
  $expectedSemantics += 11
 }
 # See-through has no complete spare OS slot without layer geometry. Depth/shadow/motion pack OS.
 $needsDissolveOS=$shaderName -eq 'FairyToon' -and !$coverageLayers -and $activeCoverage -ne 'Opaque' -and
     $passName -in @('SeeThroughMask','SeeThroughFakeShadow','SeeThroughEarlyZ')
 $interfaces=Test-StageInterface $stages $expectedSemantics $needsUV ($shaderName+'/'+$passName) $needsDissolveOS
 $bufferStatuses=@(); $resources=@()
 foreach($stage in @('VS','GS','PS')) {
 if(!$stages.ContainsKey($stage)) { continue }
 $assembly=$stages[$stage]
 $buffer=[regex]::Match($assembly,'(?s)// cbuffer UnityPerMaterial.*?// }').Value
 $layout=([regex]::Matches($buffer,'//\s+\w+\s+(_\w+);\s+// Offset:\s*(\d+)\s+Size:\s*(\d+)') | ForEach-Object { $_.Groups[1].Value+':'+$_.Groups[2].Value+':'+$_.Groups[3].Value }) -join ';'
 $bufferStatus = 'identical'
 if($layout.Length -eq 0) {
  # Opaque output-only passes and geometric DepthNormals need no material constants.
  $constantOutput = $passName -in @('ShadowCaster','CharacterShadow','CharacterMask','DepthOnly','MotionVectors') -or
   ($passName -eq 'DepthNormals' -and !$featureConfiguration.NormalMap -and !$normal2nd)
  if($buffer.Length -ne 0 -or ($stage -eq 'PS' -and $passName -ne 'GemPre' -and ($activeCoverage -ne 'Opaque' -or !$constantOutput))) {
   throw ('Missing reflected material buffer layout in '+$shaderName+'/'+$passName+'/'+$stage)
  }
  $bufferStatus = 'unused/stripped'
 } elseif(!$referenceLayouts.ContainsKey($shaderName)) { $referenceLayouts[$shaderName]=$layout }
 elseif($layout -ne $referenceLayouts[$shaderName]) { throw ('Material buffer layout differs in '+$shaderName+'/'+$passName+'/'+$stage) }
 $bufferStatuses+=$stage+':'+$bufferStatus
 $resources+=Get-StageResources $assembly $stage
 }
 $label=$shaderName+'/'+$activeCoverage+'/'+$model+'/'+$featureConfiguration.Name+'/'+$passName+'/'+$lightingConfiguration.ShadowMode+'/'+$lightingConfiguration.LightingPath+$(if($shaderName -eq 'Refraction'){'/blur='+$blur}else{''})+'/iris='+$irisEnabled+'/pupil='+$pupilMode+'/irisAdjustment='+$IrisAdjustment+'/wet='+$wet+'/dissolve='+$dissolveConfiguration.Name+'/mainTone='+$MainTone
 Write-Output ($label+': '+(($stages.Keys | Sort-Object) -join '/')+' compiled; material buffer '+($bufferStatuses -join ', ')+'; '+($resources -join '; ')+'; '+$interfaces)
}
}
}
}
}
}
}
}
}
}
Write-Output ('Offline compile summary: '+$programCount+' pass/model configurations, '+$stageCount+' stage programs; per-shader UnityPerMaterial layouts identical wherever used. Features '+$SurfaceFeatures+'; Normal2nd/Glitter UV '+$Normal2ndUV+'/'+$GlitterUV+'.')
& node (Join-Path $PSScriptRoot 'ReportVariants.cjs') --project-root $ProjectRoot --package-cache $PackageCache --shaders ($Shaders -join ',')
if($LASTEXITCODE -ne 0) { throw 'Variant count report failed' }
