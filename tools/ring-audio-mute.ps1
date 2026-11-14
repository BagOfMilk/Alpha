# Глушник звуку гри через Core Audio — спільний для tools/run-offscreen.ps1 і tools/ring-editor.ps1 (dot-source).
# Власник 08.11.2026 через ПМ: «Почему снова звуки есть?» — тури Ring.exe і Play Mode редактора завжди без звуку.
# [RingAudio.Muter]::Mute(pid): усі аудіосесії процесу на всіх активних пристроях виводу — гучність 0 і mute з перевіркою
# GetMute; повертає кількість заглушених сесій (0 — у процесу ще немає звуку) або -1, якщо хоч одну не вдалося заглушити.
# Перевіряти на сесії з тишею (WAV з нулів), а не на грі.
if (-not ('RingAudio.Muter' -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;

// Глушник звуку гри (Core Audio): усі сесії процесу на всіх активних пристроях виводу.
namespace RingAudio
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
}
