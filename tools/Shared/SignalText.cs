using Game.Core.Signals;

namespace Alpha.Shared
{
    /// <summary>
    /// Текст сигналов по ключам — ПЛЕЙСХОЛДЕР для консоли.
    ///
    /// Настоящие реплики живут в SO-таблицах (инвариант: писателю не нужен
    /// программист), но консольная сборка до Unity не дотягивается. Здесь — тот
    /// же принцип в миниатюре: ядро выдаёт TopicId, текст подбирается по ключу,
    /// и ни одна строка не зашита в правила.
    /// </summary>
    public static class SignalText
    {
        public static string Speaker(SignalChannel channel)
        {
            switch (channel)
            {
                case SignalChannel.CitizenLine: return "горожанин";
                case SignalChannel.CompanionLine: return "напарник";
                case SignalChannel.Forewarning: return "слух";
                case SignalChannel.PostReport: return "доклад";
                case SignalChannel.Ambient: return "город";
                default: return "—";
            }
        }

        public static string Line(SignalRequest r)
        {
            // Предвестник называет СВОЙ домен. Ядро их различает (subjectId и
            // тег domain:), а таблица склеивала две разные угрозы в одну
            // строку — и игрок видел «Собаки брешут» дважды подряд, не понимая,
            // что это про разные места.
            if (r.TopicId != null && r.TopicId.StartsWith("forewarn.level"))
            {
                string where = Domain(r);
                return Text(r.TopicId) + (where == null ? "" : " — " + where);
            }
            return Text(r.TopicId);
        }

        private static string Domain(SignalRequest r)
        {
            if (r.Tags == null) return null;
            for (int i = 0; i < r.Tags.Length; i++)
                if (r.Tags[i] != null && r.Tags[i].StartsWith("domain:"))
                {
                    string value = r.Tags[i].Substring("domain:".Length);
                    return string.IsNullOrEmpty(value) ? null : value;
                }
            return null;
        }

        private static string Text(string topicId)
        {
            switch (topicId)
            {
                case "tension.ambient.Calm": return "«Хорошо, что вы здесь». Дети во дворах.";
                case "tension.ambient.Murmur": return "У колодца спорят о ценах.";
                case "tension.ambient.Ferment": return "Разговор смолкает, когда подходишь.";
                case "tension.ambient.Heat": return "Ставни закрыты днём. Патруль ходит парами.";
                case "tension.ambient.Fracture": return "Площадь пуста. Оружие носят открыто.";

                case "forewarn.level1": return "«Собаки третью ночь брешут».";
                case "forewarn.level2": return "«Третий день топчется один и тот же».";
                case "forewarn.level3": return "«Что-то готовят. Скоро».";

                case "night.ambient.Calm": return "Тихо. Только ветер.";
                case "night.ambient.Murmur": return "Где-то хлопнула ставня.";
                case "night.ambient.Ferment": return "Шаги за углом стихли, когда обернулся.";
                case "night.ambient.Heat": return "Костры в бочках. Голоса не местные.";
                case "night.ambient.Fracture": return "Ни одного огня в окнах.";

                default:
                    if (topicId != null && topicId.StartsWith("post.")) return "сводка по домену";
                    if (topicId != null && topicId.StartsWith("tension.band.")) return "«Меняется. И не в лучшую сторону».";
                    return topicId;
            }
        }
    }
}
