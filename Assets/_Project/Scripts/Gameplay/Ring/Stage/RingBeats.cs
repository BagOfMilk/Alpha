using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Мітки подій кліпів рингу (<c>Assets/Art/Animations/ring_anims_events.txt</c>, жіночий набір —
    /// <c>ring_anims_f_events.txt</c>): рядок «&lt;кліп&gt; &lt;подія&gt; &lt;час с&gt; &lt;кістка A&gt; &lt;кістка B&gt;». Ключ — база
    /// парного кліпа без <c>_A</c>/<c>_B</c>; соло й рефері — повне ім'я. Чисті дані: читає режисер матчу і тести.
    /// </summary>
    public sealed class RingClipEvents
    {
        private readonly Dictionary<string, List<KeyValuePair<string, float>>> _m = new Dictionary<string, List<KeyValuePair<string, float>>>();

        public int Count { get; private set; }

        public static RingClipEvents Parse(string text)
        {
            var e = new RingClipEvents();
            e.Add(text);
            return e;
        }

        /// <summary>Додати ще один файл (жіночий набір): кліпи, яких ще немає, — повністю; наявні не чіпаються.</summary>
        public void Add(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var seen = new HashSet<string>(_m.Keys);
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3) continue;
                float t;
                if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out t)) continue;   // заголовок «Мітки …»
                if (seen.Contains(p[0])) continue;
                List<KeyValuePair<string, float>> list;
                if (!_m.TryGetValue(p[0], out list)) { list = new List<KeyValuePair<string, float>>(); _m[p[0]] = list; }
                list.Add(new KeyValuePair<string, float>(p[1], t));
                Count++;
            }
        }

        public bool Has(string clip) { return clip != null && _m.ContainsKey(clip); }

        /// <summary>Час n-ї (з нуля) мітки події в кліпі, с; -1 — немає.</summary>
        public float Time(string clip, string ev, int nth = 0)
        {
            List<KeyValuePair<string, float>> list;
            if (clip == null || !_m.TryGetValue(clip, out list)) return -1f;
            int k = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Key == ev) { if (k == nth) return list[i].Value; k++; }
            return -1f;
        }

        public IEnumerable<string> Clips { get { return _m.Keys; } }
    }

    /// <summary>Звідки взято мить такту.</summary>
    public enum BeatSource
    {
        /// <summary>Подія без кроків режисури (кінець ходу тощо): такт одразу.</summary>
        None = 0,
        /// <summary>Мітка <c>contact</c> кліпа (удар).</summary>
        Contact,
        /// <summary>Перша мітка <c>rope_hit</c> — спина в канаті.</summary>
        RopeHit,
        /// <summary>Мітка <c>corner_hit</c> — удар об кут.</summary>
        CornerHit,
        /// <summary>Виміряна мить (таблиця <see cref="RingBeats.Measured"/>): кліп без мітки.</summary>
        Measured,
        /// <summary>Мітка <c>release</c> (кидок у канат чи кут — відпустив).</summary>
        Release,
        /// <summary>Мітка <c>rope_lean</c> (заліз на кут).</summary>
        RopeLean,
        /// <summary>Мітка <c>raise</c> (рефері підняв руку переможцю) чи <c>bell</c>.</summary>
        Raise,
        /// <summary>Запасне правило: <see cref="RingBeats.RuleMax"/> с, але не пізніше <see cref="RingBeats.RuleFrac"/> довжини кліпа.</summary>
        Rule
    }

    /// <summary>План такту однієї події: на якому кроці й на якій миті кліпа кроку; для утримання — відлік рефері.</summary>
    public struct BeatPlan
    {
        /// <summary>Індекс кроку, що несе такт; -1 — кроків немає (такт одразу).</summary>
        public int Step;
        /// <summary>Мить такту в часі кліпа кроку, с (до <c>PlaySpeed</c>): від початку кліпа, коли він стартував.</summary>
        public float ClipTime;
        public BeatSource Source;
        /// <summary>Кліп, з якого взято мить (база пари, соло чи кліп рефері).</summary>
        public string Clip;
        /// <summary>Відлік утримання (кікаут): мітки кліпа рефері кроку — «раз», «два» і розв'язка (вирвався чи «три»).</summary>
        public bool IsCount;
        public float Count1, Count2, Final;
    }

    /// <summary>
    /// Такт події матчу — мить, коли текст (виноска, рядок журналу, звук, спалах) збігається з тілом. Береться з мітки
    /// кліпа кроку, а не з фіксованої затримки від початку події (огляд 07.11.2026: такт стояв на 0,33 с — раніше за
    /// підхід і за контакт). Пріоритет: мітки удару (<c>contact</c>, <c>rope_hit</c>, <c>corner_hit</c>, виміряні миті)
    /// на першому кроці, що їх має; інакше — мітки дії (<c>release</c>, <c>rope_lean</c>); інакше — запасне правило на
    /// першому кроці з кліпом. Утримання — відлік рефері (<c>count_1</c>, <c>count_2</c>, <c>kickout</c>/<c>count_3</c>).
    /// Правило записане й у <c>Assets/Art/Ring/Cast/CAST_ANIM.md</c> («Такт матчу»).
    /// </summary>
    public static class RingBeats
    {
        /// <summary>Запасне правило для кліпа без мітки й без виміру: не пізніше 0,4 с…</summary>
        public const float RuleMax = 0.4f;
        /// <summary>…і не пізніше 0,45 довжини кліпа.</summary>
        public const float RuleFrac = 0.45f;

        /// <summary>
        /// Виміряні миті для кліпів таблиці без мітки <c>contact</c> (сторінка анімацій, 08.11.2026: криві кісток обох
        /// борців, кожну мить перевірено кадрами). Ключ — як у файлі міток (база пари або соло).
        /// </summary>
        public static readonly Dictionary<string, float> Measured = new Dictionary<string, float>
        {
            { "Bodyslam", 1.65f },             // кинутий лягає спиною на мат (таз — перший мінімум після підйому, 1,67)
            { "Bump_Back", 0.52f },            // падіння: таз на маті
            { "Cover", 0.47f },                // груди нападника лягають на груди
            { "Center_Sidestep", 0.75f },      // промах: рука проходить над головою (найближчий прохід 0,72–0,80)
            { "Chain_Headlock", 0.50f },       // захват голови замкнено
            { "Chain_Headlock_Crank", 0.50f },
            { "Chain_ShoveOff", 0.25f },       // поштовх: руки відривають суперника
            { "GetUp_Back", 1.92f },           // підвівся: таз на висоті стійки
        };

        /// <summary>
        /// Варіант зі спільним підходом бази, що має мітку: усі <c>Lockup_*</c> починаються підходом <c>Lockup</c>
        /// (виміряно: руки на суперникові з 0,60 с, мітка бази — 0,616).
        /// </summary>
        public static string MarkBase(string clip)
        {
            if (clip != null && clip.StartsWith("Lockup_")) return "Lockup";
            return clip;
        }

        /// <summary>Кліп кроку для міток: пара — база, соло — ім'я, рефері — кліп рефері; кінець — підняття руки.</summary>
        public static string StepClip(StageStep st)
        {
            switch (st.Kind)
            {
                case StageStepKind.Pair:
                case StageStepKind.Solo: return st.Clip;
                case StageStepKind.Ref: return st.RefClip;
                case StageStepKind.End: return "Ref_Raise_Hand";
                // Склейка: план «на канаті/куті» такту не несе (удар спиною в канат — не контакт прийому); такт — на приземленні.
                default: return null;
            }
        }

        /// <summary>Мітка удару: contact, перший rope_hit, corner_hit, виміряна мить.</summary>
        public static bool ImpactMark(string clip, RingClipEvents ev, out float t, out BeatSource src)
        {
            t = -1f; src = BeatSource.None;
            if (clip == null) return false;
            string mb = MarkBase(clip);
            if (ev != null)
            {
                if ((t = ev.Time(mb, "contact")) >= 0f) { src = BeatSource.Contact; return true; }
                if ((t = ev.Time(clip, "rope_hit")) >= 0f) { src = BeatSource.RopeHit; return true; }
                if ((t = ev.Time(clip, "corner_hit")) >= 0f) { src = BeatSource.CornerHit; return true; }
            }
            float m;
            if (Measured.TryGetValue(clip, out m)) { t = m; src = BeatSource.Measured; return true; }
            t = -1f;
            return false;
        }

        /// <summary>Мітка дії: release, rope_lean, raise, bell.</summary>
        public static bool ActionMark(string clip, RingClipEvents ev, out float t, out BeatSource src)
        {
            t = -1f; src = BeatSource.None;
            if (clip == null || ev == null) return false;
            if ((t = ev.Time(clip, "release")) >= 0f) { src = BeatSource.Release; return true; }
            if ((t = ev.Time(clip, "rope_lean")) >= 0f) { src = BeatSource.RopeLean; return true; }
            if ((t = ev.Time(clip, "raise")) >= 0f) { src = BeatSource.Raise; return true; }
            if ((t = ev.Time(clip, "bell")) >= 0f) { src = BeatSource.Raise; return true; }
            t = -1f;
            return false;
        }

        /// <summary>Запасне правило: min(0,4 с; 0,45 довжини).</summary>
        public static float Rule(float clipLength)
        {
            return clipLength > 0f ? Math.Min(RuleMax, RuleFrac * clipLength) : RuleMax;
        }

        /// <summary>
        /// План такту для кроків події. <paramref name="clipLength"/> — довжина кліпа за іменем кроку (база пари — довжина
        /// <c>_A</c>), с; невідомий — 0.
        /// </summary>
        public static BeatPlan For(IList<StageStep> steps, RingClipEvents ev, Func<string, float> clipLength)
        {
            var p = new BeatPlan { Step = -1, ClipTime = 0f, Source = BeatSource.None, Count1 = -1f, Count2 = -1f, Final = -1f };
            if (steps == null || steps.Count == 0) return p;

            // Утримання: відлік рефері (кліп рефері кроку з мітками count_*).
            for (int i = 0; i < steps.Count; i++)
            {
                string rc = steps[i].RefClip;
                if (steps[i].Kind != StageStepKind.Pair || rc == null || ev == null) continue;
                float c1 = ev.Time(rc, "count_1"), c2 = ev.Time(rc, "count_2");
                if (c1 < 0f || c2 < 0f) continue;
                float fin = ev.Time(rc, "count_3");
                if (fin < 0f) fin = ev.Time(rc, "kickout");
                if (fin < 0f) fin = c2;
                p.Step = i; p.IsCount = true; p.Count1 = c1; p.Count2 = c2; p.Final = fin;
                p.ClipTime = fin; p.Source = BeatSource.Contact; p.Clip = rc;
                return p;
            }

            float t; BeatSource s;
            // Кроки-наслідки (суперник підвівся, рефері попереджає) такту не несуть — хіба що інших кроків немає.
            bool anyMain = false;
            for (int i = 0; i < steps.Count; i++) if (!steps[i].Aside) anyMain = true;
            Func<int, bool> skip = i => anyMain && steps[i].Aside;
            for (int i = 0; i < steps.Count; i++)
                if (!skip(i) && ImpactMark(StepClip(steps[i]), ev, out t, out s)) { p.Step = i; p.ClipTime = t; p.Source = s; p.Clip = StepClip(steps[i]); return p; }
            for (int i = 0; i < steps.Count; i++)
                if (!skip(i) && ActionMark(StepClip(steps[i]), ev, out t, out s)) { p.Step = i; p.ClipTime = t; p.Source = s; p.Clip = StepClip(steps[i]); return p; }
            for (int i = 0; i < steps.Count; i++)
            {
                string c = StepClip(steps[i]);
                if (c == null || skip(i)) continue;
                float len = clipLength != null ? clipLength(c) : 0f;
                if (steps[i].Kind == StageStepKind.Solo && steps[i].Once && len > 0f) len = Math.Min(len, 1.7f);
                p.Step = i; p.ClipTime = Rule(len); p.Source = BeatSource.Rule; p.Clip = c;
                return p;
            }
            // Лише склейка без приземлення: такт на початку кроку.
            p.Step = 0; p.ClipTime = 0f; p.Source = BeatSource.None;
            return p;
        }
    }
}
