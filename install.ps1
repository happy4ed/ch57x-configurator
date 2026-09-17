<#
    CH57x 설정기 — 한 줄 설치 스크립트

    쓰는 법 (PowerShell 에 한 줄):
      [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $p="$env:TEMP\ch57x-install.ps1"; iwr -UseBasicParsing 'https://raw.githubusercontent.com/happy4ed/ch57x-configurator/master/install.ps1' -OutFile $p; Unblock-File $p; powershell -NoProfile -ExecutionPolicy Bypass -File $p

    하는 일
      GitHub 최신 릴리스에서 zip 을 받아 %LOCALAPPDATA%\Programs\Ch57x 에 exe 하나만 덮어쓰고 실행한다.

    설계 원칙 (왜 이렇게 하는가)
      - 관리자 권한을 요구하지 않는다. 설치 위치가 사용자별 폴더라 권한 상승이 필요 없다.
      - 설치 폴더를 통째로 지우지 않는다. exe 만 덮어쓴다 — 사용자 데이터 보존.
        (프로필은 %AppData%\Ch57x 에 따로 있어 어차피 영향받지 않지만, 폴더를 지우는 습관 자체를 두지 않는다.)
      - 설치 위치는 앱 코드가 진본이다. windows/Ch57x.App/AutoStart.cs:
            public static string StableFolder => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Ch57x");
            public static string StableExePath => Path.Combine(StableFolder, "CH57x 설정기.exe");
        여기와 다른 곳에 깔면 앱이 자동시작을 등록한 경로와 어긋난다.
      - 받은 파일의 출처 표식(Mark of the Web)을 지운다. 이 표식이 남아 있으면 실행할 때마다
        보안 경고가 뜨고, 2026-09-13 에는 임시 폴더에서 실행된 사본이 재부팅에 사라져
        자동시작이 통째로 소실된 적이 있다. 고정 위치 설치 + 표식 제거가 그 두 가지를 같이 막는다.

    매개변수
      -Slim        .NET 8 데스크톱 런타임이 이미 있는 PC 용 작은 zip 을 받는다. 기본은 포터블(런타임 포함).
      -Tag v0.2.0  특정 릴리스를 받는다. 지정하지 않으면 최신(latest).
#>
[CmdletBinding()]
param(
    [switch]$Slim,
    [string]$Tag
)

$ErrorActionPreference = 'Stop'

# 콘솔이 한글을 깨뜨리지 않게 UTF-8 로 맞춘다. 옛 호스트에서는 실패할 수 있고, 실패해도 설치는 계속한다.
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch { }

# Windows PowerShell 5.1 은 진행률 막대를 그리느라 다운로드가 몇 배 느려진다. 꺼 두고 우리가 직접 안내한다.
$ProgressPreference = 'SilentlyContinue'

# 구형 Windows 10 의 PowerShell 5.1 은 기본 프로토콜이 TLS1.2 가 아니라 GitHub 연결이 끊긴다.
try {
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
} catch { }

$Owner      = 'happy4ed'
$Repo       = 'ch57x-configurator'
$TargetName = 'CH57x 설정기.exe'          # AutoStart.StableExePath 와 같은 이름이어야 한다
$ProcName   = 'CH57x 설정기'              # csproj 의 <AssemblyName> 과 같다 = 프로세스 이름
$Suffix     = if ($Slim) { '-slim-win-x64.zip' } else { '-portable-win-x64.zip' }
$MinBytes   = if ($Slim) { 50KB } else { 1MB }   # 이보다 작으면 zip 이 아니라 오류 페이지를 받은 것이다
$TotalSteps = 7
$TempRoot   = $null

function Write-Step {
    param([int]$Index, [string]$Message)
    Write-Host ("[{0}/{1}] {2}" -f $Index, $TotalSteps, $Message)
}

function Write-Note {
    param([string]$Message)
    Write-Host ("      " + $Message) -ForegroundColor DarkGray
}

# 설치 폴더 안에서 돌고 있는 프로세스를 찾는다. 이름이 바뀐 사본까지 잡기 위해 경로로도 본다.
# 보호된 프로세스의 Path 를 읽으면 예외가 나므로 하나씩 감싼다.
function Get-RunningInFolder {
    param([string]$Folder)
    $found = @()
    foreach ($p in (Get-Process -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $p.Path } catch { $path = $null }
        if ($path -and $path.StartsWith($Folder, [System.StringComparison]::OrdinalIgnoreCase)) {
            $found += $p
        }
    }
    return $found
}

try {
    Write-Host ""
    Write-Host "CH57x 설정기 설치를 시작합니다." -ForegroundColor Cyan
    Write-Host ""

    # ── [1/7] 이 PC 에서 설치가 가능한지 ────────────────────────────────────────
    Write-Step 1 "이 PC 에서 설치할 수 있는지 확인합니다."

    if ($PSVersionTable.PSVersion.Major -lt 5) {
        throw "이 스크립트는 PowerShell 5 이상이 필요합니다. 지금 버전은 $($PSVersionTable.PSVersion) 입니다. Windows 10 이상이면 기본 PowerShell 로 다시 실행해 주세요."
    }
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw "이 프로그램은 64비트 Windows 에서만 동작합니다. 지금 PC 는 32비트 Windows 로 보입니다."
    }

    $dest    = Join-Path $env:LOCALAPPDATA 'Programs\Ch57x'
    $destExe = Join-Path $dest $TargetName
    Write-Note "설치 위치: $dest"

    # ── [2/7] 어떤 파일을 받을지 ────────────────────────────────────────────────
    Write-Step 2 "최신 버전 정보를 확인합니다."

    $apiUrl = if ($Tag) {
        "https://api.github.com/repos/$Owner/$Repo/releases/tags/$Tag"
    } else {
        "https://api.github.com/repos/$Owner/$Repo/releases/latest"
    }

    $downloadUrl = $null
    $assetName   = $null
    $releaseName = $null

    try {
        # User-Agent 가 없으면 GitHub API 가 403 을 돌려준다.
        $release = Invoke-RestMethod -Uri $apiUrl -Headers @{ 'User-Agent' = 'ch57x-install' } -UseBasicParsing
        $releaseName = $release.tag_name

        # 자산 이름 전체를 하드코딩하지 않는다 — v0.1.0 에서 이름이 'CH57x-.-portable-win-x64.zip' 로
        # 깨져 나간 이력이 있다. 끝나는 모양(-portable-win-x64.zip)만 보고 고른다.
        foreach ($a in $release.assets) {
            if ($a.name -and $a.name.EndsWith($Suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
                $downloadUrl = $a.browser_download_url
                $assetName   = $a.name
                break
            }
        }
        if (-not $downloadUrl) {
            throw "릴리스 $releaseName 에 '$Suffix' 로 끝나는 파일이 없습니다."
        }
    } catch {
        # 요청 한도·회사 방화벽 등으로 API 가 막히는 경우가 있다. 고정 주소로 한 번 더 시도한다.
        Write-Note "릴리스 정보를 읽지 못해 고정 주소로 내려받습니다. (원인: $($_.Exception.Message))"
        $fixedName = if ($Slim) { 'CH57x-Configurator-slim-win-x64.zip' } else { 'CH57x-Configurator-portable-win-x64.zip' }
        $downloadUrl = if ($Tag) {
            "https://github.com/$Owner/$Repo/releases/download/$Tag/$fixedName"
        } else {
            "https://github.com/$Owner/$Repo/releases/latest/download/$fixedName"
        }
        $assetName   = $fixedName
        $releaseName = if ($Tag) { $Tag } else { 'latest' }
    }

    Write-Note "받을 파일: $assetName ($releaseName)"

    # ── [3/7] 내려받기 ──────────────────────────────────────────────────────────
    $sizeHint = if ($Slim) { "작은 파일이라 금방 끝납니다." } else { "포터블 버전은 60MB 가 넘어 잠시 걸립니다." }
    Write-Step 3 "파일을 내려받습니다. $sizeHint"

    $TempRoot = Join-Path $env:TEMP ("ch57x-install-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null
    $zipPath = Join-Path $TempRoot 'app.zip'

    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -UseBasicParsing -Headers @{ 'User-Agent' = 'ch57x-install' }
    } catch {
        throw "GitHub 에서 파일을 받지 못했습니다. 주소가 없거나(-Tag 로 지정한 버전 이름 확인), 인터넷 연결·회사 방화벽 문제일 수 있습니다. (원인: $($_.Exception.Message))"
    }

    if (-not (Test-Path $zipPath)) {
        throw "내려받은 파일이 보이지 않습니다. 잠시 뒤 다시 실행해 주세요."
    }
    $zipSize = (Get-Item $zipPath).Length
    if ($zipSize -lt $MinBytes) {
        throw "내려받은 파일이 너무 작습니다($zipSize 바이트). 파일 대신 오류 안내를 받았을 수 있습니다. 잠시 뒤 다시 실행하거나 https://github.com/$Owner/$Repo/releases 에서 직접 받아 주세요."
    }

    # 인터넷에서 받은 파일에 붙는 출처 표식을 지운다. 이게 남으면 압축을 푼 exe 에도 따라붙어
    # 실행할 때마다 보안 경고가 뜬다.
    try { Unblock-File -Path $zipPath } catch { }
    Write-Note ("내려받기 완료: {0:N1} MB" -f ($zipSize / 1MB))

    # ── [4/7] 실행 중이면 먼저 닫기 ─────────────────────────────────────────────
    Write-Step 4 "실행 중인 CH57x 설정기가 있으면 닫습니다."

    $running = @()
    $running += Get-Process -Name $ProcName -ErrorAction SilentlyContinue
    if (Test-Path $dest) { $running += Get-RunningInFolder -Folder $dest }
    $running = $running | Sort-Object -Property Id -Unique

    if ($running.Count -gt 0) {
        foreach ($p in $running) {
            try { $p.CloseMainWindow() | Out-Null } catch { }
        }
        # 트레이 상주라 창이 없을 수 있다. 최대 5초 기다렸다가 남아 있으면 강제 종료한다.
        for ($i = 0; $i -lt 10; $i++) {
            Start-Sleep -Milliseconds 500
            $alive = $running | Where-Object { -not $_.HasExited }
            if (-not $alive -or $alive.Count -eq 0) { break }
        }
        foreach ($p in $running) {
            if (-not $p.HasExited) {
                try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
            }
        }
        Start-Sleep -Milliseconds 500

        $still = @()
        $still += Get-Process -Name $ProcName -ErrorAction SilentlyContinue
        if (Test-Path $dest) { $still += Get-RunningInFolder -Folder $dest }
        if ($still.Count -gt 0) {
            throw "CH57x 설정기가 아직 실행 중이라 덮어쓸 수 없습니다. 트레이 아이콘에서 직접 종료한 뒤 이 명령을 다시 실행해 주세요."
        }
        Write-Note "실행 중이던 프로그램을 닫았습니다."
    } else {
        Write-Note "실행 중인 프로그램이 없습니다."
    }

    # ── [5/7] 압축 풀기 ─────────────────────────────────────────────────────────
    Write-Step 5 "압축을 풉니다."

    $unzipDir = Join-Path $TempRoot 'unzip'
    New-Item -ItemType Directory -Path $unzipDir -Force | Out-Null
    try {
        Expand-Archive -Path $zipPath -DestinationPath $unzipDir -Force
    } catch {
        throw "압축을 푸는 데 실패했습니다. 파일이 받다 만 상태일 수 있습니다. 다시 실행해 주세요. (원인: $($_.Exception.Message))"
    }

    # 파일 이름이 한글이라 옛 압축 도구에서 깨져 나올 수 있다. 이름으로 찾지 않고 exe 를 모아 고른다.
    $exeList = @(Get-ChildItem -Path $unzipDir -Recurse -Filter *.exe -File -ErrorAction SilentlyContinue)
    if ($exeList.Count -eq 0) {
        throw "압축 안에 실행 파일이 없습니다. 릴리스 파일이 잘못 올라갔을 수 있습니다. https://github.com/$Owner/$Repo/releases 에서 확인해 주세요."
    }
    $srcExe = $exeList | Sort-Object Length -Descending | Select-Object -First 1
    Write-Note "실행 파일 확인: $($srcExe.Name)"

    # ── [6/7] 설치 ──────────────────────────────────────────────────────────────
    Write-Step 6 "설치합니다. 기존 설정과 프로필은 건드리지 않습니다."

    # 폴더는 있으면 그대로 쓴다. 지우지 않는다 — 사용자 데이터 보존.
    New-Item -ItemType Directory -Path $dest -Force | Out-Null

    try {
        Copy-Item -LiteralPath $srcExe.FullName -Destination $destExe -Force
    } catch {
        throw "설치 폴더에 파일을 쓰지 못했습니다. 백신이 막고 있거나 프로그램이 아직 실행 중일 수 있습니다. (원인: $($_.Exception.Message))"
    }

    # 출처 표식 제거 — 이것 때문에 실행할 때마다 경고가 떴다. 두 가지 방법을 다 쓴다.
    try { Unblock-File -LiteralPath $destExe } catch { }
    try { Remove-Item -LiteralPath $destExe -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue } catch { }

    try { Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    $TempRoot = $null
    Write-Note "설치 완료: $destExe"

    # ── [7/7] 실행 ──────────────────────────────────────────────────────────────
    Write-Step 7 "프로그램을 실행합니다."
    try {
        Start-Process -FilePath $destExe
    } catch {
        Write-Note "자동 실행에 실패했습니다. 탐색기에서 직접 실행해 주세요: $destExe (원인: $($_.Exception.Message))"
    }

    Write-Host ""
    Write-Host "설치가 끝났습니다." -ForegroundColor Green
    Write-Host "  · 창은 뜨지 않습니다. 작업 표시줄 오른쪽 아래 트레이 아이콘(숨은 아이콘 표시 ^)에서 찾으세요."
    Write-Host "  · 윈도우 시작할 때 자동 실행: 트레이 아이콘 우클릭 → 「윈도우 시작 시 자동실행」."
    Write-Host "  · 프로필은 %AppData%\Ch57x 에 있고, 이 설치로 지워지지 않습니다."
    Write-Host "  · 문제가 생기면 트레이 메뉴 「정보」 창에서 「정보 복사」 를 눌러 그 내용을 알려 주세요."
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Host "설치를 마치지 못했습니다." -ForegroundColor Red
    Write-Host ("  " + $_.Exception.Message) -ForegroundColor Red
    Write-Host "  해결되지 않으면 https://github.com/$Owner/$Repo/releases 에서 zip 을 직접 받아 압축을 풀고 실행해 주세요."
    Write-Host ""
    if ($TempRoot -and (Test-Path $TempRoot)) {
        try { Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    }
    exit 1
}
