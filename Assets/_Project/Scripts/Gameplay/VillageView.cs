using System.Collections.Generic;
using Game.Core.Characters.Creation;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Signals;
using Game.Core.World;
using Game.Gameplay.Text;

namespace Game.Gameplay
{
    /// <summary>Положення сонця і загальне світло для однієї фази доби.</summary>
    public readonly struct SunPose
    {
        public readonly float Pitch;
        public readonly float Yaw;
        public readonly float Intensity;
        public readonly float R, G, B;

        public SunPose(float pitch, float yaw, float intensity, float r, float g, float b)
        {
            Pitch = pitch; Yaw = yaw; Intensity = intensity;
            R = r; G = g; B = b;
        }
    }

    /// <summary>Заливне світло: небо, горизонт, земля. Числа 0..1.</summary>
    public readonly struct AmbientPose
    {
        public readonly float SkyR, SkyG, SkyB;
        public readonly float GroundR, GroundG, GroundB;
        public readonly float BackR, BackG, BackB;

        public AmbientPose(float skyR, float skyG, float skyB,
                           float groundR, float groundG, float groundB,
                           float backR, float backG, float backB)
        {
            SkyR = skyR; SkyG = skyG; SkyB = skyB;
            GroundR = groundR; GroundG = groundG; GroundB = groundB;
            BackR = backR; BackG = backG; BackB = backB;
        }
    }

    /// <summary>
    /// Що сцена показує про минулу фазу.
    ///
    /// НАВІЩО ОКРЕМО ВІД MonoBehaviour. Тут нема жодного типу рушія,
    /// тому правила показу перевіряються звичайними тестами і лінтом збірки, а
    /// не очима на скриншоті. Компонент сцени лише застосовує те, що
    /// пораховано тут.
    ///
    /// МЕЖА, ЯКУ НЕ МОЖНА ПЕРЕЙТИ (інваріант 3): сюди приходить лише те,
    /// що ядро віддає назовні — фаза, доба, сигнали з ключами й тегами,
    /// наслідки інцидентів і мудборд. Значення прихованих шкал недоступні фізично,
    /// і зібрати з цього дашборд Напруги не можна.
    /// </summary>
    public static class VillageView
    {
        /// <summary>Ранкове сонце проти нічного: низьке, холодне і тьмяне.</summary>
        public static SunPose SunFor(DayPhase phase)
        {
            return phase == DayPhase.Night
                ? new SunPose(8f, 205f, 0.22f, 0.55f, 0.62f, 0.85f)
                : new SunPose(38f, 145f, 1.25f, 1f, 0.96f, 0.86f);
        }

        /// <summary>
        /// Заливне світло і колір неба. День і ніч задають основу, мудборд її
        /// зсуває: занепад гасить і знебарвлює, достаток додає тепла.
        /// Це єдиний дозволений спосіб показати стан міста —
        /// через вигляд, а не через число.
        /// </summary>
        public static AmbientPose AmbientFor(DayPhase phase, MoodboardState mood)
        {
            bool night = phase == DayPhase.Night;

            float decay = Clamp01(mood.Decay / 4f);
            float prosperity = Clamp01(mood.Prosperity / 4f);

            float skyR = night ? 0.16f : 0.62f;
            float skyG = night ? 0.19f : 0.72f;
            float skyB = night ? 0.30f : 0.82f;

            float backR = night ? 0.09f : 0.55f;
            float backG = night ? 0.11f : 0.68f;
            float backB = night ? 0.20f : 0.78f;

            // Занепад: світло сіріє (канали сходяться до середнього) і падає.
            float grey = (skyR + skyG + skyB) / 3f;
            skyR = Mix(skyR, grey, decay * 0.7f) * (1f - decay * 0.25f);
            skyG = Mix(skyG, grey, decay * 0.7f) * (1f - decay * 0.25f);
            skyB = Mix(skyB, grey, decay * 0.7f) * (1f - decay * 0.25f);

            // Достаток: трохи тепліше і світліше, але без відходу в карамель.
            skyR = Clamp01(skyR + prosperity * 0.06f);
            skyG = Clamp01(skyG + prosperity * 0.03f);

            return new AmbientPose(
                skyR, skyG, skyB,
                night ? 0.06f : 0.28f, night ? 0.06f : 0.26f, night ? 0.09f : 0.22f,
                backR, backG, backB);
        }

        /// <summary>Слова гравцю, а не рід протагоніста (VillageView сама його не знає) — стала стать лукапу.</summary>
        private const Gender NeutralGender = Gender.Male;

        /// <summary>Рядок стану: доба, фаза і вигляд міста словами.</summary>
        public static string Headline(DayReport report, MoodboardState mood)
        {
            string phaseKey = report.Phase == DayPhase.Night ? "village.headline.phase.night" : "village.headline.phase.day";
            string phase = UkrainianText.Get(phaseKey, NeutralGender);
            return UkrainianText.Format("village.headline", NeutralGender,
                "day", report.Day.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "phase", phase,
                "mood", MoodWords(mood));
        }

        /// <summary>Заголовок до перших діб: доба 0, ранок, вигляд міста.</summary>
        public static string OpeningHeadline(MoodboardState mood)
        {
            return UkrainianText.Format("village.headline", NeutralGender,
                "day", "0",
                "phase", UkrainianText.Get("village.headline.phase.morning", NeutralGender),
                "mood", MoodWords(mood));
        }

        /// <summary>Перший рядок стрічки: хутір прокидається, скільки в ньому людей.</summary>
        public static string OpeningLine(int people)
        {
            return UkrainianText.Format("village.opening", NeutralGender,
                "count", people.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>Як виглядає місто: достаток і занепад словами, а не числами.</summary>
        public static string MoodWords(MoodboardState mood)
        {
            // Процвітання в мудборді — це тір − 1 (Поправка №6.4): відколи
            // тір росте, його назва і є найкращим описом достатку.
            int tier = mood.Prosperity >= 3 ? 3 : (mood.Prosperity < 0 ? 0 : mood.Prosperity);
            int decayStep = mood.Decay >= 3 ? 3 : (mood.Decay < 0 ? 0 : mood.Decay);

            string tierKey = "village.mood.tier." + tier;
            string prosperity = UkrainianText.Get(tierKey, NeutralGender);

            // decayStep==0 навмисно немає рядка в таблиці (AddKey забороняє
            // порожній текст) — порожній суфікс і є правильним результатом.
            string decayKey = "village.mood.decay." + decayStep;
            string decay = UkrainianText.Has(decayKey, NeutralGender) ? UkrainianText.Get(decayKey, NeutralGender) : "";

            return prosperity + decay;
        }

        /// <summary>
        /// Людські рядки про минулу фазу: спершу що сталося, потім
        /// що чутно.
        ///
        /// Пакет E3b: репліки йдуть через <see cref="UkrainianText"/> (Поправка
        /// №3.4 — письменнику не потрібен програміст, R7 — Core віддає лише ключ).
        /// Незнайомий ключ (якого таблиця ще не знає) як і раніше
        /// показується як є ("· topicId") — пропажа репліки зобов'язана бути
        /// ВИДНА, а не мовчати.
        /// </summary>
        public static List<string> Lines(DayReport report)
        {
            var lines = new List<string>();
            if (report == null) return lines;

            for (int i = 0; i < report.Incidents.Count; i++)
                lines.Add(IncidentLine(report.Incidents[i]));

            if (report.Signals != null && report.Signals.Requests != null)
                for (int i = 0; i < report.Signals.Requests.Count; i++)
                {
                    string line = SignalLine(report.Signals.Requests[i]);
                    if (line != null) lines.Add(line);
                }

            return lines;
        }

        /// <summary>
        /// Що замовив хазяїн — словами, щоб у стрічці було видно, чому місто
        /// змінюється. Вхід — рядок <c>Steward.Act</c>: токени через пробіл
        /// ("build:&lt;id&gt;", "staff:&lt;хто&gt;@&lt;пост&gt;", "raid", "settlers").
        ///
        /// R7: назви будівлі, жителя й поста — лише з таблиці за id
        /// ("building.&lt;id&gt;", "char.&lt;id&gt;", "post.&lt;id&gt;"), а не
        /// Core-контентний DisplayName. Нема перекладу — лишається сирий id:
        /// пропажа тексту має бути видна, а не мовчати.
        /// </summary>
        public static List<string> OrderLines(string did)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(did)) return lines;

            foreach (var part in did.Split(' '))
            {
                if (part.StartsWith("build:"))
                {
                    lines.Add(UkrainianText.Format("village.order.build", NeutralGender,
                        "building", NameById("building.", part.Substring(6))));
                }
                else if (part.StartsWith("staff:"))
                {
                    var pair = part.Substring(6).Split('@');
                    if (pair.Length == 2 && pair[0].Length > 0 && pair[1].Length > 0)
                        lines.Add(UkrainianText.Format("village.order.staff", NeutralGender,
                            "who", NameById("char.", pair[0]),
                            "post", NameById("post.", pair[1])));
                }
                else if (part == "raid") lines.Add(UkrainianText.Get("village.order.raid", NeutralGender));
                else if (part == "settlers") lines.Add(UkrainianText.Get("village.order.settlers", NeutralGender));
            }

            return lines;
        }

        /// <summary>Назва за ключем "&lt;prefix&gt;&lt;id&gt;"; нема в таблиці — сам id, щоб пропажу було видно.</summary>
        private static string NameById(string prefix, string id)
        {
            string key = prefix + id;
            return UkrainianText.Has(key, NeutralGender) ? UkrainianText.Get(key, NeutralGender) : id;
        }

        private static string IncidentLine(IncidentOutcome outcome)
        {
            string what = TopicWords(outcome.TopicId);
            string how = BandWords(outcome.Band);

            string template = outcome.WasCrisis ? "village.incident.crisis_line" : "village.incident.line";
            string line = UkrainianText.Format(template, NeutralGender, "what", what, "how", how);

            if (outcome.WasUnmanned) line += UkrainianText.Get("village.incident.suffix.unmanned", NeutralGender);
            if (outcome.CausedFear) line += UkrainianText.Get("village.incident.suffix.fear", NeutralGender);
            if (outcome.PopulationLost > 0) line += UkrainianText.Get("village.incident.suffix.population_lost", NeutralGender);
            if (outcome.PeopleArrived > 0)
                line += UkrainianText.Format("village.incident.suffix.people_arrived", NeutralGender,
                    "count", outcome.PeopleArrived.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(outcome.AffectedActorId))
            {
                // R7: гравець не бачить сирий Core-id напарника — тільки ім'я
                // з таблиці ("char.<id>"). Якщо перекладу нема (ще не заведений
                // напарник), id лишається видимим — пропажа тексту зобов'язана
                // бути помітною, а не мовчати (той самий принцип, що й вище
                // для незнайомого ключа сигналу).
                line += " (" + NameById("char.", outcome.AffectedActorId) + ")";
            }

            return line;
        }

        /// <summary>
        /// Передвісник читається ТІЛЬКИ з сигналу: список передвісників у звіті
        /// internal і сцені недоступний (інваріант 3). Ступінь і домен приходять
        /// тегами — рівно стільки, скільки гравцю й належить знати.
        /// </summary>
        private static string ForewarnLine(SignalRequest request)
        {
            int level = 1;
            string domain = null;

            var tags = request.Tags;
            if (tags != null)
                for (int i = 0; i < tags.Length; i++)
                {
                    string tag = tags[i];
                    if (tag == null) continue;
                    if (tag.StartsWith("level:"))
                    {
                        int parsed;
                        if (int.TryParse(tag.Substring(6), out parsed)) level = parsed;
                    }
                    else if (tag.StartsWith("domain:"))
                    {
                        domain = tag.Substring(7);
                    }
                }

            if (level >= 3)
                return UkrainianText.Format("village.forewarn.level3", NeutralGender, "domain", DomainAccusative(domain));
            if (level == 2)
                return UkrainianText.Format("village.forewarn.level2", NeutralGender, "domain", DomainAccusative(domain));
            return UkrainianText.Get("village.forewarn.level1", NeutralGender);
        }

        /// <summary>
        /// Тег домену ядра («площадь», «улицы», «ночь» — внутрішні, російські)
        /// словом для «розмови про {domain}» — знахідний відмінок
        /// (<c>domain.acc.*</c>). Раніше тег ішов у стрічку сирим: «Тривожно:
        /// розмови про улицы». Невідомий тег — загальне «місто», не сирий рядок.
        /// </summary>
        private static string DomainAccusative(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return UkrainianText.Get("domain.acc.default", NeutralGender);
            string key = "domain.acc." + domain;
            return UkrainianText.Has(key, NeutralGender)
                ? UkrainianText.Get(key, NeutralGender)
                : UkrainianText.Get("domain.acc.default", NeutralGender);
        }

        private static string SignalLine(SignalRequest request)
        {
            // Доповіді з постів і нічний ембієнт — фон, вони не мають забивати
            // стрічку: у ній лишається те, що змінилося.
            if (request.Channel == SignalChannel.PostReport) return null;
            if (request.Channel == SignalChannel.Ambient) return null;
            if (request.Channel == SignalChannel.Forewarning) return ForewarnLine(request);
            if (request.TopicId == null) return null;

            // Репліки про інциденти вже виведені рядком наслідку.
            if (request.TopicId.StartsWith("incident.")) return null;

            // "tension.band.<Band>"/"tension.ambient.<Band>" — TopicId САМ і є
            // ключем таблиці (SignalComposer.Compose будує його буквально так,
            // AddAmbientAndThreatBandKeys у UkrainianText.cs): жодного окремого
            // switch/EndsWith тут більше не треба, ключ береться як є.
            if (request.TopicId.StartsWith("tension.band.") || request.TopicId.StartsWith("tension.ambient."))
                return UkrainianText.Has(request.TopicId, NeutralGender)
                    ? UkrainianText.Get(request.TopicId, NeutralGender)
                    : "· " + request.TopicId;

            // Що зробило місто (Поправка №6): стройка, люди, тір, рада.
            if (request.TopicId.StartsWith("city.") || request.TopicId.StartsWith("council."))
                return CityWords(request);

            return "· " + request.TopicId;
        }

        private static string CityWords(SignalRequest request)
        {
            string topic = request.TopicId;
            string reason = Tag(request, "reason:");
            string count = Tag(request, "count:");

            // "city.built.<id>"/"city.tier.<n>" — TopicId сам і є готовим
            // ключем (уже повне речення, "Збудовано: <Здание>."/"<Тир-фраза>."
            // — AddCityAndCouncilEvents у UkrainianText.cs): R7 більше не
            // читає Core-контентний DefaultBuildings.Get(id).DisplayName напряму.
            if ((topic.StartsWith("city.built.") || topic.StartsWith("city.tier.")) &&
                UkrainianText.Has(topic, NeutralGender))
                return UkrainianText.Get(topic, NeutralGender);

            if (topic == "city.people.arrived")
            {
                string key = reason == "council" ? "village.city.people.arrived.council"
                    : reason == "expedition" ? "village.city.people.arrived.expedition"
                    : "village.city.people.arrived.other";
                return UkrainianText.Get(key, NeutralGender) + CountSuffix(count);
            }

            if (topic == "city.people.left")
            {
                string key = reason == "hunger" ? "village.city.people.left.hunger"
                    : reason == "fear" ? "village.city.people.left.fear"
                    : "village.city.people.left.other";
                return UkrainianText.Get(key, NeutralGender) + CountSuffix(count);
            }

            if (topic.StartsWith("city.crowd."))
                return UkrainianText.Get(Tag(request, "dir:") == "down" ? "village.city.crowd.down" : "village.city.crowd.up",
                    NeutralGender);

            // Реальні council.*-топіки з Core/Base/Buildings/CityWorks*.cs
            // (council.decree.ordered/diplomacy.ordered/prepare_threat.ordered/
            // outfit_expedition.ordered/raid/invest.payout) — TopicId сам і є
            // готовим ключем перекладу (AddCouncilActions у UkrainianText.cs),
            // так само, як вище для "city.built."/"city.tier.".
            if (topic.StartsWith("council.") && UkrainianText.Has(topic, NeutralGender))
                return UkrainianText.Get(topic, NeutralGender);

            return "· " + topic;
        }

        private static string CountSuffix(string count) =>
            count == null ? "" : UkrainianText.Format("village.city.count_suffix", NeutralGender, "count", count);

        private static string Tag(SignalRequest request, string prefix)
        {
            var tags = request.Tags;
            if (tags == null) return null;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] != null && tags[i].StartsWith(prefix)) return tags[i].Substring(prefix.Length);
            return null;
        }

        /// <summary>"incident.&lt;id&gt;.title" — той самий ключ, яким Core-топік вже і є (§7.6/§7.8/AddIncidentHeadlinesAndOutcomes), лише з суфіксом ".title".</summary>
        private static string TopicWords(string topicId)
        {
            if (string.IsNullOrEmpty(topicId)) return topicId;
            string key = topicId + ".title";
            return UkrainianText.Has(key, NeutralGender) ? UkrainianText.Get(key, NeutralGender) : topicId;
        }

        private static string BandWords(OutcomeBand band)
        {
            switch (band)
            {
                case OutcomeBand.Best: return UkrainianText.Get("village.band.best", NeutralGender);
                case OutcomeBand.Good: return UkrainianText.Get("village.band.good", NeutralGender);
                case OutcomeBand.Base: return UkrainianText.Get("village.band.base", NeutralGender);
                default: return UkrainianText.Get("village.band.worst", NeutralGender);
            }
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        private static float Mix(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
    }
}
