param([Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
if ((git status --porcelain).Length -gt 0) { throw 'Commit project changes before release.' }
& "$PSScriptRoot\test.ps1"
$built=[Diagnostics.FileVersionInfo]::GetVersionInfo("$PSScriptRoot\dist\ErinRemote.exe").FileVersion
if (([Version]$built).ToString(3) -ne $Version) { throw "Version mismatch: build=$built, requested=$Version" }
$tag="v$Version"
git tag $tag
if ($LASTEXITCODE -ne 0) { throw 'Tag creation failed.' }
git push origin HEAD
if ($LASTEXITCODE -ne 0) { throw 'Source push failed.' }
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw 'Tag push failed.' }
Write-Output "GitHub Actions will build and publish $tag. Check the Actions tab."
