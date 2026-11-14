<#
.SYNOPSIS
  «Ринг» у Play Mode редактора Unity — без збірки Ring.exe і без мережі.

.DESCRIPTION
  Власник, 08.11.2026: «Зроби так шоб гра більше не ходила в інтернет і ти її міг локально тестувати без білдку».

  Редактор відкривається на теці проєкту (за замовчуванням — worktree, у якому лежить цей скрипт) і лишається
  відкритим між запусками: зміна коду — це перекомпіляція в редакторі (секунди), а не збірка Ring.exe.
  Вікно редактора ховається за правий край усіх моніторів (як tools/run-offscreen.ps1) і не згортається —
  Game view далі малює кадри. Керування — файлами Temp/ring-editor.cmd і Temp/ring-editor.status
  (Assets/_Project/Scripts/Editor/RingInEditor.cs): без Unity CLI і без мережі.

  Дії (-Action):
    start   — відкрити редактор, якщо ще не відкритий, і дочекатися готовності;
    shots   — підхопити зміни коду, пройти сезон ботом (-ring-shots) і дочекатися кінця; кадри — у -OutDir;
    play    — звичайна гра в Play Mode (подивитися самому: потім -Action show);
    stop    — вийти з Play Mode;
    status  — стан редактора;
    show    — повернути вікно редактора на екран; hide — сховати за край;
    quit    — закрити редактор (звільнити ~3–5 ГБ пам'яті).

  Запускати з Bash-інструмента (процеси з PowerShell-інструмента живуть у пісочниці):
    powershell -ExecutionPolicy Bypass -File tools/ring-editor.ps1 -Action shots -ShotLimit 200
    powershell -ExecutionPolicy Bypass -File tools/ring-editor.ps1 -Action quit

  Коди виходу: 0 — готово; 5 — редактор не запустився чи закрився; 6 — помилки компіляції; 7 — огляд не стартував;
  8 — огляд не дав кадрів; 9 — тайм-аут; 10 — проєкт зайнятий batchmode-процесом Unity (наприклад, збіркою);
  125 — звук редактора не вдалося заглушити (Play Mode зупинено).
  Звук — завжди вимкнений: «Mute Audio» вкладки Game (RingInEditor) і, поки скрипт чекає гру чи огляд, Core Audio
  для процесу редактора (tools/ring-audio-mute.ps1).
#>
param(
    [ValidateSet('start', 'shots', 'play', 'stop', 'status', 'show', 'hide', 'quit')]
    [string]$Action = 'shots',
    [string]$ProjectPath = '',
    [string]$OutDir = '',
    [int]$Seed = 7,
    [int]$ShotLimit = 0,
    [int]$Seasons = 0,
    [int]$TimeoutMinutes = 30,
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor\Unity.exe',
    [switch]$Visible
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
if (-not $ProjectPath) { $ProjectPath = Split-Path $PSScriptRoot -Parent }
$ProjectPath = (Resolve-Path $ProjectPath).Path
$cmdFile = Join-Path $ProjectPath 'Temp\ring-editor.cmd'
$statusFile = Join-Path $ProjectPath 'Temp\ring-editor.status'

Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class RingEditorWindows
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    /// Видимі вікна процесу, що заходять лівіше за parkX, — на x = parkX (за край екрана).
    public static int Park(int pid, int parkX, int parkY) { return Move(pid, parkX, parkY, true); }

    /// Вікна процесу, що стоять за краєм (Left >= edgeX), — назад на екран у точку (x, y).
    public static int Bring(int pid, int edgeX, int x, int y)
    {
        int moved = 0;
        EnumWindows((h, l) =>
        {
            uint owner; GetWindowThreadProcessId(h, out owner);
            if (owner != (uint)pid || !IsWindowVisible(h)) return true;
            RECT r;
            if (GetWindowRect(h, out r) && r.Left >= edgeX)
            {
                SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
                moved++;
            }
            return true;
        }, IntPtr.Zero);
        return moved;
    }

    private static int Move(int pid, int parkX, int parkY, bool park)
    {
        int moved = 0;
        EnumWindows((h, l) =>
        {
            uint owner; GetWindowThreadProcessId(h, out owner);
            if (owner != (uint)pid || !IsWindowVisible(h)) return true;
            RECT r;
            if (GetWindowRect(h, out r) && r.Left < parkX)
            {
                SetWindowPos(h, IntPtr.Zero, parkX, parkY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                moved++;
            }
            return true;
        }, IntPtr.Zero);
        return moved;
    }
}
"@
$virtual = [System.Windows.Forms.SystemInformation]::VirtualScreen
$parkX = $virtual.Right + 200
$parkY = $virtual.Top

function Get-EditorProcess {
    $norm = $ProjectPath.TrimEnd('\').Replace('\', '/').ToLowerInvariant()
    foreach ($p in Get-CimInstance Win32_Process -Filter "Name='Unity.exe'") {
        $cl = [string]$p.CommandLine
        if (-not $cl) { continue }
        $c = $cl.Replace('\', '/').ToLowerInvariant()
        if ($c.Contains($norm + '"') -or $c.Contains($norm + ' ') -or $c.EndsWith($norm)) { return $p }
    }
    return $null
}

function Keep-Hidden($proc) {
    if (-not $Visible -and $proc) { [void][RingEditorWindows]::Park([int]$proc.ProcessId, $parkX, $parkY) }
    Assert-Muted $proc
}

. (Join-Path $PSScriptRoot 'ring-audio-mute.ps1')   # [RingAudio.Muter]

# Усі аудіосесії процесу редактора — гучність 0 і mute; не вдалося — зупинити Play Mode і вийти з кодом 125.
function Assert-Muted($proc) {
    if (-not $proc) { return }
    $m = -2
    $why = 'аудіосесія редактора не заглушилась (GetMute)'
    try { $m = [RingAudio.Muter]::Mute([uint32]$proc.ProcessId) } catch { $why = $_.Exception.Message }
    if ($m -lt 0) {
        Send-Command @('stop')
        Write-Host "Звук редактора не вдалося заглушити — Play Mode зупинено: $why"
        exit 125
    }
}

function Read-Status {
    if (-not (Test-Path $statusFile)) { return $null }
    try { $lines = [System.IO.File]::ReadAllLines($statusFile) } catch { return $null }
    $s = @{}
    foreach ($l in $lines) { $i = $l.IndexOf('='); if ($i -gt 0) { $s[$l.Substring(0, $i)] = $l.Substring($i + 1) } }
    $s['age'] = 9999
    if ($s.ContainsKey('utc')) {
        try {
            $t = [DateTime]::Parse($s['utc'], $null, [System.Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
            $s['age'] = ((Get-Date).ToUniversalTime() - $t).TotalSeconds
        } catch { }
    }
    return $s
}

function Send-Command([string[]]$Lines) {
    New-Item -ItemType Directory -Force -Path (Split-Path $cmdFile) | Out-Null
    $tmp = $cmdFile + '.tmp'
    [System.IO.File]::WriteAllLines($tmp, $Lines, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -Force $tmp $cmdFile
}

function Assert-Alive($proc, [string]$Why) {
    if (-not (Get-Process -Id $proc.ProcessId -ErrorAction SilentlyContinue)) {
        Write-Host "Редактор закрився ($Why). Журнал: $(Join-Path $ProjectPath 'Logs\ring-editor.log')"
        exit 5
    }
}

# Чекає, поки редактор живий, пише свіжий стан і тричі поспіль «idle» (компіляція й імпорт скінчились).
function Wait-Idle($proc, [int]$TimeoutSec, [string]$Why) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $streak = 0
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        Keep-Hidden $proc
        Assert-Alive $proc $Why
        $s = Read-Status
        if ($s -and $s['pid'] -eq [string]$proc.ProcessId -and $s['age'] -lt 15) {
            if ($s['state'] -eq 'compile-error') {
                Write-Host "Помилки компіляції — Console редактора (-Action show) або $(Join-Path $ProjectPath 'Logs\ring-editor.log')."
                exit 6
            }
            if ($s['state'] -eq 'idle') { $streak++; if ($streak -ge 3) { return $s } } else { $streak = 0 }
        }
        Start-Sleep -Milliseconds 300
    }
    Write-Host "Редактор не став готовим за $TimeoutSec с ($Why). Глянь вікно: -Action show."
    exit 9
}

function Start-Editor {
    $proc = Get-EditorProcess
    if ($proc) {
        if ([string]$proc.CommandLine -match '(?i)-batchmode') {
            Write-Host "Проєкт зайнятий batchmode-процесом Unity (PID $($proc.ProcessId)) — дочекайся кінця збірки."
            exit 10
        }
        return $proc
    }
    if (-not (Test-Path $UnityPath)) { Write-Host "Unity не знайдено: $UnityPath (передай -UnityPath)"; exit 5 }
    # Стан і команда попереднього сеансу не мають видатися за готовність нового.
    Remove-Item -Force -ErrorAction SilentlyContinue $statusFile, $cmdFile
    $log = Join-Path $ProjectPath 'Logs\ring-editor.log'
    New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
    # Прямий запуск без оболонки Windows (UseShellExecute = false) — без її перевірок і вікон підтвердження.
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $UnityPath
    $psi.Arguments = "-projectPath `"$ProjectPath`" -logFile `"$log`""
    $psi.UseShellExecute = $false
    # З пісочниці процес іде з низькою цілісністю: у звичайну %TEMP% писати не може (Burst падав «Access to the path is
    # denied» у GetTempFileName — знахідка сесії меню 08.11.2026), а в LocalLow — може.
    $tmp = Join-Path $env:USERPROFILE 'AppData\LocalLow\RingTmp'
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $psi.EnvironmentVariables['TMP'] = $tmp
    $psi.EnvironmentVariables['TEMP'] = $tmp
    $p = [System.Diagnostics.Process]::Start($psi)
    Write-Host "Редактор запущено (PID $($p.Id)) на $ProjectPath. Перший запуск на теці імпортує проєкт — це кілька хвилин."
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    # Перші секунди вікна з'являються й переставляються часто — ховаємо їх щойно з'явились.
    while ($sw.Elapsed.TotalSeconds -lt 20) { if (-not $Visible) { [void][RingEditorWindows]::Park($p.Id, $parkX, $parkY) }; Start-Sleep -Milliseconds 15 }
    return (Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)")
}

function Refresh-Code($proc) {
    Send-Command @('refresh')
    Start-Sleep -Seconds 2
    [void](Wait-Idle $proc 900 'компіляція після змін')
}

function Start-Game($proc, [string[]]$GameArgs) {
    Send-Command (@('run') + $GameArgs)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 180) {
        Keep-Hidden $proc
        Assert-Alive $proc 'запуск гри'
        $s = Read-Status
        if ($s -and $s['pid'] -eq [string]$proc.ProcessId) {
            if ($s['state'] -eq 'playing') { return }
            if ($s['last'] -match '^(busy|compile-error|dirty-scene|no-gameview|unknown-command|error)') {
                Write-Host "Гра не стартувала: $($s['last'])"
                exit 7
            }
        }
        Start-Sleep -Milliseconds 300
    }
    Write-Host 'Гра не стартувала за 3 хв (Play Mode так і не почався).'
    exit 7
}

switch ($Action) {
    'status' {
        $proc = Get-EditorProcess
        if (-not $proc) { Write-Host "Редактор на $ProjectPath не запущено."; exit 0 }
        $mb = [int]((Get-Process -Id $proc.ProcessId).WorkingSet64 / 1MB)
        $s = Read-Status
        Write-Host "Редактор: PID $($proc.ProcessId), пам'ять $mb МБ."
        if ($s) { Write-Host "Стан: $($s['state']); остання дія: $($s['last']); оновлено $([int]$s['age']) с тому." }
        else { Write-Host 'Стану ще немає (редактор вантажиться або в Safe Mode — -Action show).' }
        exit 0
    }
    'show' {
        $proc = Get-EditorProcess
        if (-not $proc) { Write-Host 'Редактор не запущено.'; exit 5 }
        $n = [RingEditorWindows]::Bring([int]$proc.ProcessId, $virtual.Right, $virtual.Left + 40, $virtual.Top + 40)
        Write-Host "Повернуто на екран вікон: $n."
        exit 0
    }
    'hide' {
        $proc = Get-EditorProcess
        if (-not $proc) { Write-Host 'Редактор не запущено.'; exit 5 }
        $n = [RingEditorWindows]::Park([int]$proc.ProcessId, $parkX, $parkY)
        Write-Host "Сховано вікон: $n."
        exit 0
    }
    'stop' {
        $proc = Get-EditorProcess
        if (-not $proc) { Write-Host 'Редактор не запущено.'; exit 0 }
        Send-Command @('stop')
        Write-Host 'Попросив редактор вийти з Play Mode.'
        exit 0
    }
    'quit' {
        $proc = Get-EditorProcess
        if (-not $proc) { Write-Host 'Редактор не запущено.'; exit 0 }
        Send-Command @('quit')
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        while ($sw.Elapsed.TotalSeconds -lt 90 -and (Get-Process -Id $proc.ProcessId -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 500 }
        if (Get-Process -Id $proc.ProcessId -ErrorAction SilentlyContinue) { Write-Host 'Редактор не закрився за 90 с — закрий його вручну (-Action show).'; exit 9 }
        Write-Host 'Редактор закрито.'
        exit 0
    }
    'start' {
        $proc = Start-Editor
        $s = Wait-Idle $proc 1800 'завантаження'
        Write-Host "Редактор готовий (PID $($proc.ProcessId))."
        exit 0
    }
    'play' {
        $proc = Start-Editor
        [void](Wait-Idle $proc 1800 'завантаження')
        Refresh-Code $proc
        Start-Game $proc @()
        Write-Host 'Гра йде в Play Mode. Подивитися — -Action show; зупинити — -Action stop.'
        exit 0
    }
    'shots' {
        $proc = Start-Editor
        [void](Wait-Idle $proc 1800 'завантаження')
        Refresh-Code $proc
        if (-not $OutDir) { $OutDir = Join-Path $ProjectPath ('Logs\RingEditorShots\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
        New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
        $OutDir = (Resolve-Path $OutDir).Path
        $gameArgs = @('-ring-shots', $OutDir, '-ring-seed', "$Seed")
        if ($ShotLimit -gt 0) { $gameArgs += @('-ring-shot-limit', "$ShotLimit") }
        if ($Seasons -gt 0) { $gameArgs += @('-ring-shot-seasons', "$Seasons") }
        Write-Host ('Огляд: ' + ($gameArgs -join ' '))
        Start-Game $proc $gameArgs
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $lastReport = 0
        while ($true) {
            Keep-Hidden $proc
            Assert-Alive $proc 'огляд'
            $s = Read-Status
            if ($s -and $s['pid'] -eq [string]$proc.ProcessId -and $s['state'] -ne 'playing' -and $s['age'] -lt 15) { break }
            if ($sw.Elapsed.TotalMinutes -ge $TimeoutMinutes) {
                Send-Command @('stop')
                Write-Host "Огляд не скінчився за $TimeoutMinutes хв — зупинено."
                exit 9
            }
            if ($sw.Elapsed.TotalSeconds - $lastReport -ge 60) {
                $lastReport = $sw.Elapsed.TotalSeconds
                $n = @(Get-ChildItem -Path $OutDir -Filter *.png -ErrorAction SilentlyContinue).Count
                Write-Host ("  {0:N0} с: кадрів {1}" -f $sw.Elapsed.TotalSeconds, $n)
            }
            Start-Sleep -Milliseconds 500
        }
        $frames = @(Get-ChildItem -Path $OutDir -Filter *.png -ErrorAction SilentlyContinue).Count
        Write-Host ("Огляд скінчився за {0:N0} с, кадрів: {1}. Тека: {2}" -f $sw.Elapsed.TotalSeconds, $frames, $OutDir)
        if ($frames -eq 0) { exit 8 }
        exit 0
    }
}
