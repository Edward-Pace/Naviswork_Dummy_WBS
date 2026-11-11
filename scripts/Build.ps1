[CmdletBinding()]
param([string]$Configuration='Debug')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$installation=& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
if(-not $installation){throw 'Visual Studio MSBuild is required.'}
$msbuild=Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
foreach($project in @('Demo\Demo.csproj','Plugin\Plugin.csproj')){
 & $msbuild (Join-Path $repoRoot $project) /m /p:Configuration=$Configuration /p:Platform=x64 /verbosity:minimal
 if($LASTEXITCODE -ne 0){throw "Build failed: $project"}
}
$bundle=Join-Path $repoRoot 'artifacts\Naviswork.DummyWbs.bundle'
$content=Join-Path $bundle 'Contents\v24'
New-Item -ItemType Directory -Path $content -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\PackageContents.xml') -Destination (Join-Path $bundle 'PackageContents.xml') -Force
foreach($file in @('DummyWbs.Core.dll','DummyWbs.Navisworks2027.dll')){
 Copy-Item -LiteralPath (Join-Path $repoRoot "Plugin\bin\$Configuration\$file") -Destination (Join-Path $content $file) -Force
}
$config=Join-Path $content 'wbs-settings.json'
if(-not (Test-Path -LiteralPath $config)){
 & (Join-Path $repoRoot "Demo\bin\$Configuration\DummyWbs.Demo.exe") --write-settings $config
 if($LASTEXITCODE -ne 0){throw 'Default mapping generation failed.'}
}
Write-Host "Bundle: $bundle"
