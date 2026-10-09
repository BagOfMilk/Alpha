using System;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Ритм тактів бою (подача бою П1–П2, docs/research/RT_COMBAT_PRESENTATION.md): скільки триває замах, рух,
    /// смерть і в яку мить «влучання» — спливні числа, звук і реакція цілі з'являються в кадр удару, а не на
    /// старті чи в кінці анімації. Множник швидкості бою (1× / 1,5× / 2×, меню паузи) скорочує все разом,
    /// і хід ворога теж. Чистий C#: презентер (<c>BattleArenaController</c>) лише застосовує; охоронець —
    /// <c>BattleTactTimingTests</c>. Числа — ПЛЕЙСХОЛДЕР (публічних таймінгів референсу немає).
    /// </summary>
    public static class BattleTactTiming
    {
        /// <summary>Варіанти множника швидкості бою в меню паузи.</summary>
        public static readonly float[] SpeedOptions = { 1f, 1.5f, 2f };

        public const float AttackSeconds = 0.5f, AttackFastSeconds = 0.15f;
        public const float AbilitySeconds = 0.4f, AbilityFastSeconds = 0.12f;
        public const float DownSeconds = 0.6f, DownFastSeconds = 0.2f;
        public const float TileSeconds = 0.18f, TileFastSeconds = 0.09f;

        /// <summary>Частка кліпу замаху до удару: ближній — посередині, постріл — раніше (спуск, а не віддача).</summary>
        public const float MeleeImpactFraction = 0.5f;
        public const float RangedImpactFraction = 0.35f;

        /// <summary>Скільки після удару ще видно реакцію цілі, перш ніж почнеться наступний такт.</summary>
        public const float ReactionTailSeconds = 0.25f;

        /// <summary>Стеля такту удару при 1× — довгий кліп не тягне бій.</summary>
        public const float MaxAttackSeconds = 1.4f;

        /// <summary>Множник у межах 0,25–4 (захист від нуля й абсурду з налаштувань).</summary>
        public static float ClampSpeed(float speed)
            => float.IsNaN(speed) ? 1f : Math.Max(0.25f, Math.Min(4f, speed));

        /// <summary>Темп програвання одноразових кліпів: множник × 2 у швидкому ході ворога.</summary>
        public static float AnimationRate(float speed, bool fastEnemyTurn)
            => ClampSpeed(speed) * (fastEnemyTurn ? 2f : 1f);

        /// <summary>Скільки триває такт і в яку секунду від його старту — удар.</summary>
        public struct Beat
        {
            public float Duration;
            public float ImpactAt;
        }

        /// <summary>
        /// Такт удару. <paramref name="clipLengthSeconds"/> — довжина кліпу замаху при 1× (0 — кліпу немає):
        /// тоді удар — у мить <see cref="MeleeImpactFraction"/>/<see cref="RangedImpactFraction"/> кліпу, а
        /// такт триває до удару + <see cref="ReactionTailSeconds"/>, не довше <see cref="MaxAttackSeconds"/>.
        /// </summary>
        public static Beat Attack(bool melee, bool fastEnemyTurn, float clipLengthSeconds, float speed)
        {
            float s = ClampSpeed(speed);
            if (fastEnemyTurn || clipLengthSeconds <= 0.05f)
            {
                float d = (fastEnemyTurn ? AttackFastSeconds : AttackSeconds) / s;
                return new Beat { Duration = d, ImpactAt = d * 0.5f };
            }
            float rate = AnimationRate(s, false);
            float impact = clipLengthSeconds * (melee ? MeleeImpactFraction : RangedImpactFraction) / rate;
            float cap = MaxAttackSeconds / s;
            if (impact > cap - ReactionTailSeconds / s) impact = cap - ReactionTailSeconds / s;
            float duration = Math.Max(AttackSeconds / s, impact + ReactionTailSeconds / s);
            if (duration > cap) duration = cap;
            return new Beat { Duration = duration, ImpactAt = impact };
        }

        /// <summary>Такт здібності: напис — посередині, як і було.</summary>
        public static Beat Ability(bool fastEnemyTurn, float speed)
        {
            float d = (fastEnemyTurn ? AbilityFastSeconds : AbilitySeconds) / ClampSpeed(speed);
            return new Beat { Duration = d, ImpactAt = d * 0.5f };
        }

        /// <summary>Такт «впав/загинув».</summary>
        public static Beat Down(bool fastEnemyTurn, float speed)
        {
            float d = (fastEnemyTurn ? DownFastSeconds : DownSeconds) / ClampSpeed(speed);
            return new Beat { Duration = d, ImpactAt = d * 0.5f };
        }

        /// <summary>Скільки йде одна клітинка руху.</summary>
        public static float Tile(bool fastEnemyTurn, float speed)
            => (fastEnemyTurn ? TileFastSeconds : TileSeconds) / ClampSpeed(speed);

        /// <summary>Звук у мить удару за сенсом рядка журналу бою (None — без звуку).</summary>
        public static SoundCue ImpactCue(BattleLogKind kind)
        {
            switch (kind)
            {
                case BattleLogKind.Hit: return SoundCue.HitFlesh;
                case BattleLogKind.Crit: return SoundCue.HitArmor;
                case BattleLogKind.Graze: return SoundCue.Block;
                case BattleLogKind.Miss: return SoundCue.Swing;
                default: return SoundCue.None;
            }
        }

        /// <summary>
        /// Подія журналу ядра, звук якої в бою грає арена в кадр удару (<see cref="ImpactCue"/>), а не режисер
        /// звуку в мить розрахунку — інакше удар чути раніше, ніж видно.
        /// </summary>
        public static bool IsImpactEvent(string eventKey)
        {
            switch (eventKey)
            {
                case "combat.attack.hit":
                case "combat.attack.crit":
                case "combat.attack.graze":
                case "combat.attack.miss":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Підпис кнопки множника: «×1», «×1,5», «×2».</summary>
        public static string SpeedLabel(float option)
            => "×" + option.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
    }
}
