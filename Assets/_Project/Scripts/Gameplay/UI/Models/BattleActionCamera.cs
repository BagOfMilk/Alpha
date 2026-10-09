namespace Game.Gameplay.UI
{
    /// <summary>
    /// Екшн-кадр бою (подача П12; будова камери RT §1.1: кінематографічний кадр «хто б'є → ціль» має шанс на кожен удар
    /// і паузу між атаками, довжина — від самої дії). Камера арени на час удару стає перспективною й дивиться через плече
    /// того, хто б'є, на ціль (поза — <see cref="DialogueDirector"/>, план «через плече»).
    ///
    /// Без випадковості (вона в нас лише поза ядром і лише для кубика): «шанс» — накопичувач, як правило влучання без
    /// кубика: <see cref="Chance"/> = 0,34 дає рівно один кадр на ~3 удари, і гравець не бачить його двічі поспіль.
    /// Вимикається в меню паузи; на Низькій графіці вимкнено (Статут PERF-01). Числа — ПЛЕЙСХОЛДЕР.
    /// </summary>
    public sealed class BattleActionCamera
    {
        public const float Chance = 0.34f;
        /// <summary>Найменша пауза між екшн-кадрами, реальні секунди.</summary>
        public const float CooldownSeconds = 6f;
        /// <summary>Коротші удари (хвіст анімації) кадру не варті — склейка туди й назад дратує.</summary>
        public const float MinShotSeconds = 0.35f;

        private float _accumulator;
        private float _lastShotAt = float.NegativeInfinity;

        /// <summary>
        /// Удар у реальну секунду <paramref name="nowReal"/> триватиме <paramref name="shotSeconds"/>: чи знімати його
        /// екшн-кадром. Накопичувач росте лише на ударах, що пройшли решту умов.
        /// </summary>
        public bool ShouldTrigger(float nowReal, float shotSeconds, bool enabled, bool lowGraphics)
        {
            if (!enabled || lowGraphics || shotSeconds < MinShotSeconds) return false;
            if (nowReal - _lastShotAt < CooldownSeconds) return false;
            _accumulator += Chance;
            if (_accumulator < 1f) return false;
            _accumulator -= 1f;
            _lastShotAt = nowReal;
            return true;
        }

        public void Reset()
        {
            _accumulator = 0f;
            _lastShotAt = float.NegativeInfinity;
        }
    }
}
