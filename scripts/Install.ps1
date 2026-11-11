[CmdletBinding(SupportsShouldProcess)]
param([switch]$ReplaceExisting)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$bundle=Join-Path $repoRoot 'artifacts\Naviswork.DummyWbs.bundle'
if(-not (Test-Path -LiteralPath (Join-Path $bundle 'PackageContents.xml'))){throw 'Run scripts/Build.ps1 first.'}
$destinationRoot=Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$destination=Join-Path $destinationRoot 'Naviswork.DummyWbs.bundle'
$checkedRoot=[IO.Path]::GetFullPath($destinationRoot).TrimEnd('\')+'\'
if(-not [IO.Path]::GetFullPath($destination).StartsWith($checkedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid plugin destination.'}
if(Test-Path -LiteralPath $destination){
 if(-not $ReplaceExisting){throw 'Existing dummy plugin found. Use -ReplaceExisting to retain a backup and replace it.'}
 $backup=$destination+'.backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)
 if(-not [IO.Path]::GetFullPath($backup).StartsWith($checkedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid backup destination.'}
 if($PSCmdlet.ShouldProcess($destination,"Back up to $backup")){Move-Item -LiteralPath $destination -Destination $backup}
}
if($PSCmdlet.ShouldProcess($destination,'Install dummy WBS plugin')){
 New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
 Copy-Item -LiteralPath $bundle -Destination $destination -Recurse
 Write-Host "Installed to $destination"
}
