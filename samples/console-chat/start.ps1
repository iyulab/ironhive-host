# Console Chat Sample Launcher
# IronHive.Cli.Core 라이브러리 직접 통합 샘플

param(
    [string]$Provider = "env",  # env, mock, openai, gpustack, ollama
    [string]$Model = ""
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Console Chat Sample" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Provider별 환경 변수 설정
switch ($Provider.ToLower()) {
    "env" {
        # .env 파일 사용 (앱 내부에서 자동 로드)
        Write-Host "Provider: .env 파일 사용" -ForegroundColor Green
        Write-Host "(.env 또는 .env.local 파일을 현재/상위 디렉토리에서 탐색)" -ForegroundColor DarkGray
    }
    "openai" {
        if (-not $env:OPENAI_API_KEY) {
            Write-Host "OPENAI_API_KEY 환경변수를 설정하세요." -ForegroundColor Red
            Write-Host 'Example: $env:OPENAI_API_KEY="sk-xxx"' -ForegroundColor Yellow
            exit 1
        }
        $env:OPENAI_MODEL = if ($Model) { $Model } else { "gpt-4o-mini" }
        Write-Host "Provider: OpenAI" -ForegroundColor Green
        Write-Host "Model: $env:OPENAI_MODEL" -ForegroundColor Green
    }
    "gpustack" {
        $env:OPENAI_API_KEY = if ($env:GPUSTACK_API_KEY) { $env:GPUSTACK_API_KEY } else { "gpustack" }
        $env:OPENAI_ENDPOINT = if ($env:GPUSTACK_ENDPOINT) { $env:GPUSTACK_ENDPOINT } else { "http://172.30.1.53:8080/v1" }
        $env:OPENAI_MODEL = if ($Model) { $Model } elseif ($env:GPUSTACK_MODEL) { $env:GPUSTACK_MODEL } else { "qwen3-30b-a3b" }
        Write-Host "Provider: GpuStack ($env:OPENAI_ENDPOINT)" -ForegroundColor Green
        Write-Host "Model: $env:OPENAI_MODEL" -ForegroundColor Green
    }
    "ollama" {
        $env:OPENAI_API_KEY = "ollama"
        $env:OPENAI_ENDPOINT = if ($env:OLLAMA_ENDPOINT) { $env:OLLAMA_ENDPOINT } else { "http://localhost:11434/v1" }
        $env:OPENAI_MODEL = if ($Model) { $Model } else { "llama3.2" }
        Write-Host "Provider: Ollama ($env:OPENAI_ENDPOINT)" -ForegroundColor Green
        Write-Host "Model: $env:OPENAI_MODEL" -ForegroundColor Green
    }
    default {
        # Mock - 환경 변수 제거
        Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue
        Remove-Item Env:OPENAI_ENDPOINT -ErrorAction SilentlyContinue
        Remove-Item Env:OPENAI_MODEL -ErrorAction SilentlyContinue
        Write-Host "Provider: Mock (테스트용)" -ForegroundColor Yellow
    }
}

Write-Host ""

# 실행
Push-Location $PSScriptRoot
try {
    dotnet run
}
finally {
    Pop-Location
}
