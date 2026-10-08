using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Розмір усього інтерфейсу одним множником (власник 07.10.2026: «Абсолютно увесь UI завеликий, звенши усе
    /// вдвічі»). IMGUI малюється у «віртуальному» екрані <see cref="Width"/>×<see cref="Height"/> (= екран / множник),
    /// а <see cref="Apply"/> ставить <c>GUI.matrix</c> — тож усі розміри (шрифти, кнопки, панелі) зменшуються разом,
    /// без правки сотень чисел. Панелі UI Toolkit множать свій масштаб на <see cref="Factor"/>. Перемикач —
    /// у меню паузи й на титулі; вибір зберігається між запусками.
    /// </summary>
    public static class UiScale
    {
        private const string Key = "ui_scale";

        /// <summary>Вдвічі менше, ніж було (рішення власника 07.10.2026).</summary>
        public const float Default = 0.5f;

        public static readonly float[] Options = { 0.5f, 0.75f, 1f };

        private static float _factor = -1f;

        public static float Factor
        {
            get
            {
                if (_factor <= 0f)
                    _factor = System.Math.Max(0.35f, System.Math.Min(1.5f, PlayerPrefs.GetFloat(Key, Default)));
                return _factor;
            }
            set
            {
                _factor = System.Math.Max(0.35f, System.Math.Min(1.5f, value));
                PlayerPrefs.SetFloat(Key, _factor);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Ширина екрана в координатах IMGUI.</summary>
        public static float Width => Screen.width / Factor;

        /// <summary>Висота екрана в координатах IMGUI.</summary>
        public static float Height => Screen.height / Factor;

        /// <summary>Поставити масштаб IMGUI — першим рядком кожного OnGUI.</summary>
        public static void Apply() => GUI.matrix = Matrix4x4.Scale(new Vector3(Factor, Factor, 1f));

        /// <summary>Точка екрана (пікселі, Y знизу — як дає камера чи миша) → координати IMGUI (Y згори).</summary>
        public static Vector2 ScreenToGui(Vector3 screen) => new Vector2(screen.x / Factor, (Screen.height - screen.y) / Factor);

        /// <summary>Миша в координатах IMGUI.</summary>
        public static Vector2 MouseGui() => ScreenToGui(Input.mousePosition);

        /// <summary>
        /// Розкладка шапки й стрічки в координатах IMGUI: віртуальний екран, а масштаб — справжнього (матриця вже
        /// множить на <see cref="Factor"/>), тож шапка IMGUI і шапка UI Toolkit однієї висоти.
        /// </summary>
        public static HudFrame Frame(bool exploring = false) =>
            HudLayout.ForScale(Width, Height, exploring, HudLayout.ScaleFor(Screen.width, Screen.height));

        /// <summary>Масштаб панелі UI Toolkit: як був, помножений на множник інтерфейсу.</summary>
        public static float PanelScale() => HudLayout.ScaleFor(Screen.width, Screen.height) * Factor;
    }
}
