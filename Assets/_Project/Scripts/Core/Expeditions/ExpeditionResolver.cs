using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Checks;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// Детерминированный резолв вылазки. Костей нет ни одной: исход считается
    /// из подготовки отряда, порога точки и того, сколько раз её уже
    /// отрабатывали (инвариант «в ядре нет System.Random»).
    ///
    /// Непредсказуемость берётся не из случайности, а из неполноты информации:
    /// истощение точки числом не показывается, а подход выбирается до того,
    /// как станет видно, чего стоило прошлое решение.
    ///
    /// Работает через порт ISettlementActor, а не через Companion: вылазке
    /// нужно число навыка и ничего больше, и перестройку модели персонажа она
    /// должна пережить так же, как пережил городской слой.
    /// </summary>
    public static class ExpeditionResolver
    {
        /// <summary>
        /// Сила отряда на подходе: ведущий плюс половина от каждого спутника.
        ///
        /// Не сумма и не максимум. Сумма делает численность решающей и убивает
        /// смысл порога; максимум делает спутников декорацией. Половина —
        /// «остальные помогают, но ведёт один»: отряд из четырёх середняков не
        /// заменяет специалиста, а дотягивает его.
        /// </summary>
        public static int PartyValue(IReadOnlyList<ISettlementActor> party, SkillKey skill)
        {
            if (party == null || party.Count == 0 || skill.IsNone) return 0;

            var values = new List<int>(party.Count);
            for (int i = 0; i < party.Count; i++)
            {
                var a = party[i];
                if (a == null) continue;
                values.Add(a.GetCheckValue(skill) + a.GetTraitModifier(skill));
            }
            if (values.Count == 0) return 0;

            values.Sort((x, y) => y.CompareTo(x));
            int total = values[0];
            for (int i = 1; i < values.Count; i++) total += values[i] / 2;
            return total;
        }

        /// <summary>Порог и ожидаемый исход до отправки. Те же числа, что применит Resolve.</summary>
        public static ExpeditionPreview Preview(ExpeditionSite site, ExpeditionApproach approach,
            IReadOnlyList<ISettlementActor> party, SiteLedger ledger, BalanceConfig cfg)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            var skill = site.SkillFor(approach);
            int value = PartyValue(party, skill);
            int margin = value - site.Threshold;
            var band = BandFor(margin, cfg.Checks);

            double depletion = ledger == null ? 1.0 : ledger.YieldMultiplier(site.Id, cfg);

            // Ниже порога — пустые руки, а не «поменьше». Проверка
            // детерминированная: «скил ≥ порога = успех», и половина добычи за
            // провал размывала бы это в шкалу без смысла.
            double bandMult = band == OutcomeBand.Worst
                ? 0.0
                : 1.0 + cfg.ExpeditionYieldPerBand * ((int)band - (int)OutcomeBand.Base);

            return new ExpeditionPreview
            {
                SiteId = site.Id,
                Approach = approach,
                Threshold = site.Threshold,
                PartyValue = value,
                Margin = margin,
                Band = band,
                Days = site.DaysFor(approach),
                Materials = Scale(site.BaseMaterials, bandMult * depletion),
                Gold = Scale(site.BaseGold, bandMult * depletion),
                TimesWorked = ledger == null ? 0 : ledger.TimesWorked(site.Id),
                YieldMultiplier = depletion,
                ExpectedWounded = WoundCount(approach, band, cfg),
            };
        }

        /// <summary>
        /// Резолв. Регистрирует ходку в истощении точки — поэтому зовётся один
        /// раз, в отличие от Preview.
        /// </summary>
        public static ExpeditionResult Resolve(ExpeditionSite site, ExpeditionApproach approach,
            IReadOnlyList<ISettlementActor> party, SiteLedger ledger, BalanceConfig cfg)
        {
            var preview = Preview(site, approach, party, ledger, cfg);
            if (ledger != null) ledger.Register(site.Id);

            var result = new ExpeditionResult
            {
                SiteId = site.Id,
                Approach = approach,
                Band = preview.Band,
                Days = preview.Days,
                Materials = preview.Materials,
                Gold = preview.Gold,
                People = preview.Band >= OutcomeBand.Good ? site.PeopleOnGood : 0,
            };

            if (party != null)
                for (int i = 0; i < party.Count; i++)
                    if (party[i] != null) result.PartyIds.Add(party[i].Id);

            AssignWounds(result, party, site.SkillFor(approach), preview.ExpectedWounded, approach, preview.Band);
            return result;
        }

        /// <summary>
        /// Полоса исхода — та же лестница, что у городских проверок. Игрок учит
        /// одну шкалу запаса, а не две: «плюс три — хорошо, плюс семь — лучше»
        /// значит одно и то же у ворот и внутри города.
        /// </summary>
        public static OutcomeBand BandFor(int margin, CheckBalance cfg)
        {
            if (margin < 0) return OutcomeBand.Worst;
            if (margin >= cfg.BestMargin) return OutcomeBand.Best;
            if (margin >= cfg.GoodMargin) return OutcomeBand.Good;
            return OutcomeBand.Base;
        }

        /// <summary>
        /// Сколько человек вернётся ранеными.
        ///
        /// Тихий путь не ранит НИКОГДА — ни при каком исходе. Поправка №1
        /// говорит это без оговорок: «тихий путь: безопасный (без ран/шрамов/
        /// пермадета), но медленный и дорогой». Раньше здесь стояло «не ранит,
        /// пока подготовки хватает» — условие, которого в поправке нет, и оно
        /// ломало столп ровно в том случае, ради которого столп писался.
        ///
        /// Провал тихого пути стоит дней и пустых рук, а не крови: отряд неделю
        /// отсутствовал, люди всё это время ели, а принёс он ничего.
        /// </summary>
        public static int WoundCount(ExpeditionApproach approach, OutcomeBand band, BalanceConfig cfg)
        {
            if (approach == ExpeditionApproach.Quiet) return 0;

            switch (band)
            {
                case OutcomeBand.Worst: return cfg.ExpeditionForcefulWoundsOnWorst;
                case OutcomeBand.Base: return cfg.ExpeditionForcefulWoundsOnBase;
                case OutcomeBand.Good: return cfg.ExpeditionForcefulWoundsOnBase;
                default: return 0;
            }
        }

        private static void AssignWounds(ExpeditionResult result, IReadOnlyList<ISettlementActor> party,
            SkillKey skill, int count, ExpeditionApproach approach, OutcomeBand band)
        {
            if (count <= 0 || party == null || party.Count == 0) return;

            // Достаётся наименее подготовленным. Ничьи разрешаются по Id, иначе
            // исход зависел бы от порядка в списке и кампания перестала бы
            // воспроизводиться.
            var ordered = new List<ISettlementActor>();
            for (int i = 0; i < party.Count; i++) if (party[i] != null) ordered.Add(party[i]);
            ordered.Sort((a, b) =>
            {
                int va = a.GetCheckValue(skill) + a.GetTraitModifier(skill);
                int vb = b.GetCheckValue(skill) + b.GetTraitModifier(skill);
                if (va != vb) return va.CompareTo(vb);
                return string.CompareOrdinal(a.Id, b.Id);
            });

            // Тир: силовой провал калечит всерьёз, всё остальное — лёгкие раны.
            var tier = approach == ExpeditionApproach.Forceful && band == OutcomeBand.Worst
                ? WoundTier.Serious
                : WoundTier.Light;

            for (int i = 0; i < count && i < ordered.Count; i++)
                result.Wounded.Add(new ExpeditionWound { ActorId = ordered[i].Id, Tier = tier });
        }

        /// <summary>
        /// Добыча в целых. Если после истощения что-то ещё причитается, но
        /// округление увело в ноль, выдаётся единица: на «Ближних развалинах»
        /// с базой 2 выработанная точка давала бы 2 x 0.25 = 0.5 -> 0, то есть
        /// иссякала бы насухо вопреки собственному полу.
        /// </summary>
        private static int Scale(int baseValue, double multiplier)
        {
            if (baseValue <= 0 || multiplier <= 0) return 0;

            double v = baseValue * multiplier;
            int rounded = (int)Math.Round(v, MidpointRounding.AwayFromZero);
            return rounded < 1 ? 1 : rounded;
        }
    }
}
