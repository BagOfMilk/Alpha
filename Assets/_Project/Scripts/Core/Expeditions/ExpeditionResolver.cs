using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Checks;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// Детермінований резолв вилазки. Кубиків немає жодного: результат рахується
    /// з підготовки відряду, порога точки і того, скільки разів її вже
    /// відпрацьовували (інваріант «у ядрі немає System.Random»).
    ///
    /// Непередбачуваність береться не з випадковості, а з неповноти інформації:
    /// виснаження точки числом не показується, а підхід обирається до того,
    /// як стане видно, чого коштувало минуле рішення.
    ///
    /// Працює через порт ISettlementActor, а не через Companion: вилазці
    /// потрібне лише число навички і нічого більше, і перебудову моделі персонажа вона
    /// має пережити так само, як пережив міський шар.
    /// </summary>
    public static class ExpeditionResolver
    {
        /// <summary>
        /// Сила відряду на підході: провідний плюс половина від кожного супутника.
        ///
        /// Не сума і не максимум. Сума робить чисельність вирішальною і вбиває
        /// сенс порога; максимум робить супутників декорацією. Половина —
        /// «решта допомагають, але веде один»: відряд із чотирьох середняків не
        /// замінює фахівця, а дотягує його.
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

        /// <summary>Поріг і очікуваний результат до відправки. Ті самі числа, що застосує Resolve.</summary>
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

            // Нижче порога — порожні руки, а не «менше». Перевірка
            // детермінована: «скіл ≥ порога = успіх», і половина здобичі за
            // провал розмивала б це в шкалу без сенсу.
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
        /// Резолв. Реєструє ходку у виснаженні точки — тому зветься один
        /// раз, на відміну від Preview.
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
        /// Полоса результату — та сама драбина, що й у міських перевірок. Гравець вчить
        /// одну шкалу запасу, а не дві: «плюс три — добре, плюс сім — краще»
        /// значить те саме і біля воріт, і всередині міста.
        /// </summary>
        public static OutcomeBand BandFor(int margin, CheckBalance cfg)
        {
            if (margin < 0) return OutcomeBand.Worst;
            if (margin >= cfg.BestMargin) return OutcomeBand.Best;
            if (margin >= cfg.GoodMargin) return OutcomeBand.Good;
            return OutcomeBand.Base;
        }

        /// <summary>
        /// Скільки людей повернеться пораненими.
        ///
        /// Тихий шлях не ранить НІКОЛИ — за жодного результату. Поправка №1
        /// каже це без застережень: «тихий шлях: безпечний (без ран/шрамів/
        /// пермадету), але повільний і дорогий». Раніше тут стояло «не ранить,
        /// поки підготовки вистачає» — умова, якої в поправці немає, і вона
        /// ламала стовп рівно в тому випадку, заради якого стовп писався.
        ///
        /// Провал тихого шляху коштує днів і порожніх рук, а не крові: відряд тиждень
        /// був відсутній, люди весь цей час їли, а приніс він нічого.
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

            // Дістається найменш підготовленим. Нічиї розв'язуються за Id, інакше
            // результат залежав би від порядку в списку і кампанія перестала б
            // відтворюватися.
            var ordered = new List<ISettlementActor>();
            for (int i = 0; i < party.Count; i++) if (party[i] != null) ordered.Add(party[i]);
            ordered.Sort((a, b) =>
            {
                int va = a.GetCheckValue(skill) + a.GetTraitModifier(skill);
                int vb = b.GetCheckValue(skill) + b.GetTraitModifier(skill);
                if (va != vb) return va.CompareTo(vb);
                return string.CompareOrdinal(a.Id, b.Id);
            });

            // Тір: силовий провал калічить всерйоз, усе інше — легкі рани.
            var tier = approach == ExpeditionApproach.Forceful && band == OutcomeBand.Worst
                ? WoundTier.Serious
                : WoundTier.Light;

            for (int i = 0; i < count && i < ordered.Count; i++)
                result.Wounded.Add(new ExpeditionWound { ActorId = ordered[i].Id, Tier = tier });
        }

        /// <summary>
        /// Здобич у цілих. Якщо після виснаження щось іще належить, але
        /// округлення звело в нуль, видається одиниця: на «Ближніх розвалинах»
        /// з базою 2 відпрацьована точка давала б 2 x 0.25 = 0.5 -> 0, тобто
        /// вичерпувалась би насухо всупереч власній підлозі.
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
