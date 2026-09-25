using System;

namespace Game.Gameplay
{
    /// <summary>Що презентер має зробити цього кадру за рішенням <see cref="BattleTurnDirector"/>.</summary>
    public enum BattleTurnDirectorAction
    {
        /// <summary>Нічого — або не хід ШІ, або ще не сплив <c>ai_delay</c>, або йдуть такти.</summary>
        None,

        /// <summary>Час викликати РІВНО один <c>GameSession.CombatAiStepOneAction()</c> (docs/COMBAT_V2.md §5).</summary>
        StepAi,

        /// <summary>Запобіжник спрацював — примусово <c>GameSession.CombatEndTurn()</c>, хід того самого юніта тягнеться занадто довго.</summary>
        ForceEndTurn
    }

    /// <summary>
    /// Режисер ходу ворога (docs/COMBAT_V2.md §5, §7): ЧИСТА логіка темпу й
    /// запобіжників, жодного типу рушія — <see cref="BattleArenaController"/>
    /// лише годує її станом кожен кадр і виконує повернене рішення (виклик
    /// <c>GameSession.CombatAiStepOneAction()</c>, переліт камери, такти).
    ///
    /// Правило: поки <see cref="BattleTurnDirectorAction.None"/> для
    /// не-ШІ-ходу/вимкненого ШІ/зайнятого презентера — таймер паузи не
    /// накопичується (<see cref="Tick"/> скидає його на нуль), тож пауза
    /// завжди рахується заново з моменту, коли презентер справді вільний.
    /// Лічильники запобіжників (кроки того самого юніта, кроки без зміни
    /// стану) переживають ці паузи в межах ходу ОДНОГО юніта — скидаються
    /// лише коли <c>currentUnitId</c> міняється (новий юніт — новий хід) або
    /// коли викликано <see cref="Reset"/> явно.
    /// </summary>
    public sealed class BattleTurnDirector
    {
        public const float NormalDelaySeconds = 0.45f;
        public const float FastDelaySeconds = 0.08f;

        /// <summary>Запобіжник §5: той самий юніт діє більше 40 разів поспіль — примусово завершити хід.</summary>
        public const int MaxActionsForSameUnit = 40;

        /// <summary>Запобіжник §5: 3 дії поспіль без зміни стану (сигнатура View) — примусово завершити хід.</summary>
        public const int MaxActionsWithoutChange = 3;

        private float _timer;
        private string _armedUnitId;
        private int _actionsForUnit;
        private int _actionsWithoutChange;
        private string _lastSignature;

        /// <summary>Не порожньо, якщо останній <see cref="Tick"/> повернув <see cref="BattleTurnDirectorAction.ForceEndTurn"/> — презентер логує це через <c>Debug.LogWarning</c>.</summary>
        public string LastWarning { get; private set; }

        /// <summary>
        /// Годує стан цього кадру, повертає рішення. <paramref name="currentUnitId"/> —
        /// <c>BattleView.CurrentUnitId</c> (хто зараз ходить); зміна цього id
        /// між викликами трактується як «новий хід нового юніта» — лічильники
        /// запобіжників скидаються, а не переносяться на когось іншого.
        /// </summary>
        public BattleTurnDirectorAction Tick(float deltaSeconds, bool isAiTurn, bool enemyAiEnabled, bool isBusy,
            bool fastMode, string currentUnitId)
        {
            LastWarning = null;

            if (!string.Equals(currentUnitId, _armedUnitId, StringComparison.Ordinal))
                ResetUnitTracking(currentUnitId);

            if (!isAiTurn || !enemyAiEnabled || isBusy)
            {
                // Пауза не тікає, поки презентер не готовий (такти ще
                // програються, чи це взагалі не хід ШІ) — інакше перший крок
                // після довгих тактів стався б миттєво, без видимої паузи.
                _timer = 0f;
                return BattleTurnDirectorAction.None;
            }

            _timer += deltaSeconds;
            float delay = fastMode ? FastDelaySeconds : NormalDelaySeconds;
            if (_timer < delay) return BattleTurnDirectorAction.None;
            _timer = 0f;

            if (_actionsForUnit >= MaxActionsForSameUnit || _actionsWithoutChange >= MaxActionsWithoutChange)
            {
                LastWarning = "[Хід ворога] запобіжник спрацював: " +
                    (_actionsForUnit >= MaxActionsForSameUnit
                        ? "юніт " + (currentUnitId ?? "?") + " діє понад " + MaxActionsForSameUnit + " разів поспіль"
                        : "юніт " + (currentUnitId ?? "?") + " " + MaxActionsWithoutChange + " дії поспіль без зміни стану") +
                    " — хід завершено примусово.";
                ResetUnitTracking(null);
                return BattleTurnDirectorAction.ForceEndTurn;
            }

            return BattleTurnDirectorAction.StepAi;
        }

        /// <summary>
        /// Викликати одразу після <c>GameSession.CombatAiStepOneAction()</c>
        /// (успішного чи ні): <paramref name="stateSignatureAfterStep"/> —
        /// будь-який рядок, що змінюється, коли щось РЕАЛЬНО сталося (§5:
        /// «3 кроки без змін (сигнатура View)») — презентер зазвичай бере
        /// щось на кшталт довжини журналу бою плюс AP/позицію поточного юніта.
        /// </summary>
        public void RecordStepOutcome(string stateSignatureAfterStep)
        {
            _actionsForUnit++;
            _actionsWithoutChange = string.Equals(stateSignatureAfterStep, _lastSignature, StringComparison.Ordinal)
                ? _actionsWithoutChange + 1
                : 0;
            _lastSignature = stateSignatureAfterStep;
        }

        /// <summary>Новий бій / вихід із презентера — забути все.</summary>
        public void Reset() => ResetUnitTracking(null);

        private void ResetUnitTracking(string unitId)
        {
            _armedUnitId = unitId;
            _actionsForUnit = 0;
            _actionsWithoutChange = 0;
            _lastSignature = null;
            _timer = 0f;
        }
    }
}
