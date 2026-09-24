using System.Collections.Generic;

namespace Game.Core.Quests
{
    /// <summary>
    /// Авторський квест поза конвеєром дня (R6): послідовність етапів із
    /// гілкуванням по 4 полосах перевірки (Поправка №3.7) або по вибору
    /// гравця. У Unity стане ScriptableObject; тут — чистий C#.
    ///
    /// ДАЛІ: <see cref="Validate"/> — та сама структурна чесність, що й у
    /// колишньому рушії (US-13.2/архів): кожен нетермінальний етап зобов'язаний
    /// вести на існуючий етап по КОЖНІЙ гілці — тупиків немає.
    /// </summary>
    public sealed class QuestDefinition
    {
        public string Id;
        /// <summary>Ключ репліки квестодавця, коли квест пропонується вперше.</summary>
        public string OfferTextKey;

        public readonly List<QuestStage> Stages = new List<QuestStage>();
        public int StartIndex;

        public QuestDefinition(string id, string offerTextKey)
        {
            Id = id;
            OfferTextKey = offerTextKey;
        }

        public QuestDefinition Stage(QuestStage stage)
        {
            if (stage != null) Stages.Add(stage);
            return this;
        }

        public QuestStage StageAt(int index) => index >= 0 && index < Stages.Count ? Stages[index] : null;

        /// <summary>
        /// Структурна чесність (як у колишньому рушії, US-13.2): у Check —
        /// усі 4 полоси ведуть на існуючий етап; у Choice — усі варіанти;
        /// Outcome — термінал, переходу не має.
        /// </summary>
        public bool Validate(out string error)
        {
            error = null;
            if (Stages.Count == 0) { error = "квест «" + Id + "»: немає етапів"; return false; }
            if (!Valid(StartIndex)) { error = "квест «" + Id + "»: StartIndex поза межами"; return false; }

            for (int i = 0; i < Stages.Count; i++)
            {
                var s = Stages[i];
                if (s == null) { error = "квест «" + Id + "»: порожній етап #" + i; return false; }

                switch (s.Kind)
                {
                    case QuestStageKind.Check:
                        if (s.NextByBand == null || s.NextByBand.Length != 4)
                        {
                            error = "перевірка «" + s.Id + "»: NextByBand мусить мати рівно 4 елементи";
                            return false;
                        }
                        for (int b = 0; b < 4; b++)
                            if (!Valid(s.NextByBand[b]))
                            {
                                error = "перевірка «" + s.Id + "»: полоса " + b + " веде в тупик";
                                return false;
                            }
                        break;

                    case QuestStageKind.Choice:
                        if (s.Options.Count == 0)
                        {
                            error = "вибір «" + s.Id + "»: без варіантів";
                            return false;
                        }
                        for (int o = 0; o < s.Options.Count; o++)
                            if (!Valid(s.Options[o].Next))
                            {
                                error = "вибір «" + s.Id + "»: варіант «" + s.Options[o].TextKey + "» веде в тупик";
                                return false;
                            }
                        break;

                    case QuestStageKind.Outcome:
                        // термінал — переходу немає, перевіряти нічого.
                        break;
                }
            }
            return true;
        }

        private bool Valid(int index) => index >= 0 && index < Stages.Count;
    }
}
