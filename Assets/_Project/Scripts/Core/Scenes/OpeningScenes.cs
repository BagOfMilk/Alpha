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

        /// <summary>
        /// «Ремесло фахівця» (Поправка №12.9, розширена №12.10 пулом
        /// прибульців): хто з іменного касту voices вибір і (де є пост —
        /// Склад/Лазарет/Майстерня/Ринок) стає на нього. Захар говорить
        /// про Сторожу, але сам він ВЖЕ на віче (council_seat) з першого
        /// ранку, незалежно від вибору — <see cref="Base.DefaultBuildings.Watch"/>
        /// поста не відкриває (GameSession.GrantBuildingFromConsequence не
        /// знаходить OpensSlotId і нікого не переставляє, «свій пост» кожного
        /// фахівця — окрема мапа в BotSupport.OwnPostOf, навмисно не виведена
        /// звідси).
        /// </summary>
        public static string FirstBuildingKeeperOf(string buildingId)
        {
            switch (buildingId)
            {
                case Base.DefaultBuildings.Watch: return "zakhar";
                case Base.DefaultBuildings.Storehouse: return Session.ArrivalsPool.KeeperId;
                case Base.DefaultBuildings.Infirmary: return Session.ArrivalsPool.HealerId;
                case Base.DefaultBuildings.Workshop: return Session.ArrivalsPool.GobanId;
                case Base.DefaultBuildings.Market: return Session.ArrivalsPool.SindbadId;
                default: return null;
            }
        }

        /// <summary>
        /// Поправка №12.9, розширена №12.10 (рішення власника 29.09.2026):
        /// варіанти вибору першої будівлі — це ремесла фахівців, ПРИСУТНІХ у
        /// пролозі: Сторожа завжди (Захар присутній завжди) і ремесла рівно
        /// ДВОХ прибульців з пулу (<see cref="Session.ArrivalsPool"/>).
        /// <paramref name="isPresent"/> — предикат «companionId присутній»;
        /// <c>null</c> — увесь каталог (усі п'ять), як у сценарії без
        /// фільтрації (тести чистої функції). Порядок каталогу
        /// (<see cref="Base.DefaultBuildings.FirstBuildingChoices"/>)
        /// зберігається — Лазарет, коли присутній, лишається останнім.
        /// </summary>
        public static IReadOnlyList<string> AvailableFirstBuildingChoices(System.Func<string, bool> isPresent = null)
        {
            var result = new List<string>();
            foreach (var buildingId in Base.DefaultBuildings.FirstBuildingChoices)
            {
                string keeper = FirstBuildingKeeperOf(buildingId);
                if (isPresent != null && keeper != null && !isPresent(keeper)) continue;
                result.Add(buildingId);
            }
            return result;
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
        ///
        /// Поправка №12.10 (пул прибульців): варіанти вибору першої будівлі
        /// залежать від того, ХТО прибився до гурту (<see cref="Session.ArrivalsPool"/>)
        /// — а це відомо лише посеред сцени (передісторія — вже, відповідь
        /// Тугарові — ще ні). Замість лінивого обчислення кроку в момент
        /// показу (модель сцени — фіксований список кроків, без цього гачка)
        /// тут рахуються ТРИ гілки наперед, по одній на кожну відповідь
        /// Тугарові (<paramref name="backgroundId"/> уже відомий на момент
        /// побудови сцени — <c>GameSession.BeginOpeningScene</c> викликає
        /// це ПІСЛЯ підтвердження створення), кожна зі своїм набором
        /// прибульців і своєю унікальною міткою (branchTag) — чистий спосіб,
        /// що вже підтримують SceneStep.Goto/WithLabel.
        /// </summary>
        public static Scene NeighbourWithADemand(string backgroundId = null)
        {
            var refuseArrivals = Session.ArrivalsPool.Determine(backgroundId, "refuse");
            var bargainArrivals = Session.ArrivalsPool.Determine(backgroundId, "bargain");
            var askMyroslavaArrivals = Session.ArrivalsPool.Determine(backgroundId, "ask_myroslava");

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
                        refuseConsequence, nextLabel: "zakhar_refuses_refuse"),
                    SceneChoiceOption.WithCheck("bargain", "scene.neighbour.option.bargain",
                        SkillKeys.Trade, 5, ApproachForm.Trade, bargainBands, nextLabel: "zakhar_refuses_bargain"),
                    SceneChoiceOption.WithCheck("ask_myroslava", "scene.neighbour.option.ask_myroslava",
                        SkillKeys.Persuade, 4, ApproachForm.Persuade, askMyroslavaBands, nextLabel: "myroslava_reveals")
                }))
                .Step(SceneStep.Shot("zakhar", ShotFraming.Close).WithLabel("zakhar_refuses_refuse"))
                .Step(SceneStep.Line("zakhar", "scene.neighbour.elder_refuses"))
                .Step(SceneStep.Effect("sfx.door.slam"))
                .StepAll(ArrivalAnnouncementSteps(refuseArrivals))
                .Step(SceneStep.Goto("first_building_refuse"))
                .Step(SceneStep.Shot("zakhar", ShotFraming.Close).WithLabel("zakhar_refuses_bargain"))
                .Step(SceneStep.Line("zakhar", "scene.neighbour.elder_refuses"))
                .Step(SceneStep.Effect("sfx.door.slam"))
                .StepAll(ArrivalAnnouncementSteps(bargainArrivals))
                .Step(SceneStep.Goto("first_building_bargain"))
                .Step(SceneStep.Shot("myroslava", ShotFraming.Two, "protagonist").WithLabel("myroslava_reveals"))
                .Step(SceneStep.Line("myroslava", "scene.neighbour.myroslava_reveals"))
                .StepAll(ArrivalAnnouncementSteps(askMyroslavaArrivals))
                .Step(SceneStep.Goto("first_building_ask_myroslava"))
                .StepAll(FirstBuildingSteps(refuseArrivals, "refuse"))
                .StepAll(FirstBuildingSteps(bargainArrivals, "bargain"))
                .StepAll(FirstBuildingSteps(askMyroslavaArrivals, "ask_myroslava"));
        }

        /// <summary>
        /// Поправка №12.10: хто прибився до гурту ГГ (передісторія і відповідь
        /// Тугарові — двоє РІЗНИХ, <see cref="Session.ArrivalsPool"/>) —
        /// голос за кадром (порожній план, Статут UI-02): жодних нових Shot
        /// на Гобана-Сайра/Синдбада — портретів для них поки немає, а
        /// порожній план дозволяє говорити будь-кому з картою (перевірено
        /// SceneValidator: говорити може лише той, хто в кадрі, ЯКЩО кадр
        /// не порожній). Окремий рядок на кожного прибульця (не аргумент у
        /// Line — його SceneStep не підтримує), ключ —
        /// "scene.neighbour.arrival.&lt;id&gt;".
        /// </summary>
        private static IEnumerable<SceneStep> ArrivalAnnouncementSteps(Session.ArrivalsPool.Arrivals arrivals)
        {
            yield return SceneStep.Shot(null, ShotFraming.Empty);
            yield return SceneStep.Line(arrivals.FromBackground, "scene.neighbour.arrival." + arrivals.FromBackground);
            yield return SceneStep.Line(arrivals.FromTugar, "scene.neighbour.arrival." + arrivals.FromTugar);
        }

        /// <summary>
        /// Поправка №12.7, переглянута №12.9, переглянута №12.10 (рішення
        /// власника 29.09.2026): гра стартує без будівель, рада вже гуде на
        /// майдані просто неба (віче, FirstHourWorld.Build), і одразу після
        /// розмови з Тугаром громада зводить першу будову — ту, яку обере
        /// гравець, за ремеслом присутніх фахівців
        /// (<see cref="AvailableFirstBuildingChoices"/> — Сторожа завжди
        /// плюс ремесла двох прибульців <paramref name="arrivals"/>).
        /// Кожен варіант каже в самому тексті, що відкриває і чого бракуватиме
        /// (Статут MECH-05, UI-02); будівля стає одразу і без ціни, на її пост
        /// (де він є) стає свій іменний. <paramref name="branchTag"/> —
        /// унікальна частина міток цієї гілки (три гілки Тугара мають різні
        /// набори прибульців і не можуть ділити мітки). Без нових кадрів
        /// Shot на Гобана-Сайра/Синдбада — портретів для них поки немає:
        /// їхню репліку виголошує Захар (той самий кадр, що вже є для нього).
        /// </summary>
        private static IEnumerable<SceneStep> FirstBuildingSteps(Session.ArrivalsPool.Arrivals arrivals, string branchTag)
        {
            string entryLabel = "first_building_" + branchTag;
            yield return SceneStep.Shot("zakhar", ShotFraming.Two, "protagonist").WithLabel(entryLabel);
            yield return SceneStep.Line("zakhar", "scene.neighbour.first_building.prompt");

            var choices = AvailableFirstBuildingChoices(id => id == "zakhar" || arrivals.Contains(id));

            var options = new List<SceneChoiceOption>();
            foreach (var buildingId in choices)
                options.Add(SceneChoiceOption.Simple(buildingId, "scene.neighbour.option." + buildingId,
                    new QuestConsequence().Building(buildingId).Flag(FirstBuildingFlagPrefix + buildingId),
                    nextLabel: entryLabel + "_" + buildingId));
            yield return SceneStep.Choice(FirstBuildingChoiceId, options);

            foreach (var buildingId in choices)
            {
                string keeper = FirstBuildingKeeperOf(buildingId);
                // Портрет є лише для Захара/Діда Овсія/Гафії (уже в сцені
                // раніше); Гобана-Сайра й Синдбада озвучує Захар за кадром
                // без Shot на них.
                bool hasPortrait = keeper == "zakhar" || keeper == Session.ArrivalsPool.KeeperId ||
                                    keeper == Session.ArrivalsPool.HealerId;
                string speaker = hasPortrait ? keeper : "zakhar";
                yield return SceneStep.Shot(speaker, ShotFraming.Close).WithLabel(entryLabel + "_" + buildingId);
                yield return SceneStep.Line(speaker, "scene.neighbour.first_building." + buildingId);
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
