# Execute production runtime C# with engine API test doubles. Never starts Unity or restores packages.
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path "$PSScriptRoot/../../..").Path
$scratch = Join-Path $projectRoot 'Logs/WetnessChecks'
[void][IO.Directory]::CreateDirectory($scratch)
$dotnet = (Get-Command dotnet).Source
$dotnetRoot = Split-Path $dotnet -Parent
$sdk = Get-ChildItem "$dotnetRoot/sdk" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$reference = Get-ChildItem "$dotnetRoot/packs/Microsoft.NETCore.App.Ref" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$framework = Get-ChildItem "$($reference.FullName)/ref" -Directory | Select-Object -First 1
$major = $reference.Name.Split('.')[0]
$output = Join-Path $scratch 'WetnessChecks.dll'
$arguments = @('-nologo', '-target:exe', '-langversion:9', '-define:UNITY_EDITOR', "-out:$output")
$arguments += Get-ChildItem $framework.FullName -Filter '*.dll' | ForEach-Object { '-r:' + $_.FullName }
$arguments += @("$PSScriptRoot/WetnessRuntimeTests.cs", "$PSScriptRoot/../Runtime/FairyToonWetness.cs", "$PSScriptRoot/../Runtime/FairyToonWaterVolume.cs")
& $dotnet "$($sdk.FullName)/Roslyn/bincore/csc.dll" @arguments
if ($LASTEXITCODE -ne 0) { throw 'Wetness runtime test compilation failed.' }
$config = '{"runtimeOptions":{"tfm":"net' + $major + '.0","framework":{"name":"Microsoft.NETCore.App","version":"' + $major + '.0.0"}}}'
[IO.File]::WriteAllText((Join-Path $scratch 'WetnessChecks.runtimeconfig.json'), $config)
& $dotnet $output
if ($LASTEXITCODE -ne 0) { throw 'Wetness runtime checks failed.' }
