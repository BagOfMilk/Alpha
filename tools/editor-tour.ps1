<#
.SYNOPSIS
  Автотур у ВІДКРИТОМУ редакторі Unity — без Alpha.exe і без нових вікон.

.DESCRIPTION
  Власник, 28.09.2026: «мені не подобається, що ти відчиняєш та зачиняєш
  вікно. Проєкт нехай буде запущенним при тестуванні». Редактор на теці
  проєкту (за замовчуванням .claude/worktrees/live основного репозиторію)
  відкриває власник через Unity Hub, і той лишається відкритим. Скрипт лише
  керує ним через Unity CLI (пакет com.unity.pipeline):

    1) перевіряє, що редактор на зв'язку;
    2) AssetDatabase.Refresh — редактор без фокуса сам змін не підхоплює —
       і чекає кінця імпорту й компіляції; помилки компіляції — код 6;
    3) AutoplayInEditor.Run("<прапорці>"): Game view 1600x900, Play Mode;
       тур сам виходить із Play Mode, коли закінчить;
    4) чекає Logs/Autoplay/Logs/autoplay-summary.txt з рядком «код виходу N»
       і повертає N (0/2/3/4 — як у Alpha.exe, див. AutoplayBootstrap).

  Коди самого скрипта: 5 — редактор не на зв'язку, 6 — помилки компіляції,
  7 — тур не стартував, 8 — тур перервано (Play Mode скінчився без
  підсумку) або тайм-аут.

  Запускати з Bash-інструмента (процеси з PowerShell-інструмента живуть у
  пісочниці), аргументи туру — одним рядком у лапках:
    powershell -ExecutionPolicy Bypass -File tools/editor-tour.ps1 -Flags "-autoplay-journal"
    powershell -ExecutionPolicy Bypass -File tools/editor-tour.ps1 -Flags "-autoplay -autoplay-threshold"
#>
param(
    [string]$Flags = "-autoplay-journal",
    [string]$ProjectPath = "",
    [int]$TimeoutMinutes = 40
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if (-not $ProjectPath) {
    # Тека live — поруч з основною копією, а не з цією worktree.
    $commonGitDir = (& git -C $PSScriptRoot rev-parse --git-common-dir).Trim()
    if (-not [System.IO.Path]::IsPathRooted($commonGitDir)) { $commonGitDir = Join-Path $PSScriptRoot $commonGitDir }
    $ProjectPath = Join-Path (Split-Path -Parent $commonGitDir) '.claude\worktrees\live'
}
$ProjectPath = (Resolve-Path $ProjectPath).Path

$unity = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
if (-not (Test-Path $unity)) { Write-Host "Unity CLI не знайдено: $unity"; exit 5 }

function Invoke-Eval([string]$Code, [int]$Timeout = 60) {
    # stderr CLI в PowerShell 5.1 з 2>&1 стає ErrorRecord, а під 'Stop' — винятком.
    $ErrorActionPreference = 'Continue'
    # PowerShell до 7.3 передає аргумент у зовнішню програму, не екрануючи
    # лапки всередині: Run("-autoplay") дійшов би до CLI як Run(-autoplay).
    if ($PSVersionTable.PSVersion -lt [version]'7.3') { $Code = $Code.Replace('"', '\"') }
    # --result-only: лише результат eval, без конверта з командою і параметрами.
    $out = & $unity command eval $Code --result-only --project-path $ProjectPath --timeout $Timeout --caller plugin --skill unity-cli 2>&1 | Out-String
    return [pscustomobject]@{ Ok = ($LASTEXITCODE -eq 0); Text = $out.Trim() }
}

function Test-True([string]$Text) { return $Text -match '(?i)\btrue\b' }
function Test-False([string]$Text) { return $Text -match '(?i)\bfalse\b' }

# 1) Зв'язок.
$probe = Invoke-Eval 'return UnityEditor.EditorApplication.isPlaying;'
if (-not $probe.Ok) {
    Write-Host "Редактор на $ProjectPath не на зв'язку. Відкрий проєкт у Unity Hub і дочекайся кінця імпорту."
    Write-Host $probe.Text
    exit 5
}
if (Test-True $probe.Text) {
    Write-Host "Редактор уже в Play Mode — дочекайся кінця або зупини Play."
    exit 7
}

# 2) Підхопити зміни з диска і дочекатися компіляції. Під час перезавантаження
#    домену eval не відповідає — це нормально, чекаємо далі.
$refresh = Invoke-Eval 'UnityEditor.AssetDatabase.Refresh(); return 1;' 120
if (-not $refresh.Ok) {
    # Перезавантаження домену посеред Refresh може обірвати відповідь — це не
    # помилка; справжні проблеми покаже очікування нижче і scriptCompilationFailed.
    Write-Host "Refresh без відповіді (ймовірно, перезавантаження домену): $($refresh.Text)"
}
$deadline = (Get-Date).AddMinutes(10)
$idleInRow = 0
while ($idleInRow -lt 2) {
    if ((Get-Date) -gt $deadline) { Write-Host "Компіляція не скінчилась за 10 хвилин."; exit 8 }
    Start-Sleep -Seconds 2
    $busy = Invoke-Eval 'return UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating;'
    if ($busy.Ok -and (Test-False $busy.Text)) { $idleInRow++ } else { $idleInRow = 0 }
}
$failed = Invoke-Eval 'return UnityEditor.EditorUtility.scriptCompilationFailed;'
if ($failed.Ok -and (Test-True $failed.Text)) {
    Write-Host "Помилки компіляції — тур на старому коді не запускаю. Подробиці: $ProjectPath\Logs\ або консоль редактора."
    exit 6
}

# 3) Старт туру. Сцени Game.unity немає в репозиторії — збирається кодом.
$summary = Join-Path $ProjectPath 'Logs\Autoplay\Logs\autoplay-summary.txt'
# Екранування для рядка C#; для командного рядка лапки екранує Invoke-Eval.
$escaped = $Flags.Replace('\', '\\').Replace('"', '\"')
$run = Invoke-Eval ('return Game.Gameplay.EditorTools.AutoplayInEditor.Run("' + $escaped + '");')
if ($run.Text -match 'no-scene') {
    Write-Host "Сцени Game.unity немає — збираю (GameSceneBuilder.Build)."
    $build = Invoke-Eval 'Game.Gameplay.EditorTools.GameSceneBuilder.Build(); return 1;' 300
    if (-not $build.Ok) { Write-Host "Сцену не зібрано:"; Write-Host $build.Text; exit 7 }
    $run = Invoke-Eval ('return Game.Gameplay.EditorTools.AutoplayInEditor.Run("' + $escaped + '");')
}
if (-not ($run.Ok -and $run.Text -match 'started')) {
    Write-Host "Тур не стартував:"
    Write-Host $run.Text
    exit 7
}
Write-Host ("Тур почато (" + $Flags + "): " + ($run.Text -split "`n" | Select-String 'started' | Select-Object -First 1))

# 4) Чекати підсумку. Якщо Play Mode скінчився, а підсумку нема (Stop руками,
#    виняток у редакторі) — не чекати до тайм-ауту.
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$notPlayingInRow = 0
$code = $null
while ($null -eq $code) {
    if ((Get-Date) -gt $deadline) { Write-Host "Тайм-аут $TimeoutMinutes хв — тур не завершився."; exit 8 }
    Start-Sleep -Seconds 5
    if (Test-Path $summary) {
        $text = Get-Content -Path $summary -Encoding UTF8 -Raw
        if ($text -match 'код виходу (\d+)') { $code = [int]$Matches[1]; break }
    }
    $playing = Invoke-Eval 'return UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;'
    if ($playing.Ok -and (Test-False $playing.Text)) { $notPlayingInRow++ } else { $notPlayingInRow = 0 }
    if ($notPlayingInRow -ge 3 -and -not (Test-Path $summary)) {
        Write-Host "Play Mode скінчився без підсумку туру — тур перервано."
        exit 8
    }
}

$shots = Join-Path $ProjectPath 'Logs\Autoplay\Screenshots'
$shotCount = if (Test-Path $shots) { (Get-ChildItem $shots -Filter *.png).Count } else { 0 }
Write-Host "--- підсумок ($summary), знімків: $shotCount ---"
Get-Content -Path $summary -Encoding UTF8 | Select-Object -Last 15 | ForEach-Object { Write-Host $_ }
exit $code
