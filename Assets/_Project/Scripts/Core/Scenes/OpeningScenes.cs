using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Quests;

namespace Game.Core.Scenes
{
    /// <summary>
    /// Сцены открытия «Перевал» (docs/FIRST_HOUR.md §2.2).
    ///
    /// Текста здесь нет — только ключи: реплики живут в таблицах, и правит их
    /// автор без программиста. Сцена проверяется SceneValidator до всякого
    /// редактора.
    /// </summary>
    public static class OpeningScenes
    {
        /// <summary>Id вибору доби 1 (Поправка №7.8) — спільний з ботами (Core/Session/Bots).</summary>
        public const string TugarOfferChoiceId = "tugar_offer_choice";

        public const string CommunityFactionId = "community";
        public const string TuharBoyarsFactionId = "tuhar_boyars";

        /// <summary>Прапор: гравець виграв боярину час торгом — полегшує тихий шлях вузла 1 (GameSession.ApplyBargainedTimeBonusIfNeeded).</summary>
        public const string TugarBargainedTimeFlag = "tugar_bargained_time";

        /// <summary>
        /// Сутки 1, утро — «Сосед с претензией».
        ///
        /// Тугар Вовк предлагает пропустить авангард орды через перевал за долю.
        /// Смысл сцены — телеграфия: игрок узнаёт антагониста ДО боя, и тот же
        /// человек будет стоять при командире орды в финале. Без этой сцены
        /// предательство в финале — сюрприз, а не расплата.
        ///
        /// Поправка №7.8: вместо того чтобы Захар молча отказывал сам, ответ —
        /// выбор игрока (протагониста): отказать от имени общины, выторговать
        /// время у бояр (проверка Торговли, флаг смягчает узел 1) или спросить
        /// Мирославу, чего не договаривает отец (проверка Убеждения, ветвится
        /// на отдельную реплику). Все три сходятся в том же узле 1.
        /// </summary>
        public static Scene NeighbourWithADemand()
        {
            var refuseConsequence = new QuestConsequence()
                .Faction(CommunityFactionId, 10).Faction(TuharBoyarsFactionId, -10).Flag("tugar_offer_refused");

            var bargainBands = new[]
            {
                new QuestConsequence().Faction(TuharBoyarsFactionId, 5).Faction(CommunityFactionId, -15),
                new QuestConsequence().Faction(TuharBoyarsFactionId, 10).Faction(CommunityFactionId, -10),
                new QuestConsequence().Faction(TuharBoyarsFactionId, 15).Faction(CommunityFactionId, -5).Flag(TugarBargainedTimeFlag),
                new QuestConsequence().Faction(TuharBoyarsFactionId, 20).Flag(TugarBargainedTimeFlag)
            };

            var askMyroslavaBands = new[]
            {
                new QuestConsequence().Loyalty("myroslava", -5).Flag("myroslava_asked"),
                new QuestConsequence().Flag("myroslava_asked"),
                new QuestConsequence().Loyalty("myroslava", 5).Flag("myroslava_asked").Flag("myroslava_hint"),
                new QuestConsequence().Loyalty("myroslava", 10).Flag("myroslava_asked").Flag("myroslava_hint")
            };

            return new Scene("opening.neighbour", "scene.opening.neighbour.title")
                .Step(SceneStep.Shot(OpeningCast.TuharVovk().Id, ShotFraming.Close))
                .Step(SceneStep.Line("tuhar", "scene.neighbour.offer"))
                .Step(SceneStep.Shot("protagonist", ShotFraming.Close))
                .Step(SceneStep.Beat(1.5))
                .Step(SceneStep.Shot("tuhar", ShotFraming.Two, "protagonist"))
                .Step(SceneStep.Line("tuhar", "scene.neighbour.threat"))
                .Step(SceneStep.Shot("zakhar", ShotFraming.Close))
                .Step(SceneStep.Line("zakhar", "scene.neighbour.zakhar_yields_floor"))
                .Step(SceneStep.Shot("protagonist", ShotFraming.Close))
                .Step(SceneStep.Beat(1.0))
                .Step(SceneStep.Choice(TugarOfferChoiceId, new List<SceneChoiceOption>
                {
                    SceneChoiceOption.Simple("refuse", "scene.neighbour.option.refuse",
                        refuseConsequence, nextLabel: "zakhar_refuses"),
                    SceneChoiceOption.WithCheck("bargain", "scene.neighbour.option.bargain",
                        SkillKeys.Trade, 5, ApproachForm.Trade, bargainBands, nextLabel: "zakhar_refuses"),
                    SceneChoiceOption.WithCheck("ask_myroslava", "scene.neighbour.option.ask_myroslava",
                        SkillKeys.Persuade, 4, ApproachForm.Persuade, askMyroslavaBands, nextLabel: "myroslava_reveals")
                }))
                .Step(SceneStep.Shot("zakhar", ShotFraming.Close).WithLabel("zakhar_refuses"))
                .Step(SceneStep.Line("zakhar", "scene.neighbour.elder_refuses"))
                .Step(SceneStep.Effect("sfx.door.slam"))
                .Step(SceneStep.Transition("to.node1.pass"))
                .Step(SceneStep.Shot("myroslava", ShotFraming.Two, "protagonist").WithLabel("myroslava_reveals"))
                .Step(SceneStep.Line("myroslava", "scene.neighbour.myroslava_reveals"))
                .Step(SceneStep.Transition("to.node1.pass"));
        }

        /// <summary>
        /// Развязка узла 1. Четыре исхода, ни один не гейм-овер: сцена одна,
        /// ключ реплики разный — состав ростера на сутки 2–5 решается здесь
        /// (чеклист §3 стр. 19).
        /// </summary>
        public static Scene PassResolution(string outcomeKey)
        {
            return new Scene("opening.pass_resolution", "scene.pass.title")
                .Step(SceneStep.Shot("protagonist", ShotFraming.Close))
                .Step(SceneStep.Line("protagonist", "scene.pass." + outcomeKey))
                .Step(SceneStep.Shot(null, ShotFraming.Empty))
                .Step(SceneStep.Beat(2.0))
                .Step(SceneStep.Transition("to.settlement.evening"));
        }

        /// <summary>Карточка протагониста: у участника сцены обязана быть карточка.</summary>
        public static CharacterCard Protagonist() => new CharacterCard(
            "protagonist", "Протагонист", SourceTier.Original, null,
            "тот, кого община пустила на порог");
    }
}
