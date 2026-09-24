using System;
using System.Runtime.InteropServices;

namespace Game.Gameplay
{
    /// <summary>
    /// Немедленное завершение процесса игры, минуя teardown движка.
    ///
    /// Почему не <c>Application.Quit</c>: нативный teardown Unity 6.4 на этой
    /// сборке падает в UnityPlayer.dll (0xC0000005) независимо от кода игры —
    /// доказано на голом титуле (-quit-after-title), при любом графическом API.
    ///
    /// Почему не <c>Environment.Exit</c>: так сделали первым фиксом, и он
    /// заменил падение ЗАВИСАНИЕМ — рантайм Mono ждёт потоки/финализаторы,
    /// окно висит «Не отвечает», процесс не умирает (замер 24.09.2026: тур
    /// проходит за три секунды, дальше двадцать минут висения до таймаута).
    ///
    /// <c>TerminateProcess</c> завершает процесс сразу и с нужным кодом
    /// выхода. Всё, что должно сохраниться, к этому моменту уже записано:
    /// автосейв делается утром, итог автопрогона — перед вызовом.
    /// В редакторе и на других платформах — обычный <c>Application.Quit</c>.
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
