using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Поточний рівень графіки (Поправка №21.1, Статут PERF-01): перший запуск — автопідбір
    /// <see cref="GraphicsTierPicker"/>, далі — вибір гравця з меню паузи чи титулу, збережений
    /// у PlayerPrefs. Застосування — лише перемикання рівня якості: кожен рівень несе свій
    /// асет URP (<c>Editor/GraphicsTiersSetup</c>), тож рантайму не треба знати типів URP.
    /// </summary>
    public static class GraphicsTier
    {
        private const string PrefKey = "alpha.gfx";
        private static bool _initialised;

        public static GraphicsLevel Current
        {
            get
            {
                int level = QualitySettings.GetQualityLevel();
                if (level <= 0) return GraphicsLevel.Low;
                return level >= 2 ? GraphicsLevel.High : GraphicsLevel.Medium;
            }
        }

        /// <summary>Найдешевший режим: без частинок погоди та інших прикрас.</summary>
        public static bool IsLow => Current == GraphicsLevel.Low;

        /// <summary>Раз на запуск: збережений вибір або автопідбір під цю машину.</summary>
        public static void InitOnce()
        {
            if (_initialised) return;
            _initialised = true;

            GraphicsLevel level;
            if (TryCommandLineLevel(out level))
            {
                Apply(level); // лише на цей запуск, без збереження (тури: -gfx medium)
                return;
            }
            if (PlayerPrefs.HasKey(PrefKey))
                level = Clamp(PlayerPrefs.GetInt(PrefKey));
            else
                level = GraphicsTierPicker.Pick(SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize,
                    SystemInfo.systemMemorySize, SystemInfo.processorCount);
            Apply(level);
        }

        /// <summary>
        /// Рівень з командного рядка: <c>-gfx low|medium|high</c> — для турів і перевірок на певному рівні (власник,
        /// 08.10.2026: «Постав Середню графіку в турі і покажи екшн-кадр»). Діє лише на запуск, вибору гравця не змінює.
        /// </summary>
        private static bool TryCommandLineLevel(out GraphicsLevel level)
        {
            level = GraphicsLevel.Medium;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!string.Equals(args[i], "-gfx", System.StringComparison.OrdinalIgnoreCase)) continue;
                switch (args[i + 1].ToLowerInvariant())
                {
                    case "low": level = GraphicsLevel.Low; return true;
                    case "medium": level = GraphicsLevel.Medium; return true;
                    case "high": level = GraphicsLevel.High; return true;
                }
            }
            return false;
        }

        /// <summary>Вибір гравця: діє одразу і запам'ятовується.</summary>
        public static void Set(GraphicsLevel level)
        {
            Apply(level);
            PlayerPrefs.SetInt(PrefKey, (int)level);
            PlayerPrefs.Save();
        }

        private static void Apply(GraphicsLevel level)
        {
            int index = (int)level;
            int count = QualitySettings.names.Length;
            if (count > 0 && index >= count) index = count - 1;
            if (QualitySettings.GetQualityLevel() != index)
                QualitySettings.SetQualityLevel(index, true);
        }

        private static GraphicsLevel Clamp(int value) =>
            value <= 0 ? GraphicsLevel.Low : (value >= 2 ? GraphicsLevel.High : GraphicsLevel.Medium);
    }
}
