using System;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Expeditions;
using Game.Core.Health;

namespace Game.Core.Quests
{
    /// <summary>
    /// Последствия КВЕСТОВОГО боя для ростера (US-13.1, US-17.1): смерти насовсем и
    /// ранения по тем же правилам, что у вылазки (даун/спасённый = Серьёзное при
    /// победе, Критическое при поражении; низкий HP = Лёгкое), но без дороги, лута
    /// и XP за победу — награду даёт Outcome-этап квеста. Айронмен: гибель
    /// протагониста — game over (решает вызывающий по report.ProtagonistDied).
    /// </summary>
    public static class QuestCombat
    {
        public sealed class Report
        {
            public CombatOutcome Outcome;
            public readonly System.Collections.Generic.List<CompanionOutcome> Companions =
                new System.Collections.Generic.List<CompanionOutcome>();
            public bool ProtagonistDied;
        }

        private static int _scarCounter;

        public static Report ApplyToRoster(CombatState combat, Roster roster, BalanceConfig cfg,
                                           Func<Companion, Scar> scarPicker = null)
        {
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            if (combat.Outcome == CombatOutcome.Ongoing)
                throw new InvalidOperationException("Бой не завершён");
            scarPicker = scarPicker ?? DefaultScarPicker;

            var report = new Report { Outcome = combat.Outcome };
            bool victory = combat.Outcome == CombatOutcome.Victory;

            foreach (var unit in combat.Units)
            {
                if (unit.Side != Side.Player || unit.SourceCompanionId == null) continue;
                var comp = roster.Get(unit.SourceCompanionId);
                if (comp == null) continue;

                var oc = new CompanionOutcome(comp.Id);
                switch (unit.LifeState)
                {
                    case UnitLifeState.Dead:
                        comp.Kill();
                        oc.Died = true;
                        if (comp.IsProtagonist) report.ProtagonistDied = true;
                        break;

                    case UnitLifeState.Stabilized:
                    case UnitLifeState.Downed:
                    {
                        var tier = victory ? InjuryTier.Serious : InjuryTier.Critical;
                        var scar = scarPicker(comp);
                        bool scarred = comp.ApplyInjury(tier, cfg, scar);
                        oc.Injury = comp.CurrentInjury;
                        oc.ScarId = scarred && scar != null ? scar.Id : null;
                        break;
                    }

                    case UnitLifeState.Active:
                        double fraction = unit.Profile.MaxHp > 0 ? (double)unit.Hp / unit.Profile.MaxHp : 1.0;
                        if (fraction <= cfg.LightInjuryHpFraction)
                        {
                            comp.ApplyInjury(InjuryTier.Light, cfg, null);
                            oc.Injury = comp.CurrentInjury;
                        }
                        else
                        {
                            comp.Status = CompanionStatus.InCamp;
                        }
                        break;
                }
                report.Companions.Add(oc);
            }

            return report;
        }

        private static Scar DefaultScarPicker(Companion _)
            => _scarCounter++ % 2 == 0 ? DefaultContent.OneEye() : DefaultContent.Limp();
    }
}
