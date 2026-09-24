using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Checks;

namespace Game.Core.Story
{
    /// <summary>
    /// План фінального штурму — БОЙОВО-АГНОСТИЧНІ дані (§1.1, переопределяет
    /// §4.14 щодо типу): id ворогів за полосою Готовності (включно з босом
    /// «burunda»), ключ арени, id зрадника-напарника, якщо є. У
    /// <c>BattleSetup</c> (Core/Combat, B1 — тут ще не існує) його перетворює
    /// D1: B6 не залежить від B1 у паралельній фазі (§1.1).
    /// </summary>
    public sealed class AssaultPlan
    {
        public readonly List<string> EnemyDefinitionIds = new List<string>();
        public string ArenaKey;

        /// <summary>Id напарника, що бʼється на боці ворога (null — зради нема).</summary>
        public string DefectorCompanionId;
    }

    /// <summary>Чим платить кожна полоса фіналу — «жодна не чиста перемога» (§7.15).</summary>
    public enum FinaleCostKind
    {
        /// <summary>М'якша ціна: компроміс (шлях назад для когось, тінь на репутації).</summary>
        Compromise = 0,
        /// <summary>Важча ціна: хтось лишається на тому боці/платить тілом.</summary>
        Hostage = 1
    }

    /// <summary>Розв'язка фіналу як дані — ключ тексту + категорія ціни.</summary>
    public sealed class FinaleOutcome
    {
        /// <summary>"best"|"good"|"base"|"worst" — той самий формат, що у PassVanguardOutcome.ResolveKey.</summary>
        public string Key;
        public FinaleCostKind Cost;
    }

    /// <summary>
    /// Фінал (доба 5, ніч, R8) — РЕАЛЬНИЙ, не текстовий прев'ю. Два шляхи:
    /// тихий («загатити річку» — перевірка) і кровавий (тактичний бій, силу
    /// якого задає <see cref="BuildAssault"/>). Обидва зсунуті полосою
    /// Готовності: гірша Готовність — важчий фінал (монотонність — акцептанс
    /// пакета B6, перевірено на двох полосах).
    /// </summary>
    public static class Finale
    {
        public const string BurundaBossId = "burunda";
        public const string RankAndFileEnemyId = "horde_skirmisher";
        public const string ArenaKeyValue = "finale_pass";
        public const string DamTopicId = "finale.dam";
        public const string DamTacticsTopicId = "finale.dam.tactics";

        /// <summary>
        /// Кроваво: «тримати перевал». Рядових більше при гіршій Готовності;
        /// Бурунда — завжди, окремо від рахунку рядових (боса не масштабує
        /// жодна полоса — це іменний бос, Поправка №5.2). Зрадник (Мирослава,
        /// якщо доля привела її до ворога) — параметром, а не читанням
        /// Companion/Loyalty (B4 тут ще не існує, §1.1).
        /// </summary>
        public static AssaultPlan BuildAssault(ReadinessBand band, string defectorCompanionId = null, ReadinessBalance cfg = null)
        {
            var balance = cfg ?? new ReadinessBalance();
            int rankAndFile = ReadinessBalance.Pick(balance.AssaultEnemyCountByBand, (int)band, 4);

            var plan = new AssaultPlan { ArenaKey = ArenaKeyValue, DefectorCompanionId = defectorCompanionId };
            for (int i = 0; i < rankAndFile; i++) plan.EnemyDefinitionIds.Add(RankAndFileEnemyId);
            plan.EnemyDefinitionIds.Add(BurundaBossId);
            return plan;
        }

        /// <summary>
        /// Тихо: «загатити річку», перша з ДВОХ перевірок шляху (§3.5: «перевірки
        /// Mechanics≥7/Tactics≥5» — множина; §7.15 <c>finale.option.quiet</c>
        /// називає обидва навики). Поріг Mechanics зсунутий полосою Готовності —
        /// гірша Готовність, вищий поріг (важче). Показаний заздалегідь, як і
        /// будь-яка інша перевірка (інваріант 8). Друга перевірка — <see cref="BuildDamTactics"/>;
        /// обидві резолвить D1 через звичайний <c>CheckResolver</c>. Як саме дві
        /// полоси зводяться в одну (гірша з двох? обидві мають пройти?) специфікація
        /// не фіксує — відкрите питання власнику (openIssues пакета B6, не §9 —
        /// дописано пізніше за аудит розривів).
        /// </summary>
        public static CheckRequest BuildDam(ReadinessBand band, ReadinessBalance cfg = null)
        {
            var balance = cfg ?? new ReadinessBalance();
            int threshold = ReadinessBalance.Pick(balance.DamThresholdByBand, (int)band, 5);
            return new CheckRequest(SkillKeys.Mechanics, threshold, ApproachForm.Neutral, DamTopicId);
        }

        /// <summary>
        /// Тихо: «загатити річку», ДРУГА перевірка шляху — Tactics, зсунута тією ж
        /// полосою Готовності (§3.5/§7.15, фікс-рев'ю B6: раніше тихий шлях мав
        /// лише перевірку Mechanics, а специфікація вимагає обидві).
        /// </summary>
        public static CheckRequest BuildDamTactics(ReadinessBand band, ReadinessBalance cfg = null)
        {
            var balance = cfg ?? new ReadinessBalance();
            int threshold = ReadinessBalance.Pick(balance.TacticsThresholdByBand, (int)band, 5);
            return new CheckRequest(SkillKeys.Tactics, threshold, ApproachForm.Neutral, DamTacticsTopicId);
        }

        /// <summary>"best"|"good"|"base"|"worst" — той самий ключ, що озвучують <c>finale.outcome.*</c> (§7.15).</summary>
        public static string ResolveKey(OutcomeBand band)
        {
            switch (band)
            {
                case OutcomeBand.Best: return "best";
                case OutcomeBand.Good: return "good";
                case OutcomeBand.Base: return "base";
                default: return "worst";
            }
        }

        /// <summary>
        /// Мапить полосу на ключ ТА категорію ціни: жодна полоса не «чиста»
        /// перемога (§7.15) — Найкраща/Хороша платять компромісом, Базова/
        /// Найгірша — заручником.
        /// </summary>
        public static FinaleOutcome Resolve(OutcomeBand band)
        {
            var cost = (band == OutcomeBand.Base || band == OutcomeBand.Worst)
                ? FinaleCostKind.Hostage
                : FinaleCostKind.Compromise;
            return new FinaleOutcome { Key = ResolveKey(band), Cost = cost };
        }
    }
}
