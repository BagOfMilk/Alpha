using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Налаштування «Уповільнення на вбивстві» (подача бою П6): перемикач у меню паузи, зберігається між запусками,
    /// як <see cref="BattleSpeed"/>. Без збереженого вибору — увімкнено, крім Низької графіки (Статут PERF-01;
    /// <see cref="KillSlowMo.DefaultEnabled"/>). Саме вікно уповільнення рахує <see cref="KillSlowMo"/>.
    /// </summary>
    public static class BattleSlowMoSetting
    {
        private const string Key = "battle_kill_slowmo";

        public static bool Enabled
        {
            get
            {
                if (PlayerPrefs.HasKey(Key)) return PlayerPrefs.GetInt(Key, 1) != 0;
                return KillSlowMo.DefaultEnabled(GraphicsTier.Current == GraphicsLevel.Low);
            }
            set
            {
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
