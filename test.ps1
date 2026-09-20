$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path "$projectRoot\work" -Force | Out-Null
& "$projectRoot\build.ps1"
& $compiler /nologo /target:exe /codepage:65001 "/out:$projectRoot\work\FakeCLI.exe" "$projectRoot\tests\FakeCLI.cs"
if ($LASTEXITCODE -ne 0) { throw 'Fake CLI build failed.' }
Copy-Item -LiteralPath "$projectRoot\dist\ErinRemote.exe" -Destination "$projectRoot\work\ErinRemote.exe" -Force
Copy-Item -LiteralPath "$projectRoot\tests\fixtures" -Destination "$projectRoot\work" -Recurse -Force
$testProcess = Start-Process -FilePath "$projectRoot\work\ErinRemote.exe" -ArgumentList '--self-test',('"' + $projectRoot + '\work\test-results.txt"') -WindowStyle Hidden -PassThru
$testProcess.WaitForExit()
Get-Content -LiteralPath "$projectRoot\work\test-results.txt" -Encoding UTF8
if ($testProcess.ExitCode -ne 0) { throw 'Tests failed.' }

