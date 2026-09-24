using Game.Core.Combat;
using Game.Core.Randomness;

namespace Game.Core.Session
{
    /// <summary>
    /// Параметри нової гри (§4.1 TEST_BUILD.md: <c>NewGame(NewGameOptions o)</c>).
    ///
    /// <see cref="Roller"/> — навмисне порушення "гарного тону" DTO (звичне
    /// поле-дані тут несе живий об'єкт), тому що іншого шляху немає: R1
    /// забороняє Game.Core реалізовувати <see cref="IDiceRoller"/> взагалі
    /// (<c>ArchitectureGuardTests.Core_NoTypeImplementsIDiceRoller</c>), а
    /// справжня сидована реалізація (<c>Game.Gameplay.Combat.SeededDiceRoller</c>)
    /// живе в сборці, на яку Game.Core НЕ посилається (напрямок залежності
    /// Gameplay→Core, не навпаки). Отже <see cref="GameSession"/> фізично не
    /// може сконструювати кубик сам — той, хто збирає гру (Alpha.Play/Alpha.Sim/
    /// Unity-composition root), будує <c>SeededDiceRoller</c> один раз і кладе
    /// сюди; GameSession лише звертається до нього через інтерфейс
    /// (<c>Roll01</c>/<c>CaptureState</c>/<c>RestoreState</c>) — жодного знання
    /// про конкретний тип. Для <see cref="HitRuleKind.Threshold"/> це поле не
    /// потрібне (бій повністю детермінований без кубика).
    /// </summary>
    public sealed class NewGameOptions
    {
        public HitRuleKind HitRule = HitRuleKind.Threshold;

        /// <summary>Сід сесії. Кубик (якщо є) переводиться в цей стан через RestoreState — GameSession його не "створює", лише сідує вже отриманий ззовні екземпляр.</summary>
        public ulong Seed = 1;

        /// <summary>Готовий кубик ззовні (див. коментар класу). Обов'язковий лише для HitRule=Percent.</summary>
        public IDiceRoller Roller;

        /// <summary>Протагоніст може загинути насправді (без сюжетного захисту від смерті в бою).</summary>
        public bool Ironman;

        /// <summary>true — пропустити швидкий екран створення (R12), протагоніст лишається заглушкою FirstHourWorld.</summary>
        public bool SkipCreation;

        /// <summary>
        /// Поправка №7.7 (рішення власника 24.09.2026, дослівно: «на момент
        /// тесту механік та геймплей лупу усі будівлі 1 день»): у тестовій
        /// збірці кожне здание будується рівно одні сутки — заказ уранці,
        /// готово до наступного ранку, пост відкривається одразу.
        ///
        /// Дефолт — true, бо саме тестова збірка (Unity нова гра, Alpha.Play,
        /// боти) є тим "цим білдом", для якого власник це вирішив: усі три
        /// шляхи ведуть крізь <see cref="GameSession.NewGame"/> і жоден з них
        /// явно не передає цей прапорець, тож усі отримують один день за
        /// замовчуванням. Проєктні строки (п'ять стадій по добах, Поправка
        /// №6.1, ПЛЕЙСХОЛДЕР) лишаються в даних (<c>DefaultBuildings</c>) і
        /// включаються звичайною новою грою, коли виклик явно ставить false —
        /// це баланс кампанії, а не тестової збірки.
        /// </summary>
        public bool TestBuildOneDayConstruction = true;
    }

    /// <summary>"Тренувальний бій" з титульного меню (§4.1: <c>NewTrainingBattle</c>) — пісочниця, без кампанії.</summary>
    public sealed class TrainingBattleOptions
    {
        public HitRuleKind HitRule = HitRuleKind.Threshold;
    }

    /// <summary>Дія гравця у вікні реакції на форсовану кризу доби 5 (§3.5, §4.1 <c>ReactToCrisis</c>).</summary>
    public enum CrisisReaction
    {
        /// <summary>Витратити золото — м'якший укус.</summary>
        SpendGold,
        /// <summary>Відрядити людину з поста — той самий ефект іншою ціною.</summary>
        SendDefender,
        /// <summary>Не реагувати — вікно згорає невідреагованим.</summary>
        Ignore
    }
}
