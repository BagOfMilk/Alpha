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
    - перед збіркою — окремий запуск Unity лише для імпорту й компіляції: перша збірка плеєра після зміни скриптів
      пише стару сцену («level0 is corrupted», 08.11.2026; чистка Library/Bee не лікує). -SkipCompilePass — без нього;
    - після збірки: є Ring_Data/Plugins/x86_64/lib_burst_generated.dll (код Burst AOT), у boot.config немає
      player-connection (Development відкриває мережевий порт), у журналі немає помилок Burst.

  Перед запуском — рядок «старт» сесії-ПМ (черга Unity, docs/ring/ZONES.md), після — «вільно».

  Запуск (з Bash-інструмента):
    powershell -ExecutionPolicy Bypass -File tools/ring-build.ps1                     # Build/Ring/Ring.exe
    powershell -ExecutionPolicy Bypass -File tools/ring-build.ps1 -Method BuildPlayerShow
    powershell -ExecutionPolicy Bypass -File tools/ring-build.ps1 -ReviewVenues          # + клубна зала й палац (-ring-venue hall|arena)

  -ReviewVenues: Unity отримує RING_REVIEW_VENUES=1 — у сцені, крім спортзалу, ще клубна зала й палац (RingStageSceneSetup,
  лише для оглядів і заміру FPS у палаці; гра й звичайні збірки — без них, Статут PERF-01). Без перемикача змінна
  примусово прибирається, навіть якщо є в оболонці, — звичайна збірка не тягне зайвих FBX.

  Коди виходу: 0 — зібрано з Burst; 5 — Unity не знайдено або прохід компіляції впав; 6 — помилки компіляції C#; 9 — тайм-аут;
  10 — проєкт зайнятий іншим Unity; 11 — Burst вимкнено в налаштуваннях; 12 — збірка не вдалась;
  13 — зібрано, але без коду Burst або з помилками Burst у журналі; 14 — у збірці лишився мережевий порт;
  15 — з -ReviewVenues, а клубної зали й палацу в сцені немає.
#>
param(
    [ValidateSet('BuildPlayer', 'BuildPlayerShow', 'BuildPlayerDev')]
    [string]$Method = 'BuildPlayer',
    [string]$ProjectPath = '',
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor\Unity.exe',
    [int]$TimeoutMinutes = 90,
    [switch]$AllowNoBurst,
    [switch]$SkipCompilePass,
    [switch]$ReviewVenues
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

function Invoke-Unity([string]$UnityArgs, [string]$What) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $UnityPath
    $psi.Arguments = $UnityArgs
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables['TMP'] = $tmp
    $psi.EnvironmentVariables['TEMP'] = $tmp
    if ($ReviewVenues) { $psi.EnvironmentVariables['RING_REVIEW_VENUES'] = '1' }
    else { [void]$psi.EnvironmentVariables.Remove('RING_REVIEW_VENUES') }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    Write-Host "$What (PID $($p.Id))…"
    if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        Write-Host "$What не скінчився за $TimeoutMinutes хв — зупинено."
        exit 9
    }
    Write-Host ("{0}: Unity завершився з кодом {1} за {2:N0} с." -f $What, $p.ExitCode, $sw.Elapsed.TotalSeconds)
    return $p.ExitCode
}

# 3а. Окремий прохід імпорту й компіляції (граблі «level0 is corrupted»).
if (-not $SkipCompilePass) {
    $clog = Join-Path $ProjectPath "Logs\ring_build_compile_$stamp.log"
    $ccode = Invoke-Unity "-quit -batchmode -projectPath `"$ProjectPath`" -logFile `"$clog`"" 'Прохід компіляції'
    $clines = if (Test-Path $clog) { Get-Content -Encoding UTF8 $clog } else { @() }
    $cErr = @($clines | Where-Object { $_ -match 'error CS\d{4}' } | Select-Object -Unique)
    if ($cErr.Count) {
        Write-Host "Помилки компіляції C# ($($cErr.Count)), журнал: $clog"
        $cErr | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" }
        exit 6
    }
    if ($ccode -ne 0) { Write-Host "Прохід компіляції впав (код $ccode), журнал: $clog"; exit 5 }
}

# 3б. Збірка.
Write-Host "Журнал збірки: $log"
$code = Invoke-Unity "-quit -batchmode -projectPath `"$ProjectPath`" -logFile `"$log`" -executeMethod Game.Gameplay.EditorTools.RingSceneBuilder.$Method" "Збірка $Method"

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
$venueLine = @($lines | Where-Object { $_ -match 'RING_REVIEW_VENUES=1 — у сцені ще й зали (\d+)' } | Select-Object -Last 1)
$venueCount = if ($venueLine.Count) { [int]([regex]::Match($venueLine[0], 'зали (\d+)').Groups[1].Value) } else { 0 }
Write-Host ("Зали огляду в сцені (клубна зала, палац): " + $(if ($ReviewVenues) { "$venueCount з 2" } else { 'ні (лише спортзал)' }))
if ($netPort -and $Method -ne 'BuildPlayerDev') { exit 14 }
if ($ReviewVenues -and $venueCount -lt 2) { Write-Host 'Просили -ReviewVenues, а в журналі сцени немає обох залів — палацу в збірці немає.'; exit 15 }
if (-not $burstOff -and (-not $hasBurst -or $burstErr.Count)) { exit 13 }
exit 0
