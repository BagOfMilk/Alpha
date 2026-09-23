using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Signals;
using Game.Core.World;

namespace Game.Gameplay
{
    /// <summary>Положение солнца и общий свет для одной фазы суток.</summary>
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

    /// <summary>Заливающий свет: небо, горизонт, земля. Числа 0..1.</summary>
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
    /// Что сцена показывает про прошедшую фазу.
    ///
    /// ЗАЧЕМ ОТДЕЛЬНО ОТ MonoBehaviour. Здесь нет ни одного типа движка,
    /// поэтому правила показа проверяются обычными тестами и линтом сборки, а
    /// не глазами на скриншоте. Компонент сцены только применяет то, что
    /// посчитано здесь.
    ///
    /// ГРАНИЦА, КОТОРУЮ НЕЛЬЗЯ ПЕРЕЙТИ (инвариант 3): сюда приходит только то,
    /// что ядро отдаёт наружу — фаза, сутки, сигналы с ключами и тегами,
    /// исходы инцидентов и мудборд. Значения скрытых шкал недоступны физически,
    /// и собрать из этого дашборд Напряжения нельзя.
    /// </summary>
    public static class VillageView
    {
        /// <summary>Утреннее солнце против ночного: низкое, холодное и тусклое.</summary>
        public static SunPose SunFor(DayPhase phase)
        {
            return phase == DayPhase.Night
                ? new SunPose(8f, 205f, 0.22f, 0.55f, 0.62f, 0.85f)
                : new SunPose(38f, 145f, 1.25f, 1f, 0.96f, 0.86f);
        }

        /// <summary>
        /// Заливающий свет и цвет неба. День и ночь задают основу, мудборд её
        /// сдвигает: упадок гасит и обесцвечивает, достаток добавляет тепла.
        /// Это единственный разрешённый способ показать состояние города —
        /// через вид, а не через число.
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

            // Упадок: свет сереет (каналы сходятся к среднему) и падает.
            float grey = (skyR + skyG + skyB) / 3f;
            skyR = Mix(skyR, grey, decay * 0.7f) * (1f - decay * 0.25f);
            skyG = Mix(skyG, grey, decay * 0.7f) * (1f - decay * 0.25f);
            skyB = Mix(skyB, grey, decay * 0.7f) * (1f - decay * 0.25f);

            // Достаток: чуть теплее и светлее, но без ухода в карамель.
            skyR = Clamp01(skyR + prosperity * 0.06f);
            skyG = Clamp01(skyG + prosperity * 0.03f);

            return new AmbientPose(
                skyR, skyG, skyB,
                night ? 0.06f : 0.28f, night ? 0.06f : 0.26f, night ? 0.09f : 0.22f,
                backR, backG, backB);
        }

        /// <summary>Строка состояния: сутки, фаза и вид города словами.</summary>
        public static string Headline(DayReport report, MoodboardState mood)
        {
            string phase = report.Phase == DayPhase.Night ? "ночь" : "день";
            return "Сутки " + report.Day + " · " + phase + " · " + MoodWords(mood);
        }

        /// <summary>Как выглядит город: достаток и упадок словами, а не числами.</summary>
        public static string MoodWords(MoodboardState mood)
        {
            // Процветание в мудборде — это тир − 1 (Поправка №6.4): с тех пор
            // как тир растёт, его имя и есть лучшее описание достатка.
            string prosperity;
            if (mood.Prosperity >= 3) prosperity = "городок";
            else if (mood.Prosperity == 2) prosperity = "слобода";
            else if (mood.Prosperity == 1) prosperity = "село";
            else prosperity = "хутор";

            string decay;
            if (mood.Decay >= 3) decay = ", заколочен и замусорен";
            else if (mood.Decay == 2) decay = ", обветшал";
            else if (mood.Decay == 1) decay = ", кое-где запущен";
            else decay = "";

            return prosperity + decay;
        }

        /// <summary>
        /// Человеческие строки о прошедшей фазе: сначала что случилось, потом
        /// что слышно.
        ///
        /// ЧЕСТНАЯ ПОМЕТКА. Тексты здесь временные. По Поправке №3.4 реплики
        /// живут в таблицах контента, ядро отдаёт только ключ и теги, и
        /// писателю не нужен программист. Пока таблиц нет, ключи переводятся
        /// этим словарём, а незнакомый ключ показывается как есть — чтобы
        /// пропажа реплики была ВИДНА, а не молчала.
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

        private static string IncidentLine(IncidentOutcome outcome)
        {
            string what = TopicWords(outcome.TopicId);
            string how = BandWords(outcome.Band);

            string line = outcome.WasCrisis ? "КРИЗИС: " + what : what;
            line += " — " + how;

            if (outcome.WasUnmanned) line += "; на посту никого не было";
            if (outcome.CausedFear) line += "; община напугана";
            if (outcome.PopulationLost > 0) line += "; люди уходят";
            if (outcome.PeopleArrived > 0) line += "; с ними пришли ещё " + outcome.PeopleArrived;
            if (!string.IsNullOrEmpty(outcome.AffectedActorId)) line += " (" + outcome.AffectedActorId + ")";

            return line;
        }

        /// <summary>
        /// Предвестник читается ТОЛЬКО из сигнала: список предвестников в отчёте
        /// internal и сцене недоступен (инвариант 3). Ступень и домен приходят
        /// тегами — ровно столько, сколько игроку и положено знать.
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

            if (level >= 3) return "Беда близко, и она про " + (domain ?? "город");
            if (level == 2) return "Тревожно: разговоры про " + (domain ?? "город");
            return "Что-то назревает";
        }

        private static string SignalLine(SignalRequest request)
        {
            // Доклады с постов и ночной эмбиент — фон, они не должны забивать
            // ленту: в ней остаётся то, что изменилось.
            if (request.Channel == SignalChannel.PostReport) return null;
            if (request.Channel == SignalChannel.Ambient) return null;
            if (request.Channel == SignalChannel.Forewarning) return ForewarnLine(request);
            if (request.TopicId == null) return null;

            // Реплики об инцидентах уже выведены строкой исхода.
            if (request.TopicId.StartsWith("incident.")) return null;

            if (request.TopicId.StartsWith("tension.band."))
                return BandChangeWords(request.TopicId);

            // Ежедневный «как оно сегодня» — короткой строкой. Повторы гасит
            // лента: одна и та же фраза два дня подряд превращается в обои,
            // а слой сигналов существует ровно против этого (Поправка №3.4).
            if (request.TopicId.StartsWith("tension.ambient."))
                return AmbientWords(request.TopicId);

            // Что сделал город (Поправка №6): стройка, люди, тир, совет.
            if (request.TopicId.StartsWith("city.") || request.TopicId.StartsWith("council."))
                return CityWords(request);

            return "· " + request.TopicId;
        }

        private static string CityWords(SignalRequest request)
        {
            string topic = request.TopicId;
            string reason = Tag(request, "reason:");
            string count = Tag(request, "count:");

            if (topic.StartsWith("city.built."))
            {
                var def = Game.Core.Base.DefaultBuildings.Get(topic.Substring("city.built.".Length));
                return "Достроили: " + (def != null ? def.DisplayName : topic);
            }

            if (topic.StartsWith("city.tier."))
            {
                switch (topic.Substring("city.tier.".Length))
                {
                    case "2": return "Хутор стал селом";
                    case "3": return "Село разрослось в слободу";
                    case "4": return "Слобода стала городком";
                }
            }

            if (topic == "city.people.arrived")
            {
                string who = reason == "council" ? "позвал совет"
                    : reason == "expedition" ? "привёл отряд"
                    : "пришли сами";
                return "Пришли люди" + (count != null ? " (" + count + ")" : "") + ": " + who;
            }

            if (topic == "city.people.left")
            {
                string why = reason == "hunger" ? "голодно"
                    : reason == "fear" ? "боятся"
                    : "не держит здесь ничего";
                return "Ушли люди" + (count != null ? " (" + count + ")" : "") + ": " + why;
            }

            if (topic.StartsWith("city.crowd."))
                return Tag(request, "dir:") == "down" ? "Улицы пустеют" : "Людей на улицах стало больше";

            if (topic == "council.raid") return "Облава: стража прошла по дворам";

            return "· " + topic;
        }

        private static string Tag(SignalRequest request, string prefix)
        {
            var tags = request.Tags;
            if (tags == null) return null;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] != null && tags[i].StartsWith(prefix)) return tags[i].Substring(prefix.Length);
            return null;
        }

        private static string TopicWords(string topicId)
        {
            switch (topicId)
            {
                case "incident.petty_theft": return "Со склада пропадает припас";
                case "incident.market_brawl": return "Драка на рынке";
                case "incident.spoiled_stores": return "Запасы портятся";
                case "incident.sick_child": return "Больной ребёнок";
                case "incident.protection_racket": return "К торговцам ходят за долей";
                case "incident.missing_person": return "Пропал человек";
                case "incident.night_burglary": return "Ночная кража";
                case "incident.night_arson": return "Поджог";
                case "incident.crisis_riot": return "Бунт на площади";
                default: return topicId;
            }
        }

        private static string BandWords(OutcomeBand band)
        {
            switch (band)
            {
                case OutcomeBand.Best: return "разобрались лучше некуда";
                case OutcomeBand.Good: return "разобрались чисто";
                case OutcomeBand.Base: return "кое-как уладили";
                default: return "вышло скверно";
            }
        }

        /// <summary>Фоновое ощущение дня: одна короткая фраза вместо ключа.</summary>
        private static string AmbientWords(string topicId)
        {
            if (topicId.EndsWith("Calm")) return "Тихо";
            if (topicId.EndsWith("Murmur")) return "На улицах бурчат";
            if (topicId.EndsWith("Ferment")) return "Народ переговаривается по углам";
            if (topicId.EndsWith("Heat")) return "Взгляды тяжёлые";
            if (topicId.EndsWith("Fracture")) return "Каждый разговор на грани крика";
            return "· " + topicId;
        }

        private static string BandChangeWords(string topicId)
        {
            if (topicId.EndsWith("Calm")) return "Город притих";
            if (topicId.EndsWith("Murmur")) return "По улицам пошёл ропот";
            if (topicId.EndsWith("Ferment")) return "Люди сбиваются в кучки и спорят";
            if (topicId.EndsWith("Heat")) return "Воздух густой: вот-вот вспыхнет";
            if (topicId.EndsWith("Fracture")) return "Город трещит по швам";
            return "· " + topicId;
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        private static float Mix(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
    }
}
