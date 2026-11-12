using System;
using Game.Core.Ring;
using Game.Core.Ring.Career;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>
    /// Пропозиція вищої федерації на екрані кінця сезону (П5, правило А2 «два ключі», власник 11.11.2026: «А2, як радиш»). Два рядки —
    /// «Глядачі N/потрібно» і «Букер M/потрібно» (+ пояс, якщо зарахований) — і під кожним, чого бракує. Кнопки «Прийняти контракт» /
    /// «Лишитися» кличуть <see cref="CareerSession.AcceptOffer"/> / <see cref="CareerSession.DeclineOffer"/>. Після переходу — рядок
    /// про новий контракт і пояс; після звільнення з вищої федерації — куди повертаєшся. Розкладка й кольори — справа екрана («Меню»).
    /// <para>
    /// Контракт (§6.4, 12.11.2026): аванс переходу, дрібний шрифт — стільки, скільки прочитано (кнопка «Прочитати дрібний шрифт»,
    /// кидок Техніки раз на пропозицію, або менеджерка), і чи тримає чинна пастка «виключне право». Пастку екран бачить лише так.
    /// </para>
    /// </summary>
    public sealed class SeasonOfferModel
    {
        private readonly CareerSession _s;
        private readonly RingText _t;

        public SeasonOfferModel(CareerSession session, RingText text)
        {
            _s = session ?? throw new ArgumentNullException(nameof(session));
            _t = text ?? throw new ArgumentNullException(nameof(text));
        }

        /// <summary>
        /// Ключі пропозиції — лише до рішення: після «Прийняти» вони рахувалися б уже для нової федерації, після «Лишитися» вже не
        /// потрібні (лишається рядок стану, <see cref="StatusText"/>).
        /// </summary>
        private OfferKeys Keys { get { return _s.State.OfferDecision == 0 ? _s.GetOfferKeys() : null; } }

        /// <summary>Рядки ключів (заголовок, «Глядачі», «Букер») показувати — до рішення.</summary>
        public bool ShowKeys { get { return Visible && Keys != null; } }

        private string FedName(FederationDef f) { return f == null ? "" : _t.Get(f.NameKey); }

        /// <summary>Поточна федерація: «Залізна лава · рівень 1 з 4».</summary>
        public string FederationText
        {
            get { return _t.Format("c.fed.line", FedName(_s.Federation), _s.FederationTier, _s.Content.Federations.Count); }
        }

        /// <summary>Показувати блок: кінець сезону, підсумок остаточний (після титульного матчу чи відмови).</summary>
        public bool Visible { get { return _s.SeasonSummaryFinal && (Keys != null || _s.State.OfferDecision != 0); } }

        /// <summary>Вище нікуди — замість ключів рядок «вершина».</summary>
        public bool IsTop { get { var k = Keys; return k != null && !k.HasHigher; } }

        /// <summary>Пропозиція є і рішення ще не ухвалено — показати дві кнопки.</summary>
        public bool CanDecide { get { return _s.OfferAvailable; } }

        public string TitleText
        {
            get
            {
                var k = Keys;
                if (k == null) return "";
                if (!k.HasHigher) return _t.Format("c.offer.top", FedName(_s.Federation));
                var to = _s.Content.Federation(k.ToTier);
                return _t.Format(k.Ready ? "c.offer.title" : "c.offer.progress", FedName(to));
            }
        }

        public bool AudienceOk { get { var k = Keys; return k != null && k.AudienceOk; } }

        public string AudienceText
        {
            get { var k = Keys; return k == null || !k.HasHigher ? "" : _t.Format("c.offer.audience", k.Popularity, k.PopularityNeed); }
        }

        /// <summary>Чого бракує глядачам (null — ключ є).</summary>
        public string AudienceHint
        {
            get { var k = Keys; return k == null || !k.HasHigher || k.AudienceOk ? null : _t.Format("c.offer.need_audience", k.PopularityNeed - k.Popularity); }
        }

        public bool BookerOk { get { var k = Keys; return k != null && k.BookerOk; } }

        public string BookerText
        {
            get { var k = Keys; return k == null || !k.HasHigher ? "" : _t.Format("c.offer.booker", k.TrustCounted, k.TrustNeed); }
        }

        /// <summary>Пояс зарахований у довіру — позначка «+ пояс» біля рядка букера (null — не чемпіон).</summary>
        public string BeltMark { get { var k = Keys; return k != null && k.HasHigher && k.BeltCounted ? _t.Get("c.offer.belt") : null; } }

        public string BookerHint
        {
            get { var k = Keys; return k == null || !k.HasHigher || k.BookerOk ? null : _t.Format("c.offer.need_booker", k.TrustNeed - k.TrustCounted); }
        }

        /// <summary>
        /// Рядок стану під ключами: обидва ключі — «кличе»; чемпіон — попередження про прощальний матч; відмова — «повториться»
        /// (друга поспіль — «букер ображений»); перехід — новий контракт і доля пояса; звільнення з вищої федерації — куди.
        /// </summary>
        public string StatusText
        {
            get
            {
                var st = _s.State;
                if (st.Report != null && st.Report.Fired && st.Federation > 1)
                    return _t.Format("c.season.demoted", FedName(_s.Federation), FedName(_s.DemotionFederation));
                var from = _s.MovedFrom;
                if (from != null)
                {
                    if (st.FarewellResult == 1) return _t.Format("c.offer.farewell.ok", FedName(_s.Federation));
                    if (st.FarewellResult == 2) return _t.Format("c.offer.farewell.bad", FedName(from), FedName(_s.Federation));
                    return _t.Format("c.offer.moved", FedName(_s.Federation));
                }
                if (st.OfferDecision == 2)
                    return _t.Get(st.OfferDeclines >= CareerRules.DeclinesToOffend ? "c.offer.offended" : "c.offer.declined");
                if (!CanDecide) return null;
                return _t.Get(_s.IsChampion ? "c.offer.farewell" : "c.offer.ready");
            }
        }

        public string AcceptText { get { return _t.Get("c.offer.accept"); } }
        public string DeclineText { get { return _t.Get("c.offer.decline"); } }

        // ---------- контракт (§6.4) ----------

        private ContractView Contract { get { return _s.State.OfferDecision == 0 ? _s.GetOfferContract() : null; } }

        /// <summary>Рядки контракту (аванс, дрібний шрифт) показувати: є пропозиція, рішення ще немає.</summary>
        public bool ShowContract { get { return Visible && Contract != null; } }

        /// <summary>«Аванс при переході: 80» (з менеджеркою — з поміткою про +25 %).</summary>
        public string AdvanceText
        {
            get { var c = Contract; return c == null ? "" : _t.Format(c.HasManager ? "c.offer.advance.manager" : "c.offer.advance", c.Advance); }
        }

        /// <summary>Що видно в дрібному шрифті: не прочитано / нічого не розібрано / «щось є» чи «на око чисто» / пункт пастки чи «чесний».</summary>
        public string FinePrintText
        {
            get
            {
                var c = Contract;
                if (c == null) return "";
                switch (c.Sight)
                {
                    case ContractSight.Missed: return _t.Get("c.offer.fine.missed");
                    case ContractSight.Hint: return _t.Get(c.HintSomething ? "c.offer.fine.hint" : "c.offer.fine.clean_hint");
                    case ContractSight.Seen:
                        return c.ExclusiveSeen
                            ? _t.Format("c.offer.fine.exclusive", c.ExclusiveFeePercent, c.ExclusiveSeasons, c.ExclusivePenalty)
                            : _t.Get("c.offer.fine.clean");
                    default: return _t.Get("c.offer.fine.unread");
                }
            }
        }

        /// <summary>Дрібний шрифт насторожує (пункт пастки видно чи «щось є») — екран може це виділити; як — справа екрана.</summary>
        public bool FinePrintWarns { get { var c = Contract; return c != null && (c.ExclusiveSeen || c.HintSomething); } }

        /// <summary>«Папір перевірила менеджерка» (null — менеджерки немає).</summary>
        public string ManagerNote { get { var c = Contract; return c != null && c.HasManager ? _t.Get("c.offer.fine.manager") : null; } }

        /// <summary>Кнопка «Прочитати дрібний шрифт»: раз на пропозицію, без менеджерки.</summary>
        public bool CanRead { get { var c = Contract; return c != null && c.CanRead; } }

        public string ReadText { get { return _t.Get("c.offer.read"); } }

        /// <summary>Шанси кидка до кидка (2d6 + Техніка): «Техніка 2: усе видно — 58 %, щось видно — 36 %».</summary>
        public string ReadChanceText
        {
            get
            {
                var c = Contract;
                if (c == null || !c.CanRead) return null;
                var pct = RingChances.Percent(TwoD6.ChancesOf36(c.ReadModifier));
                return _t.Format("c.offer.read.chance", RingChances.Signed(c.ReadModifier), pct[(int)RollTier.Strong], pct[(int)RollTier.Partial]);
            }
        }

        /// <summary>Прочитати дрібний шрифт (кнопка). Після — <see cref="FinePrintText"/> показує, що видно.</summary>
        public ContractReadResult Read() { return _s.ReadOfferContract(); }

        /// <summary>
        /// Пастка тримає: «піти зараз — штраф N, а в гаманці M» (кнопок рішення немає, пропозиція повториться) чи «штраф N знімуть
        /// одразу» (кнопки є). null — чинної пастки немає.
        /// </summary>
        public string PenaltyText
        {
            get
            {
                var c = Contract;
                if (c == null || c.ExitPenalty <= 0) return null;
                return c.ExitLocked ? _t.Format("c.offer.locked", c.ExitPenalty, _s.State.Money) : _t.Format("c.offer.penalty", c.ExitPenalty);
            }
        }
    }
}
