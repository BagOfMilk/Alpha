using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Налаштування «Екшн-кадри» (подача бою П12): перемикач у меню паузи, зберігається між запусками; типово ввімкнено.
    /// На Низькій графіці кадрів немає незалежно від нього (<see cref="BattleActionCamera"/>, Статут PERF-01).
    /// </summary>
    public static class BattleActionCameraSetting
    {
        private const string Key = "battle_action_camera";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(Key, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
