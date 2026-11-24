# 배포용 실행 파일 만들기
#
#   가장 쉬운 방법: 솔루션 폴더의 publish.cmd 더블클릭 (PowerShell 스크립트 실행 정책과 상관없이 실행됨)
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
$requiredSdkMajor = 10   # Directory.Build.props 의 MacroPadDotNet(net10.0) 과 맞춤

# 탐색기에서 띄우면("PowerShell에서 실행" 또는 publish.cmd 더블클릭) 끝나자마자 창이 닫혀서 오류를 읽을 수 없음 → 그때만 멈춤
function Test-LaunchedFromExplorer {
    try {
        $procId = $PID
        for ($i = 0; $i -lt 2; $i++) {   # powershell ← explorer, 또는 powershell ← cmd(publish.cmd) ← explorer
            $procId = (Get-CimInstance Win32_Process -Filter "ProcessId=$procId").ParentProcessId
            $name = (Get-Process -Id $procId -ErrorAction Stop).ProcessName
            if ($name -eq 'explorer') { return $true }
            if ($name -ne 'cmd') { return $false }
        }
        return $false
    } catch {
        return $false
    }
}
$pauseAtEnd = -not $env:CI -and (Test-LaunchedFromExplorer)

function Assert-DotNetSdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw @"
'dotnet' 명령을 찾을 수 없습니다.
  1) .NET $requiredSdkMajor SDK 를 설치하십시오: https://dotnet.microsoft.com/download/dotnet/$requiredSdkMajor.0
     (오른쪽 'Runtime' 이 아니라 왼쪽 'SDK' 를 받아야 합니다)
  2) 설치 후 PowerShell 창을 새로 열고 다시 실행하십시오.
"@
    }

    $sdks = @(& dotnet --list-sdks)
    $majors = @($sdks | ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } })
    if (-not ($majors | Where-Object { $_ -ge $requiredSdkMajor })) {
        $found = if ($sdks.Count) { ($sdks | ForEach-Object { "    $_" }) -join "`n" } else { '    (없음 — Runtime 만 설치된 상태입니다)' }
        throw @"
.NET $requiredSdkMajor SDK 가 없습니다. 실행 파일을 만들려면 Runtime 이 아니라 SDK 가 필요합니다.
  설치된 SDK:
$found
  다운로드: https://dotnet.microsoft.com/download/dotnet/$requiredSdkMajor.0  (왼쪽 'SDK' 의 Windows x64 설치 관리자)
  설치 후 PowerShell 창을 새로 열고 다시 실행하십시오.
"@
    }
}

$common = @(
    '-c', 'Release',
    '-r', 'win-x64',
    '-o', $out,
    '-p:PublishSingleFile=true',
    '-p:DebugType=none',          # PDB/빌드 경로(C:\Users\<사용자>\…)가 exe에 남지 않게
    '-p:IncludeNativeLibrariesForSelfExtract=true',  # WebView2Loader.dll 도 exe 하나에
    '--self-contained', $SelfContained.IsPresent.ToString().ToLower()
)

$failed = $false
try {
    Assert-DotNetSdk

    dotnet publish (Join-Path $root 'src\PadAgent\PadAgent.csproj') @common
    if ($LASTEXITCODE -ne 0) { throw "PadAgent publish 실패 (위쪽 빨간 error 줄을 확인하십시오)" }

    dotnet publish (Join-Path $root 'src\PadFlasher\PadFlasher.csproj') @common
    if ($LASTEXITCODE -ne 0) { throw "PadFlasher publish 실패 (위쪽 빨간 error 줄을 확인하십시오)" }

    Write-Host ""
    Write-Host "완료: $out" -ForegroundColor Green
} catch {
    $failed = $true
    Write-Host ""
    Write-Host $_.Exception.Message -ForegroundColor Red
} finally {
    if ($pauseAtEnd) {
        Write-Host ""
        Read-Host "Enter 를 누르면 창이 닫힙니다" | Out-Null
    }
}

if ($failed) { exit 1 }
