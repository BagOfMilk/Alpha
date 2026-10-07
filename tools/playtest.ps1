# Рутина плейтесту власника (docs/PLAYTEST.md): збирає білд ЛИШЕ коли змінився код чи асети,
# запускає гру з репортером нотаток (F8) і в режимі низького навантаження, після гри
# переносить сесію в <репо>\Playtest\ і показує зведення.
#
#   powershell -File tools/playtest.ps1            # зібрати, якщо треба, і грати
#   powershell -File tools/playtest.ps1 -NoBuild   # грати наявним білдом
#   powershell -File tools/playtest.ps1 -Rebuild   # зібрати наново примусово
#   powershell -File tools/playtest.ps1 -Full      # без обмеження кадрів (перевірити вигляд «на повну»)
#   powershell -File tools/playtest.ps1 -Digest    # лише показати останню сесію (нотатки й помилки)
param(
    [switch]$NoBuild,
    [switch]$Rebuild,
    [switch]$Full,
    [switch]$Digest,
    [int]$Width = 1600,
    [int]$Height = 900
)

$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent
$out = Join-Path $proj "Build\Windows"
$exe = Join-Path $out "Alpha.exe"
$stamp = Join-Path $out "build_commit.txt"
$sessions = Join-Path $proj "Playtest"

function Show-Latest {
    $last = Get-ChildItem $sessions -Directory -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
    if (-not $last) { Write-Host "Сесій плейтесту ще немає." -ForegroundColor Yellow; return }
    $notes = @(Get-ChildItem $last.FullName -Filter "*.txt" | Where-Object { $_.Name -match '^\d{3}\.txt$' }).Count
    Write-Host "Сесія: $($last.FullName)  (нотаток: $notes)" -ForegroundColor Cyan
    $md = Join-Path $last.FullName "notes.md"
    if (Test-Path $md) { Get-Content $md -Encoding UTF8 | Select-Object -Skip 6 }
    $err = Join-Path $last.FullName "errors.md"
    if (Test-Path $err) { Get-Content $err -Encoding UTF8 }
}

if ($Digest) { Show-Latest; exit 0 }

# --- 1. Чи треба збирати: коміт білда проти поточного + незакомічені зміни в Assets ---
Push-Location $proj
$head = (git rev-parse --short HEAD).Trim()
$dirty = (git status --porcelain -- Assets Packages ProjectSettings) -ne $null
Pop-Location
$want = if ($dirty) { "$head+зміни" } else { $head }
$have = if (Test-Path $stamp) { (Get-Content $stamp -Raw).Trim() } else { "" }
$needBuild = $Rebuild -or -not (Test-Path $exe) -or ($have -ne $want) -or $dirty

if ($NoBuild -and -not (Test-Path $exe)) { Write-Host "Білда немає — запусти без -NoBuild." -ForegroundColor Red; exit 1 }

if ($needBuild -and -not $NoBuild) {
    Write-Host "Збираю білд ($want; було: $(if ($have) { $have } else { 'нічого' })) — Unity у фоні, низький пріоритет..." -ForegroundColor Cyan
    & powershell -NoProfile -File (Join-Path $PSScriptRoot "build-unity.ps1") -LowPriority
    if ($LASTEXITCODE -ne 0) { Write-Host "Збирання впало — лог у Logs\. Скажи Клоду «розбери збірку»." -ForegroundColor Red; exit 1 }
    Set-Content -Path $stamp -Value $want -Encoding UTF8
} else {
    Write-Host "Білд актуальний ($have) — без збирання." -ForegroundColor Green
}

# --- 2. Гра з репортером ---
$gameArgs = @('-playtest', '-screen-fullscreen', '0', '-screen-width', "$Width", '-screen-height', "$Height")
if (-not $Full) { $gameArgs += '-lowcpu' }
Write-Host "Грай. F8 — нотатка (тип, суть, Enter). Нічого не лагодимо під час гри — лише записуємо." -ForegroundColor Yellow
$p = Start-Process -FilePath $exe -ArgumentList $gameArgs -PassThru
$p.WaitForExit()

# --- 3. Перенести сесії з теки білда в репозиторій (білд можна перезбирати, нотатки лишаються) ---
$src = Join-Path $out "Playtest"
if (Test-Path $src) {
    New-Item -ItemType Directory -Force -Path $sessions | Out-Null
    Get-ChildItem $src -Directory | ForEach-Object {
        $dst = Join-Path $sessions $_.Name
        if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
        Move-Item $_.FullName $dst
    }
}
$log = Join-Path $env:USERPROFILE "AppData\LocalLow\DefaultCompany\Alpha\Player.log"
$last = Get-ChildItem $sessions -Directory -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
if ($last -and (Test-Path $log)) { Copy-Item $log (Join-Path $last.FullName "Player.log") -Force }

Show-Latest
Write-Host ""
Write-Host "Далі: скажи Клоду «розбери плейтест» — він прочитає нотатки й виправить усе однією пачкою." -ForegroundColor Green
