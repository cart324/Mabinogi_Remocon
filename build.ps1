param([string]$OutputName = "ErinRemote.exe")
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'dist') -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/win32manifest:$projectRoot\src\app.manifest" "/resource:$projectRoot\src\capabilities.json,capabilities.json" "/resource:$projectRoot\data\recipes.json,recipes.json" "/resource:$projectRoot\data\routes.json,routes.json" "/out:$projectRoot\dist\$OutputName" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $projectRoot\dist\$OutputName"
