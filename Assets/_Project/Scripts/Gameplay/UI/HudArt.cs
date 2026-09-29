using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Шрифти й значки інтерфейсу (Поправка №12.2: Fixel, Noto Serif, значки
    /// game-icons.net). Асет <c>Resources/AlphaHudArt</c> створює і щоразу
    /// оновлює <c>Editor/HudPanelAssets</c> під час збирання сцени: він
    /// тримає посилання на файли з <c>Assets/ThirdParty</c>, тож вони
    /// гарантовано потрапляють у білд. Без асета інтерфейс лишається на
    /// вбудованому шрифті й без значків — гра від цього не ламається.
    /// </summary>
    public sealed class HudArt : ScriptableObject
    {
        public const string ResourcePath = "AlphaHudArt";

        /// <summary>Fixel Text Regular — тіло інтерфейсу і репліки.</summary>
        public Font UiRegular;
        /// <summary>Fixel Text SemiBold — виділене (замість синтетичного жирного).</summary>
        public Font UiSemiBold;
        /// <summary>Fixel Display SemiBold — заголовки.</summary>
        public Font Display;
        /// <summary>Noto Serif Regular — «паперові» екрани (хроніка, звіти; наступні кроки міграції).</summary>
        public Font Paper;

        /// <summary>Ключ значка (ресурс «gold», «phase.night», «away»…) → текстура; однакова довжина масивів.</summary>
        public string[] IconKeys = new string[0];
        public Texture2D[] Icons = new Texture2D[0];

        private static HudArt _cached;
        private static bool _loaded;

        /// <summary>Асет з Resources (один раз за сесію); null, якщо його немає.</summary>
        public static HudArt Current
        {
            get
            {
                if (!_loaded)
                {
                    _cached = Resources.Load<HudArt>(ResourcePath);
                    _loaded = true;
                }
                return _cached;
            }
        }

        public Texture2D Icon(string key)
        {
            if (string.IsNullOrEmpty(key) || IconKeys == null || Icons == null) return null;
            int n = IconKeys.Length < Icons.Length ? IconKeys.Length : Icons.Length;
            for (int i = 0; i < n; i++)
                if (IconKeys[i] == key) return Icons[i];
            return null;
        }
    }
}
