<#
.SYNOPSIS
    IronHive CLI 로컬 빌드 스크립트

.DESCRIPTION
    현재 플랫폼에 맞는 ironhive 실행 파일을 빌드합니다.
    기본적으로 Release 모드로 빌드하며, 단일 파일로 생성됩니다.

.PARAMETER Configuration
    빌드 구성 (Debug, Release). 기본값: Release

.PARAMETER Runtime
    런타임 식별자 (win-x64, linux-x64, osx-x64, osx-arm64).
    지정하지 않으면 현재 시스템에 맞게 자동 감지합니다.

.PARAMETER OutputDir
    출력 디렉토리. 기본값: ./dist

.PARAMETER NoBuild
    빌드 없이 기존 빌드 결과만 복사합니다.

.PARAMETER Install
    빌드 후 사용자 PATH에 설치합니다.

.EXAMPLE
    .\build-local.ps1
    # 현재 플랫폼용 Release 빌드

.EXAMPLE
    .\build-local.ps1 -Configuration Debug
    # Debug 모드로 빌드

.EXAMPLE
    .\build-local.ps1 -Runtime linux-x64
    # Linux x64용 크로스 컴파일

.EXAMPLE
    .\build-local.ps1 -Install
    # 빌드 후 PATH에 설치
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string]$Runtime,

    [string]$OutputDir = "./dist",

    [switch]$NoBuild,

    [switch]$Install
)

$ErrorActionPreference = "Stop"

# 프로젝트 루트로 이동
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
Push-Location $ProjectRoot

try {
    # 런타임 자동 감지
    if (-not $Runtime) {
        if ($IsWindows -or $env:OS -eq "Windows_NT") {
            $Runtime = "win-x64"
        } elseif ($IsMacOS) {
            # Apple Silicon 감지
            $arch = & uname -m
            if ($arch -eq "arm64") {
                $Runtime = "osx-arm64"
            } else {
                $Runtime = "osx-x64"
            }
        } else {
            # Linux
            $arch = & uname -m
            if ($arch -eq "aarch64") {
                $Runtime = "linux-arm64"
            } else {
                $Runtime = "linux-x64"
            }
        }
    }

    # 실행 파일 이름 결정
    $ExeName = if ($Runtime -like "win-*") { "ironhive.exe" } else { "ironhive" }

    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " IronHive CLI Local Build" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Configuration: $Configuration" -ForegroundColor Gray
    Write-Host "Runtime:       $Runtime" -ForegroundColor Gray
    Write-Host "Output:        $OutputDir" -ForegroundColor Gray
    Write-Host ""

    # 버전 정보 읽기
    $PropsFile = Join-Path $ProjectRoot "Directory.Build.props"
    if (Test-Path $PropsFile) {
        $PropsContent = Get-Content $PropsFile -Raw
        if ($PropsContent -match '<Version>([^<]+)</Version>') {
            $Version = $Matches[1]
            Write-Host "Version:       $Version" -ForegroundColor Green
        }
    }
    Write-Host ""

    # 출력 디렉토리 생성
    $OutputPath = Join-Path $ProjectRoot $OutputDir
    if (-not (Test-Path $OutputPath)) {
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    }

    if (-not $NoBuild) {
        Write-Host "[1/3] Restoring dependencies..." -ForegroundColor Yellow
        & dotnet restore
        if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

        Write-Host "[2/3] Building..." -ForegroundColor Yellow
        & dotnet publish src/IronHive.Cli `
            -c $Configuration `
            -r $Runtime `
            --self-contained true `
            -p:PublishSingleFile=true `
            -o $OutputPath

        if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    }

    # 빌드 결과 확인
    $ExePath = Join-Path $OutputPath $ExeName
    if (-not (Test-Path $ExePath)) {
        throw "Build output not found: $ExePath"
    }

    $FileInfo = Get-Item $ExePath
    $SizeMB = [math]::Round($FileInfo.Length / 1MB, 2)

    Write-Host "[3/3] Build complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Output: $ExePath" -ForegroundColor White
    Write-Host "Size:   $SizeMB MB" -ForegroundColor Gray
    Write-Host ""

    # 버전 확인
    Write-Host "Verifying build..." -ForegroundColor Yellow
    & $ExePath --version
    Write-Host ""

    # 설치 옵션
    if ($Install) {
        Write-Host "Installing to PATH..." -ForegroundColor Yellow

        # 설치 디렉토리 결정
        if ($Runtime -like "win-*") {
            $InstallDir = Join-Path $env:LOCALAPPDATA "IronHive"
        } else {
            $InstallDir = Join-Path $env:HOME ".local/bin"
        }

        if (-not (Test-Path $InstallDir)) {
            New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
        }

        $InstallPath = Join-Path $InstallDir $ExeName
        Copy-Item $ExePath $InstallPath -Force

        Write-Host "Installed to: $InstallPath" -ForegroundColor Green

        # Windows PATH 추가 안내
        if ($Runtime -like "win-*") {
            $CurrentPath = [Environment]::GetEnvironmentVariable("PATH", "User")
            if ($CurrentPath -notlike "*$InstallDir*") {
                Write-Host ""
                Write-Host "To add to PATH, run:" -ForegroundColor Yellow
                Write-Host "[Environment]::SetEnvironmentVariable('PATH', `$env:PATH + ';$InstallDir', 'User')" -ForegroundColor Cyan
            }
        }
    }

    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Done!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage:" -ForegroundColor Gray
    Write-Host "  $ExePath --help" -ForegroundColor White
    Write-Host "  $ExePath -p `"Hello, world!`"" -ForegroundColor White
    Write-Host ""

} finally {
    Pop-Location
}
