# 배포용 실행 파일 만들기
#
#   PowerShell 에서 솔루션 폴더 기준으로:
#     .\tools\publish.ps1                 # 작은 실행 파일 (.NET 10 Desktop Runtime 필요 — VS 2026 깔려 있으면 이미 있음)
#     .\tools\publish.ps1 -SelfContained  # .NET 설치 없이 도는 실행 파일 (용량 큼)
#
#   결과: publish\PadAgent.exe, publish\PadFlasher.exe, publish\macropad.json, publish\configs\
#   ※ publish 폴더가 솔루션 폴더 안에 있으면, 실행 파일은 솔루션 폴더의 macropad.json 을 그대로 씀.
#     다른 곳으로 옮기면 옆에 있는 macropad.json 을 씀.

param(
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'publish'

$common = @(
    '-c', 'Release',
    '-r', 'win-x64',
    '-o', $out,
    '-p:PublishSingleFile=true',
    '--self-contained', $SelfContained.IsPresent.ToString().ToLower()
)

if ($SelfContained) {
    $common += '-p:IncludeNativeLibrariesForSelfExtract=true'
}

dotnet publish (Join-Path $root 'src\PadAgent\PadAgent.csproj') @common
if ($LASTEXITCODE -ne 0) { throw "PadAgent publish 실패" }

dotnet publish (Join-Path $root 'src\PadFlasher\PadFlasher.csproj') @common
if ($LASTEXITCODE -ne 0) { throw "PadFlasher publish 실패" }

Write-Host ""
Write-Host "완료: $out" -ForegroundColor Green
