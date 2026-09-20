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
            switch (r.TopicId)
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
                    if (r.TopicId != null && r.TopicId.StartsWith("post.")) return "сводка по домену";
                    if (r.TopicId != null && r.TopicId.StartsWith("tension.band.")) return "«Меняется. И не в лучшую сторону».";
                    return r.TopicId;
            }
        }
    }
}
