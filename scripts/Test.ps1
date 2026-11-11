[CmdletBinding()]
param([string]$Configuration='Debug')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
& (Join-Path $repoRoot "Demo\bin\$Configuration\DummyWbs.Demo.exe") --test
if($LASTEXITCODE -ne 0){throw 'Prototype tests failed.'}
