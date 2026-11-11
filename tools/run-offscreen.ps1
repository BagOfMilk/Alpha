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
# Код виходу — код виходу гри (0/2/3/4, див. CLAUDE.md, «Як зібрати і перевірити»);
# 124 — тайм-аут; 125 — звук гри не вдалося заглушити чи перевірити (гру зупинено).
#
# Звук — завжди заглушений (власник 08.11.2026 через ПМ: «Почему снова звуки есть?»): з першої
# мілісекунди кожна аудіосесія процесу гри на всіх активних пристроях виводу отримує гучність 0 і mute,
# mute перевіряється GetMute. Не вдалося заглушити чи перевірити — гра зупиняється (код 125).
# -Visible — не ховати вікно (чесний «стіновий» FPS: Windows гальмує вікно за краєм екрана до ~10 к/с).
param(
    [string]$Exe = "",
    [string[]]$GameArgs = @(),
    [int]$Width = 1600,
    [int]$Height = 900,
    # Скільки чекати, перш ніж ховати вікно: 0 — одразу, як з'явилось.
    [int]$ParkDelayMs = 0,
    # Запобіжник: гру, що не завершилась за цей час, буде зупинено (код 124).
    [int]$TimeoutSec = 900,
    # Не ховати вікно за край екрана (заміри FPS, tools/ring-build.ps1 → RingFpsProbe). Звук усе одно заглушений.
    [switch]$Visible
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

// Глушник звуку гри (Core Audio): усі сесії процесу на всіх активних пристроях виводу.
namespace OffscreenAudioCom
{
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator { [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices); }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint n); [PreserveSig] int Item(uint i, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice { [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o); }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2 { int NotImpl1(); int NotImpl2(); [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e); }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator { [PreserveSig] int GetCount(out int n); [PreserveSig] int GetSession(int i, out IAudioSessionControl2 s); }
    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        int a1(); int a2(); int a3(); int a4(); int a5(); int a6(); int a7(); int a8(); int a9();
        int GetSessionIdentifier(out IntPtr s); int GetSessionInstanceIdentifier(out IntPtr s);
        [PreserveSig] int GetProcessId(out uint pid);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float v, ref Guid ctx); [PreserveSig] int GetMasterVolume(out float v);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx); [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

    public static class Muter
    {
        /// <summary>
        /// Глушить усі аудіосесії процесу pid на всіх активних пристроях виводу (гучність 0 + mute) і перевіряє mute.
        /// Повертає кількість заглушених сесій (0 — у процесу ще немає звуку) або -1, якщо хоч одну не вдалося заглушити.
        /// </summary>
        public static int Mute(uint pid)
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDeviceCollection col;
            Marshal.ThrowExceptionForHR(en.EnumAudioEndpoints(0, 1, out col));   // eRender, DEVICE_STATE_ACTIVE
            uint count; Marshal.ThrowExceptionForHR(col.GetCount(out count));
            int hit = 0; bool fail = false; Guid ctx = Guid.Empty;
            for (uint d = 0; d < count; d++)
            {
                IMMDevice dev; if (col.Item(d, out dev) != 0) continue;
                Guid iid = typeof(IAudioSessionManager2).GUID; object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) continue;
                IAudioSessionEnumerator se; if (((IAudioSessionManager2)o).GetSessionEnumerator(out se) != 0) continue;
                int n; if (se.GetCount(out n) != 0) continue;
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 c; if (se.GetSession(i, out c) != 0 || c == null) continue;
                    uint spid; if (c.GetProcessId(out spid) != 0 || spid != pid) continue;
                    hit++;
                    var v = (ISimpleAudioVolume)c;
                    v.SetMasterVolume(0f, ref ctx);
                    v.SetMute(true, ref ctx);
                    bool m; float vol;
                    if (v.GetMute(out m) != 0 || !m || v.GetMasterVolume(out vol) != 0 || vol > 0.001f) fail = true;
                }
            }
            return fail ? -1 : hit;
        }
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
# Не Start-Process: той іде через оболонку Windows (ShellExecute), а вона для непідписаного Ring.exe/Alpha.exe
# показує «Open File — Security Warning» і чекає клацання людини (власник, 08.11.2026: «Постійні запити…»).
# Прямий запуск процесу (UseShellExecute = false) цієї перевірки не проходить.
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Resolve-Path $Exe).Path
$psi.WorkingDirectory = Split-Path $psi.FileName -Parent
$psi.UseShellExecute = $false
$psi.Arguments = (($launchArgs | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' ')
$p = [System.Diagnostics.Process]::Start($psi)

# Перші секунди — часто: Unity створює вікно, потім може його перерозмірити й
# поставити по центру. Далі — рідше, до кінця гри (раптом вікно повернуть).
$firstParkMs = -1
$timedOut = $false
$muteFail = ''
$mutedSessions = 0
while (-not $p.HasExited) {
    try { $m = [OffscreenAudioCom.Muter]::Mute([uint32]$p.Id) } catch { $m = -2; $muteFail = $_.Exception.Message }
    if ($m -lt 0) {
        if (-not $muteFail) { $muteFail = 'аудіосесія гри не заглушилась (GetMute)' }
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        break
    }
    if ($m -gt $mutedSessions) { $mutedSessions = $m; Write-Host "Звук гри заглушено: сесій $m (за $($sw.ElapsedMilliseconds) мс)." }
    if (-not $Visible -and $sw.ElapsedMilliseconds -ge $ParkDelayMs) {
        $moved = [OffscreenWindows]::Park($p.Id, $parkX, $parkY)
        if ($moved -gt 0 -and $firstParkMs -lt 0) { $firstParkMs = $sw.ElapsedMilliseconds }
    }
    if ($sw.Elapsed.TotalSeconds -ge $TimeoutSec) { $timedOut = $true; Stop-Process -Id $p.Id -Force; break }
    if ($sw.ElapsedMilliseconds -lt 15000 + $ParkDelayMs) { Start-Sleep -Milliseconds 15 } else { Start-Sleep -Milliseconds 500 }
}
$p.WaitForExit()
if ($muteFail) {
    Write-Host "Звук гри не вдалося заглушити — гру зупинено: $muteFail" -ForegroundColor Red
    exit 125
}
if ($timedOut) {
    Write-Host "Гра не завершилась за $TimeoutSec с — зупинено." -ForegroundColor Red
    exit 124
}

if ($Visible) {
    Write-Host "Вікно гри не ховалось (-Visible)."
} elseif ($firstParkMs -ge 0) {
    Write-Host "Вікно гри сховано за край екрана за $firstParkMs мс після запуску."
} else {
    Write-Host "Вікно гри не з'явилось або з'явилось уже за краєм екрана."
}
Write-Host "Гра завершилась, код виходу $($p.ExitCode) (за $([math]::Round($sw.Elapsed.TotalSeconds, 1)) с)."
exit $p.ExitCode
