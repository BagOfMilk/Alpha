using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Множник швидкості бою (подача бою П1, docs/research/RT_COMBAT_PRESENTATION.md): 1× / 1,5× / 2× — анімації,
    /// рух, паузи між діями й хід ворога разом. Перемикач — у меню паузи; вибір зберігається між запусками, як
    /// <see cref="UiScale"/>. Ритм рахує <see cref="BattleTactTiming"/>; на розрахунок бою не впливає (детермінізм).
    /// </summary>
    public static class BattleSpeed
    {
        private const string Key = "battle_speed";
        private static float _multiplier = -1f;

        public static float Multiplier
        {
            get
            {
                if (_multiplier <= 0f) _multiplier = BattleTactTiming.ClampSpeed(PlayerPrefs.GetFloat(Key, 1f));
                return _multiplier;
            }
            set
            {
                _multiplier = BattleTactTiming.ClampSpeed(value);
                PlayerPrefs.SetFloat(Key, _multiplier);
                PlayerPrefs.Save();
            }
        }
    }
}
