using System;
using System.IO;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Версія білда для гравця. Власник 13.10.2026: «Наведи порядок, щоб всюди були версії» — запустив стару збірку
    /// з 29.09 і не мав як її відрізнити. <c>tools/build-unity.ps1</c> кладе поруч з exe <c>version.txt</c> (один рядок:
    /// «збірка N · коміт · дата») і <c>ВЕРСІЯ.txt</c> для людини; гра показує рядок на титулі й пише в журнал.
    /// Файл їде разом з текою білда, тож копія чи архів теж знають свою версію.
    /// </summary>
    public static class BuildVersion
    {
        private static string _cached;

        /// <summary>«збірка 1234 · dfb91ab · 13.10.2026», або «редактор» / «версія невідома».</summary>
        public static string Line
        {
            get
            {
                if (_cached != null) return _cached;
                if (Application.isEditor) return _cached = "редактор";
                try
                {
                    string f = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "version.txt");
                    _cached = File.Exists(f) ? File.ReadAllText(f).Trim() : "";
                }
                catch (Exception) { _cached = ""; }
                if (string.IsNullOrEmpty(_cached)) _cached = "версія невідома";
                return _cached;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LogOnce() => Debug.Log("[Версія] " + Line);
    }
}
