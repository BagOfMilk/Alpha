<#
.SYNOPSIS
  Збірка Ring.exe у пакетному режимі Unity — з Burst, без Development, без вікон Windows.

.DESCRIPTION
  P0-3 (docs/ring/playtest_2026-11-08_prompt.md): збірки падали на кроці AotNativeLinkLLVMWindows.Link —
  «Burst internal compiler error: UnauthorizedAccessException … Path.InternalGetTempFileName». Причина: Unity, запущений
  з пісочниці Claude, має низьку цілісність і не може писати у звичайну %TEMP%, а bcl.exe Burst бере тимчасовий файл
  саме там. У LocalLow писати може. Тому скрипт дає Unity свої TMP і TEMP у %USERPROFILE%\AppData\LocalLow\RingTmp
  (той самий прийом, що й tools/ring-editor.ps1, 19e7f9a).

  Інше, що скрипт стереже:
    - Burst не вимкнений: ProjectSettings/BurstAotSettings_StandaloneWindows.json з EnableBurstCompilation: false
      (так обходили P0-3 08.11.2026) — відмова, якщо не передано -AllowNoBurst;
    - проєкт не відкритий іншим Unity (редактор чи інша збірка) — інакше Unity сам відмовить;
    - запуск без оболонки Windows (UseShellExecute = false) — без «Open File — Security Warning»;
    - після збірки: є Ring_Data/Plugins/x86_64/lib_burst_generated.dll (код Burst AOT), у boot.config немає
      player-connection (Development відкриває мережевий порт), у журналі немає помилок Burst.

  Перед запуском — рядок «старт» сесії-ПМ (черга Unity, docs/ring/ZONES.md), після — «вільно».

  Запуск (з Bash-інструмента):
    powershell -ExecutionPolicy Bypass -File tools/ring-build.ps1                     # Build/Ring/Ring.exe
    powershell -ExecutionPolicy Bypass -File tools/ring-build.ps1 -Method BuildPlayerShow

  Коди виходу: 0 — зібрано з Burst; 5 — Unity не знайдено або впав; 6 — помилки компіляції C#; 9 — тайм-аут;
  10 — проєкт зайнятий іншим Unity; 11 — Burst вимкнено в налаштуваннях; 12 — збірка не вдалась;
  13 — зібрано, але без коду Burst або з помилками Burst у журналі; 14 — у збірці лишився мережевий порт.
#>
param(
    [ValidateSet('BuildPlayer', 'BuildPlayerShow', 'BuildPlayerDev')]
    [string]$Method = 'BuildPlayer',
    [string]$ProjectPath = '',
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor\Unity.exe',
    [int]$TimeoutMinutes = 90,
    [switch]$AllowNoBurst
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
if (-not $ProjectPath) { $ProjectPath = Split-Path $PSScriptRoot -Parent }
$ProjectPath = (Resolve-Path $ProjectPath).Path
$folder = @{ BuildPlayer = 'Ring'; BuildPlayerShow = 'RingShow'; BuildPlayerDev = 'RingDev' }[$Method]
$exe = Join-Path $ProjectPath "Build\$folder\Ring.exe"

if (-not (Test-Path $UnityPath)) { Write-Host "Unity не знайдено: $UnityPath (передай -UnityPath)"; exit 5 }

# 1. Проєкт не має бути відкритий іншим Unity.
$norm = $ProjectPath.TrimEnd('\').Replace('\', '/').ToLowerInvariant()
foreach ($p in Get-CimInstance Win32_Process -Filter "Name='Unity.exe'") {
    $c = ([string]$p.CommandLine).Replace('\', '/').ToLowerInvariant()
    if ($c.Contains($norm + '"') -or $c.Contains($norm + ' ') -or $c.EndsWith($norm)) {
        Write-Host "Проєкт уже відкритий Unity (PID $($p.ProcessId)). Закрий редактор (tools/ring-editor.ps1 -Action quit) або дочекайся збірки."
        exit 10
    }
}

# 2. Burst не вимкнений.
$burstJson = Join-Path $ProjectPath 'ProjectSettings\BurstAotSettings_StandaloneWindows.json'
$burstOff = $false
if (Test-Path $burstJson) {
    $raw = Get-Content -Raw -Encoding UTF8 $burstJson
    if ($raw -match '"EnableBurstCompilation"\s*:\s*false') { $burstOff = $true }
}
if ($burstOff -and -not $AllowNoBurst) {
    Write-Host "Burst вимкнено: $burstJson (EnableBurstCompilation: false). Це обхід P0-3 — прибери файл або передай -AllowNoBurst."
    exit 11
}
Write-Host ("Burst: " + $(if ($burstOff) { 'ВИМКНЕНО (-AllowNoBurst)' } else { 'увімкнено' }))

# 3. Тимчасова тека, у яку Unity з низькою цілісністю може писати.
$tmp = Join-Path $env:USERPROFILE 'AppData\LocalLow\RingTmp'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $ProjectPath "Logs\ring_build_$($Method)_$stamp.log"
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
$before = if (Test-Path $exe) { (Get-Item $exe).LastWriteTimeUtc } else { [DateTime]::MinValue }

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $UnityPath
$psi.Arguments = "-quit -batchmode -projectPath `"$ProjectPath`" -logFile `"$log`" -executeMethod Game.Gameplay.EditorTools.RingSceneBuilder.$Method"
$psi.UseShellExecute = $false
$psi.EnvironmentVariables['TMP'] = $tmp
$psi.EnvironmentVariables['TEMP'] = $tmp
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$p = [System.Diagnostics.Process]::Start($psi)
Write-Host "Збірка $Method (PID $($p.Id)), журнал: $log"
if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Write-Host "Збірка не скінчилась за $TimeoutMinutes хв — зупинено."
    exit 9
}
$code = $p.ExitCode
Write-Host ("Unity завершився з кодом {0} за {1:N0} с." -f $code, $sw.Elapsed.TotalSeconds)

# 4. Розбір журналу.
$lines = if (Test-Path $log) { Get-Content -Encoding UTF8 $log } else { @() }
$csErr = @($lines | Where-Object { $_ -match 'error CS\d{4}' } | Select-Object -Unique)
$burstErr = @($lines | Where-Object { $_ -match '(?i)burst' -and $_ -match '(?i)(error|exception|failed)' -and $_ -notmatch '(?i)0 error' } | Select-Object -Unique)
$result = @($lines | Where-Object { $_ -match '\[Ринг\] Білд:|Build Finished, Result:' })
if ($result.Count) { $result | ForEach-Object { Write-Host "  $_" } }
if ($csErr.Count) {
    Write-Host "Помилки компіляції C# ($($csErr.Count)):"
    $csErr | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" }
    exit 6
}
if ($code -ne 0 -or -not (Test-Path $exe) -or (Get-Item $exe).LastWriteTimeUtc -le $before) {
    Write-Host "Збірка не вдалась. Останні рядки з помилками:"
    $lines | Where-Object { $_ -match '(?i)error|exception|failed' } | Select-Object -Last 15 | ForEach-Object { Write-Host "  $_" }
    exit 12
}

# 5. Що в збірці.
$data = Join-Path (Split-Path $exe) 'Ring_Data'
$burstDll = Join-Path $data 'Plugins\x86_64\lib_burst_generated.dll'
$hasBurst = Test-Path $burstDll
$boot = Join-Path $data 'boot.config'
$netPort = (Test-Path $boot) -and ((Get-Content -Raw $boot) -match '(?i)player-connection')
Write-Host ("Збірка: {0} ({1:N1} МБ теки)" -f $exe, ((Get-ChildItem -Recurse (Split-Path $exe) | Measure-Object Length -Sum).Sum / 1MB))
Write-Host ("Код Burst AOT: " + $(if ($hasBurst) { "є ($([int]((Get-Item $burstDll).Length / 1KB)) КБ)" } else { 'НЕМАЄ' }))
Write-Host ("Мережевий порт (player-connection у boot.config): " + $(if ($netPort) { 'Є' } else { 'немає' }))
if ($burstErr.Count) {
    Write-Host "Рядки журналу про Burst з помилками ($($burstErr.Count)):"
    $burstErr | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" }
}
if ($netPort -and $Method -ne 'BuildPlayerDev') { exit 14 }
if (-not $burstOff -and (-not $hasBurst -or $burstErr.Count)) { exit 13 }
exit 0
