using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Quests;

namespace Game.Core.Scenes
{
    /// <summary>
    /// Сцени відкриття «Перевал» (docs/FIRST_HOUR.md §2.2).
    ///
    /// Тексту тут нема — лише ключі: репліки живуть у таблицях, і править їх
    /// автор без програміста. Сцена перевіряється SceneValidator до будь-якого
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

        /// <summary>Id вибору першої будівлі (Поправка №12.7) — спільний з ботами, автотуром і журналом.</summary>
        public const string FirstBuildingChoiceId = "first_building_choice";

        /// <summary>Префікс прапора обраної першої будівлі: «first_building.&lt;buildingId&gt;» (у зліпку разом зі StoryFlags).</summary>
        public const string FirstBuildingFlagPrefix = "first_building.";

        /// <summary>Хто з іменного касту стає на пост першої будівлі (картка — Поправка №5.2).</summary>
        public static string FirstBuildingKeeperOf(string buildingId)
        {
            switch (buildingId)
            {
                case Base.DefaultBuildings.CouncilHall: return "zakhar";
                case Base.DefaultBuildings.Storehouse: return "keeper";
                case Base.DefaultBuildings.Infirmary: return "healer";
                default: return null;
            }
        }

        /// <summary>
        /// Доба 1, ранок — «Сусід з претензією».
        ///
        /// Тугар Вовк пропонує пропустити авангард орди через перевал за частку.
        /// Сенс сцени — телеграфія: гравець дізнається антагоніста ДО бою, і та сама
        /// людина стоятиме при командирі орди у фіналі. Без цієї сцени
        /// зрада у фіналі — сюрприз, а не розплата.
        ///
        /// Поправка №7.8: замість того щоб Захар мовчки відмовляв сам, відповідь —
        /// вибір гравця (протагоніста): відмовити від імені громади, виторгувати
        /// час у бояр (перевірка Торгівлі, прапор пом'якшує вузол 1) або спитати
        /// Мирославу, чого не договорює батько (перевірка Переконання, розгалужується
        /// на окрему репліку). Усі три сходяться в тому самому вузлі 1.
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
                .Step(SceneStep.Goto("first_building"))
                .Step(SceneStep.Shot("myroslava", ShotFraming.Two, "protagonist").WithLabel("myroslava_reveals"))
                .Step(SceneStep.Line("myroslava", "scene.neighbour.myroslava_reveals"))
                .Step(SceneStep.Goto("first_building"))
                .StepAll(FirstBuildingSteps());
        }

        /// <summary>
        /// Поправка №12.7 (рішення власника 29.09.2026: «Так придовити, перша
        /// будівля зьявляється як вибор після прологу»): гра стартує без
        /// будівель, і одразу після розмови з Тугаром громада зводить першу —
        /// ту, яку обере гравець. Три варіанти (<see cref="Base.DefaultBuildings.FirstBuildingChoices"/>),
        /// кожен каже в самому тексті, що відкриває і чого бракуватиме
        /// (Статут MECH-05, UI-02); будівля стає одразу і без ціни, на її пост
        /// стає свій іменний (Захар/Дід Овсій/Гафія). Обидві гілки розмови
        /// сходяться сюди стрибком (<see cref="SceneStep.Goto"/>).
        /// </summary>
        private static IEnumerable<SceneStep> FirstBuildingSteps()
        {
            yield return SceneStep.Shot("zakhar", ShotFraming.Two, "protagonist").WithLabel("first_building");
            yield return SceneStep.Line("zakhar", "scene.neighbour.first_building.prompt");

            var options = new List<SceneChoiceOption>();
            foreach (var buildingId in Base.DefaultBuildings.FirstBuildingChoices)
                options.Add(SceneChoiceOption.Simple(buildingId, "scene.neighbour.option." + buildingId,
                    new QuestConsequence().Building(buildingId).Flag(FirstBuildingFlagPrefix + buildingId),
                    nextLabel: "first_building." + buildingId));
            yield return SceneStep.Choice(FirstBuildingChoiceId, options);

            foreach (var buildingId in Base.DefaultBuildings.FirstBuildingChoices)
            {
                string keeper = FirstBuildingKeeperOf(buildingId);
                yield return SceneStep.Shot(keeper, ShotFraming.Close).WithLabel("first_building." + buildingId);
                yield return SceneStep.Line(keeper, "scene.neighbour.first_building." + buildingId);
                yield return SceneStep.Transition("to.node1.pass");
            }
        }

        /// <summary>
        /// Розв'язка вузла 1. Чотири результати, жоден не гейм-овер: сцена одна,
        /// ключ репліки різний — склад ростера на добу 2–5 вирішується тут
        /// (чеклист §3 стор. 19).
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

        /// <summary>
        /// Ім'я-заглушка протагоніста, поки гравець не ввів своє (створення
        /// пропущено). Інтерфейс показує замість нього «Провідник»/«Провідниця»
        /// за родом (ScreenText.ResolveCompanionName).
        /// </summary>
        public const string ProtagonistPlaceholderName = "Протагоніст";

        /// <summary>Картка протагоніста: у учасника сцени зобов'язана бути картка.</summary>
        public static CharacterCard Protagonist() => new CharacterCard(
            "protagonist", ProtagonistPlaceholderName, SourceTier.Original, null,
            "тот, кого община пустила на порог");
    }
}
