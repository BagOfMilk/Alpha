using System;
using System.Runtime.InteropServices;

namespace Game.Gameplay
{
    /// <summary>
    /// Негайне завершення процесу гри, минаючи teardown рушія.
    ///
    /// Чому не <c>Application.Quit</c>: нативний teardown Unity 6.4 на цій
    /// збірці падає в UnityPlayer.dll (0xC0000005) незалежно від коду гри —
    /// доведено на голому титулі (-quit-after-title), за будь-якого графічного API.
    ///
    /// Чому не <c>Environment.Exit</c>: так зробили першим фіксом, і він
    /// замінив падіння ЗАВИСАННЯМ — рантайм Mono чекає потоки/фіналізатори,
    /// вікно висить «Не відповідає», процес не вмирає (замір 24.09.2026: тур
    /// проходить за три секунди, далі двадцять хвилин висіння до таймауту).
    ///
    /// <c>TerminateProcess</c> завершує процес одразу і з потрібним кодом
    /// виходу. Усе, що має зберегтися, на цей момент вже записано:
    /// автосейв робиться вранці, підсумок автопрогону — перед викликом.
    /// У редакторі та на інших платформах — звичайний <c>Application.Quit</c>.
    /// </summary>
    public static class HardExit
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint exitCode);
#endif

        public static void Now(int exitCode)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            TerminateProcess(GetCurrentProcess(), unchecked((uint)exitCode));
#else
            UnityEngine.Application.Quit(exitCode);
#endif
        }
    }
}
