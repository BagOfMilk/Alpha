using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>
    /// Жіночі форми звертання до героїні (П5, ПМ 12.11.2026: «Жіночі варіанти тексту (`&lt;ключ&gt;.f` …) — так»). Поруч із ключем,
    /// де герой у чоловічому роді («Ти виграв», «хлопче», «я сам»), лежить «ключ.f» з жіночою формою. <see cref="RingText"/>
    /// відступу на базовий ключ не знає, тож вибір робиться тут, а не в екранах: для героїні базовий ключ отримує текст «.f», для
    /// героя — повертається свій. Тоді промо, закулісся, кінцівки й кар'єра беруть звичайний ключ і нічого не знають про стать.
    /// <para>
    /// Форма — лише там, де є обидва ключі: пари статі на кшталт «c.look.sex.m / c.look.sex.f» базового ключа не мають і
    /// не чіпаються. Свій (чоловічий) текст кожного екземпляра <see cref="RingText"/> запам'ятовується окремо, поза словником, при
    /// першому виклику — повторний виклик і новий екземпляр (меню перечитує тексти) безпечні. Хто потім кладе в базовий ключ інший
    /// текст через <see cref="RingText.Add"/>, той для ключів із «.f» його втратить — для таких ключів не робити (ім'я героя
    /// «w.player.name» жіночої форми не має).
    /// </para>
    /// <para>
    /// Рід суперника (рішення ПМ 12.11.2026, P2-6: суперник своєї статі, мішаний матч — подія): «ключ.rf» — текст для героїні
    /// проти суперниці («Сильна суперниця», «вона тебе вже била»). Герой проти жінки за правилами кар'єри не буває, тому «.rf»
    /// діє лише разом із героїнею; героїня проти Молота бере «.f» або базовий текст. Підміна глобальна, тож «.rf» — лише в ключах
    /// картки тижня, де на екрані один суперник (білий перелік в охоронці HeroFormsTests). Екран із кількома суперниками (афіша,
    /// підсумок сезону) «.rf» не бере; текст, що живе довше за тиждень (чутка в підсумку тижня), пишеться нейтрально. Назви шляхів («Герой / Героїня») — не тут:
    /// їх показують і героєві, і суперникові на одному екрані, тож рід — за статтю борця (<see cref="RingLabels.PathName(RingText, Game.Core.Ring.RingPath, bool)"/>).
    /// </para>
    /// </summary>
    public static class HeroForms
    {
        public const string FemaleSuffix = ".f";
        /// <summary>Героїня проти суперниці: «ключ.rf».</summary>
        public const string RivalSuffix = ".rf";

        /// <summary>Свої (базові) тексти екземпляра й чинний режим — поза словником.</summary>
        private sealed class Mode
        {
            public readonly Dictionary<string, string> Own = new Dictionary<string, string>(StringComparer.Ordinal);
            public bool Heroine, RivalWoman;
        }

        private static readonly ConditionalWeakTable<RingText, Mode> Modes = new ConditionalWeakTable<RingText, Mode>();

        public static string FemaleKey(string key) { return key + FemaleSuffix; }
        public static string RivalKey(string key) { return key + RivalSuffix; }

        /// <summary>Чи підставлено в цей текст форми героїні (останній <see cref="Apply(RingText, bool, bool)"/>).</summary>
        public static bool ShowsHeroine(RingText text)
        {
            Mode m;
            return text != null && Modes.TryGetValue(text, out m) && m.Heroine;
        }

        /// <summary>Чи підставлено в цей текст форми «героїня проти суперниці».</summary>
        public static bool ShowsRivalWoman(RingText text)
        {
            Mode m;
            return text != null && Modes.TryGetValue(text, out m) && m.RivalWoman;
        }

        /// <summary>Суперник із картки тижня — жінка (на титулі й створенні картки ще немає). Молот — чоловік.</summary>
        public static bool OpponentIsWoman(Game.Core.Ring.Career.CareerSession s)
        {
            if (s == null || s.Stage == Game.Core.Ring.Career.CareerStage.Title || s.Stage == Game.Core.Ring.Career.CareerStage.Create) return false;
            var npc = s.GetShowCard().Opponent;
            return npc != null && npc.Female;
        }

        /// <summary>
        /// Чи показувати жіночі форми. На створенні ядро стать ще не знає (SyncHeroSex — після CreateHero): вона береться з тіла,
        /// вибраного на екрані (body_f_*), — і підписи «Героїня / Лиходійка» міняються разом із тілом (рішення ПМ 12.11.2026).
        /// На титулі — базові тексти; далі — стать кар'єри.
        /// </summary>
        public static bool IsHeroine(Game.Core.Ring.Career.CareerSession s, int heroBody)
        {
            if (s == null || s.Stage == Game.Core.Ring.Career.CareerStage.Title) return false;
            if (s.Stage == Game.Core.Ring.Career.CareerStage.Create)
            {
                var bodies = CareerLooks.HeroBodies;
                return bodies.Length > 0 && CareerLooks.IsFemaleBody(bodies[Math.Max(0, Math.Min(heroBody, bodies.Length - 1))]);
            }
            return s.State.FemaleKnown && s.State.Female;
        }

        /// <summary>Базові ключі, що мають жіночу форму: є і «ключ», і «ключ.f».</summary>
        public static List<string> FormKeys(IDictionary<string, string> d) { return KeysWith(d, FemaleSuffix); }

        /// <summary>Базові ключі, що мають форму «героїня проти суперниці»: є і «ключ», і «ключ.rf».</summary>
        public static List<string> RivalKeys(IDictionary<string, string> d) { return KeysWith(d, RivalSuffix); }

        private static List<string> KeysWith(IDictionary<string, string> d, string suffix)
        {
            var res = new List<string>();
            if (d == null) return res;
            foreach (var k in d.Keys)
            {
                if (k.Length <= suffix.Length || !k.EndsWith(suffix, StringComparison.Ordinal)) continue;
                var bk = k.Substring(0, k.Length - suffix.Length);
                if (d.ContainsKey(bk)) res.Add(bk);
            }
            res.Sort(StringComparer.Ordinal);
            return res;
        }

        /// <summary>Підставити форми героїні (<paramref name="female"/>) чи повернути свої; суперник — чоловік.</summary>
        public static int Apply(RingText text, bool female) { return Apply(text, female, false); }

        /// <summary>
        /// Підставити форми: героїня проти суперниці — «.rf», де є; інакше героїня — «.f», де є; інакше свій текст. Суперниця без
        /// героїні не діє (такого матчу кар'єра не ставить). Повертає, скільки ключів має бодай одну форму.
        /// </summary>
        public static int Apply(RingText text, bool female, bool rivalFemale)
        {
            if (text == null) return 0;
            var d = text.AsDictionary();
            var mode = Modes.GetValue(text, _ => new Mode());
            var keys = new SortedSet<string>(FormKeys(d), StringComparer.Ordinal);
            keys.UnionWith(RivalKeys(d));
            bool rival = female && rivalFemale;
            foreach (var bk in keys)
            {
                string mine;
                if (!mode.Own.TryGetValue(bk, out mine)) { mine = d[bk]; mode.Own[bk] = mine; }
                string form;
                if (rival && d.TryGetValue(RivalKey(bk), out form)) d[bk] = form;
                else if (female && d.TryGetValue(FemaleKey(bk), out form)) d[bk] = form;
                else d[bk] = mine;
            }
            mode.Heroine = female;
            mode.RivalWoman = rival;
            return keys.Count;
        }
    }
}
