# 배포용 실행 파일 만들기
#
#   PowerShell 에서 솔루션 폴더 기준으로:
#     .\tools\publish.ps1                 # 작은 실행 파일 (.NET 10 Desktop Runtime 필요 — VS 2026 깔려 있으면 이미 있음)
#     .\tools\publish.ps1 -SelfContained  # .NET 설치 없이 도는 실행 파일 (용량 큼)
#
#   결과: publish\PadAgent.exe, publish\PadFlasher.exe, publish\macropad.example.json, publish\configs\
#   ※ publish 폴더가 솔루션 폴더 안에 있으면, 실행 파일은 솔루션 폴더의 macropad.json 을 그대로 씀.
#     다른 곳으로 옮기면 옆에 있는 macropad.json 을 씀 (없으면 macropad.example.json 을 복사해서 만듦).

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
    '-p:DebugType=none',          # PDB/빌드 경로(C:\Users\<사용자>\…)가 exe에 남지 않게
    '-p:IncludeNativeLibrariesForSelfExtract=true',  # WebView2Loader.dll 도 exe 하나에
    '--self-contained', $SelfContained.IsPresent.ToString().ToLower()
)

dotnet publish (Join-Path $root 'src\PadAgent\PadAgent.csproj') @common
if ($LASTEXITCODE -ne 0) { throw "PadAgent publish 실패" }

dotnet publish (Join-Path $root 'src\PadFlasher\PadFlasher.csproj') @common
if ($LASTEXITCODE -ne 0) { throw "PadFlasher publish 실패" }

Write-Host ""
Write-Host "완료: $out" -ForegroundColor Green
