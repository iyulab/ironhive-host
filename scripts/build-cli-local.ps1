<#
.SYNOPSIS
    ironhive.exe를 빌드하고 D:\lib에 복사합니다.

.DESCRIPTION
    build-local.ps1을 실행한 후 결과물을 D:\lib로 복사합니다.
    D:\lib는 PATH에 등록되어 있어 빌드 후 바로 사용 가능합니다.

.EXAMPLE
    .\build-cli-local.ps1
#>

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$TargetDir = "D:\lib"

# build-local.ps1 실행
& "$ScriptDir\build-local.ps1"

if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# D:\lib로 복사
$SourceExe = Join-Path (Split-Path -Parent $ScriptDir) "dist\ironhive.exe"

if (-not (Test-Path $SourceExe)) {
    throw "Build output not found: $SourceExe"
}

Write-Host ""
Write-Host "Copying to $TargetDir..." -ForegroundColor Yellow
Copy-Item $SourceExe $TargetDir -Force

$TargetExe = Join-Path $TargetDir "ironhive.exe"
Write-Host "Installed: $TargetExe" -ForegroundColor Green
