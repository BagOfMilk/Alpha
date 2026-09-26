# Запускає гру (зазвичай автотур) так, щоб її вікно не заважало на екрані:
# одразу після появи вікно відсувається за правий край віртуального екрана
# (усіх моніторів разом) і тримається там до кінця. Гра не згорнута — далі
# малює кадри й робить знімки; згорнуте вікно Unity малювати перестає.
#
# Власник, 25.09.2026: «можем ли мы не открывать юнити так что бы перекрывать
# мне экран?» — варіант «вікно за межами екрана».
#
# Використання (аргументи гри — ОДНИМ рядком у лапках):
#   powershell -File tools/run-offscreen.ps1 -GameArgs "-autoplay -autoplay-journal"
#   powershell -File tools/run-offscreen.ps1 -Exe Build\Windows\Alpha.exe -GameArgs "-autoplay"
# Граблі: у режимі -File PowerShell не розбирає «a,b» на масив — гра отримувала
# один аргумент «-autoplay,-autoplay-journal», не впізнавала його і стояла на
# титулі до тайм-ауту. Тому рядок ділиться тут, за пробілами й комами.
# Код виходу — код виходу гри (0/2/3/4, див. CLAUDE.md, «Як зібрати і перевірити»).
param(
    [string]$Exe = "",
    [string[]]$GameArgs = @(),
    [int]$Width = 1600,
    [int]$Height = 900,
    # Скільки чекати, перш ніж ховати вікно: 0 — одразу, як з'явилось.
    [int]$ParkDelayMs = 0,
    # Запобіжник: гру, що не завершилась за цей час, буде зупинено (код 124).
    [int]$TimeoutSec = 900
)

$ErrorActionPreference = 'Stop'
$proj = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrEmpty($Exe)) { $Exe = Join-Path $proj "Build\Windows\Alpha.exe" }
if (-not (Test-Path $Exe)) {
    Write-Host "Гру не знайдено: $Exe — спершу tools/build-unity.ps1" -ForegroundColor Red
    exit 1
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class OffscreenWindows
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

    /// <summary>
    /// Відсуває кожне видиме вікно верхнього рівня процесу pid, що заходить
    /// лівіше за parkX, на x = parkX. Повертає, скільки вікон пересунуто.
    /// </summary>
    public static int Park(int pid, int parkX, int parkY)
    {
        int moved = 0;
        EnumWindows((h, l) =>
        {
            uint owner;
            GetWindowThreadProcessId(h, out owner);
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

# Паркуємо праворуч від УСІХ моніторів: на другому моніторі вікно теж не з'явиться.
$virtual = [System.Windows.Forms.SystemInformation]::VirtualScreen
$parkX = $virtual.Right + 200
$parkY = $virtual.Top

$gameArgList = @()
foreach ($chunk in $GameArgs) { $gameArgList += $chunk.Split(@(' ', ','), [System.StringSplitOptions]::RemoveEmptyEntries) }
$launchArgs = @('-screen-fullscreen', '0', '-screen-width', "$Width", '-screen-height', "$Height") + $gameArgList
Write-Host ("Аргументи гри: " + ($launchArgs -join ' '))
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $Exe -ArgumentList $launchArgs -PassThru

# Перші секунди — часто: Unity створює вікно, потім може його перерозмірити й
# поставити по центру. Далі — рідше, до кінця гри (раптом вікно повернуть).
$firstParkMs = -1
$timedOut = $false
while (-not $p.HasExited) {
    if ($sw.ElapsedMilliseconds -ge $ParkDelayMs) {
        $moved = [OffscreenWindows]::Park($p.Id, $parkX, $parkY)
        if ($moved -gt 0 -and $firstParkMs -lt 0) { $firstParkMs = $sw.ElapsedMilliseconds }
    }
    if ($sw.Elapsed.TotalSeconds -ge $TimeoutSec) { $timedOut = $true; Stop-Process -Id $p.Id -Force; break }
    if ($sw.ElapsedMilliseconds -lt 15000 + $ParkDelayMs) { Start-Sleep -Milliseconds 15 } else { Start-Sleep -Milliseconds 500 }
}
$p.WaitForExit()
if ($timedOut) {
    Write-Host "Гра не завершилась за $TimeoutSec с — зупинено." -ForegroundColor Red
    exit 124
}

if ($firstParkMs -ge 0) {
    Write-Host "Вікно гри сховано за край екрана за $firstParkMs мс після запуску."
} else {
    Write-Host "Вікно гри не з'явилось або з'явилось уже за краєм екрана."
}
Write-Host "Гра завершилась, код виходу $($p.ExitCode) (за $([math]::Round($sw.Elapsed.TotalSeconds, 1)) с)."
exit $p.ExitCode
