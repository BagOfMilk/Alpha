using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Characters.Build;
using Game.Core.Characters.Creation;
using Game.Core.Checks;
using Game.Core.Items;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Core.Stats;
using Game.Gameplay.Text;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Чистий C# (без <c>UnityEngine</c>) — форматування тексту екранів і
    /// перевірка легальності кнопок, винесені з *Screen.cs, щоб мати
    /// headless-EditMode тести (docs/TEST_BUILD.md, пакет E1b: "pure-C#
    /// screen-model helpers... з тестами"). *Screen.cs лише малює те, що тут
    /// пораховано — жодної логіки формату в самому IMGUI-шарі.
    ///
    /// Підключено прямим &lt;Compile Include&gt; у
    /// tools/Game.Tests.Headless/Game.Tests.EditMode.csproj (той самий
    /// прийом, що вже є для VillageView.cs/UkrainianText.cs).
    /// </summary>
    public static class ScreenText
    {
        // ===================== легальність кнопок =====================

        /// <summary>Чи натискати кнопку, і якщо ні — ключ причини (Widgets.DisabledButton показує його словами).</summary>
        public readonly struct Legality
        {
            public readonly bool Enabled;
            public readonly string ReasonKey;

            /// <summary>
            /// Id підмета причини (той, хто "Загинув"/"недоступний"), коли
            /// відомий — фікс-ревью (major, раунд 2): без нього
            /// ReasonText брав рід ГЛЯДАЧА для чужого стану, і "ui.reason.*"
            /// показувались із неперекритою чоловічою заглушкою "(-ла)"/
            /// "(-на)" замість дібраної форми (Мирослава читала "недоступний
            /// (-на)" замість "недоступна").
            /// </summary>
            public readonly string SubjectId;

            public Legality(bool enabled, string reasonKey, string subjectId = null)
            {
                Enabled = enabled;
                ReasonKey = reasonKey;
                SubjectId = subjectId;
            }

            public static readonly Legality Ok = new Legality(true, null);
        }

        /// <summary>
        /// Текст причини недоступності — рід підмета (<see cref="Legality.SubjectId"/>),
        /// не глядача, тим самим <see cref="SubjectGender"/>, що вже коректно
        /// працює в EventLine/CompanionStatusLabel.
        /// </summary>
        public static string ReasonText(Legality legality, Gender viewerGender)
        {
            if (string.IsNullOrEmpty(legality.ReasonKey)) return string.Empty;
            return UkrainianText.Get(legality.ReasonKey, SubjectGender(legality.SubjectId, viewerGender));
        }

        /// <summary>
        /// Видима (без прихованих чисел) частина легальності кандидата на пост
        /// чи в відряд: SlotLocked/SlotOccupied дізнаємось лише зі спроби
        /// (AssignmentResult) — тут лише те, що видно в CompanionSummary.Status
        /// заздалегідь, той самий аудит CompanionStatus.Antagonist, що й у ядрі
        /// (§4.5 TEST_BUILD.md).
        /// </summary>
        public static Legality AssignCandidateLegality(CompanionSummary companion)
        {
            if (companion == null) return new Legality(false, "ui.reason.unknown_companion");
            switch (companion.Status)
            {
                case CompanionStatus.Dead: return new Legality(false, "ui.reason.dead", companion.Id);
                case CompanionStatus.OnMission: return new Legality(false, "ui.reason.on_mission", companion.Id);
                case CompanionStatus.Antagonist: return new Legality(false, "ui.reason.antagonist", companion.Id);
                default: return Legality.Ok;
            }
        }

        /// <summary>
        /// Видима частина DispatchResult-перевірки (порожньо/дублі/недоступний)
        /// ДО спроби DepartExpedition — найдешевші відмови ловляться тут, решта
        /// (NoSuchSite/PartyTooLarge/PartyAlreadyAway) видно лише зі спроби.
        /// </summary>
        public static Legality ExpeditionPartyLegality(IReadOnlyList<string> companionIds, RosterView roster)
        {
            if (companionIds == null || companionIds.Count == 0)
                return new Legality(false, "ui.reason.empty_party");

            var seen = new HashSet<string>();
            for (int i = 0; i < companionIds.Count; i++)
            {
                if (!seen.Add(companionIds[i]))
                    return new Legality(false, "ui.reason.duplicate_companion");
            }

            for (int i = 0; i < companionIds.Count; i++)
            {
                var legality = AssignCandidateLegality(FindCompanion(roster, companionIds[i]));
                if (!legality.Enabled) return legality;
            }

            return Legality.Ok;
        }

        public static CompanionSummary FindCompanion(RosterView roster, string companionId)
        {
            if (roster?.Companions == null || string.IsNullOrEmpty(companionId)) return null;
            for (int i = 0; i < roster.Companions.Count; i++)
            {
                var c = roster.Companions[i];
                if (c != null && c.Id == companionId) return c;
            }
            return null;
        }

        // ===================== рішення (Decision/Quest/Crisis/Finale) =====================

        /// <summary>Один рядок варіанту рішення — поріг видно заздалегідь (інваріант 8), як і показник кандидата.</summary>
        public static string DecisionOptionLine(DecisionOptionView option, Gender gender)
        {
            if (option == null) return string.Empty;

            string path = option.Path == IncidentPathView.Bloody
                ? UkrainianText.Get("ui.decision.path.bloody", gender)
                : UkrainianText.Get("ui.decision.path.quiet", gender);

            // Полірування (ціль 6 «Рішення», owner: "the option text says so
            // (тактичний бій: N ворогів), not just a skill threshold"): такий
            // вузол не має порогу навички взагалі (перевірка не
            // викликається — кроваво завжди бій), тож звичайний шаблон
            // "{skill} ≥ {threshold}" тут би збрехав про механіку.
            if (option.TacticalBattleEnemyCount > 0)
                return UkrainianText.Format("ui.decision.option_line.battle", gender,
                    "path", path, "count", option.TacticalBattleEnemyCount.ToString(),
                    "enemies", EnemiesCount(option.TacticalBattleEnemyCount));

            string skill = SkillLabel(option.SkillKey, gender);
            string candidate = option.HasCandidate
                ? UkrainianText.Format("ui.decision.candidate", gender, "name", ResolveCompanionName(option.BestActorId, gender, null))
                : UkrainianText.Get("ui.decision.no_candidate", gender);
            string band = string.IsNullOrEmpty(option.ExpectedBand) ? "" : BandWordsFor(option.ExpectedBand, gender);

            return UkrainianText.Format("ui.decision.option_line", gender,
                "path", path, "skill", skill, "threshold", option.Threshold.ToString(),
                "candidate", candidate, "band", band);
        }

        /// <summary>
        /// Один рядок варіанту сценового вибору (Поправка №7.8, тест-збірка,
        /// п.1): сам текст варіанту (TextKey) — завжди; якщо варіант несе
        /// перевірку (SkillKey не порожній) — дописуємо скіл/поріг/виконавця/
        /// очікувану полосу заздалегідь (інваріант 8), тим самим шаблоном
        /// прозорості, що вже <see cref="DecisionOptionLine"/> для рішень.
        /// </summary>
        public static string SceneOptionLine(DecisionOptionView option, Gender gender, RosterView roster)
        {
            if (option == null) return string.Empty;
            string text = UkrainianText.Has(option.TextKey, gender) ? UkrainianText.Get(option.TextKey, gender) : option.TextKey;
            if (string.IsNullOrEmpty(option.SkillKey)) return text;

            string skill = SkillLabel(option.SkillKey, gender);
            string performer = option.HasCandidate
                ? ResolveCompanionName(option.BestActorId, gender, roster)
                : UkrainianText.Get("ui.decision.no_candidate", gender);
            string band = string.IsNullOrEmpty(option.ExpectedBand) ? "" : BandWordsFor(option.ExpectedBand, gender);

            return UkrainianText.Format("ui.scene.option_check_line", gender,
                "text", text, "skill", skill, "threshold", option.Threshold.ToString(),
                "performer", performer, "band", band);
        }

        /// <summary>
        /// Назва квесту: "quest.&lt;id&gt;" (Гафія — id без префікса), або сам id,
        /// якщо він уже ключ (глава арки Максима — "quest.maksym.ch1").
        /// </summary>
        public static string QuestName(string questId, Gender gender)
        {
            if (string.IsNullOrEmpty(questId)) return string.Empty;
            string prefixed = "quest." + questId;
            if (UkrainianText.Has(prefixed, gender)) return UkrainianText.Get(prefixed, gender);
            if (UkrainianText.Has(questId, gender)) return UkrainianText.Get(questId, gender);
            return questId;
        }

        /// <summary>Рядок етапу-перевірки квесту — навичка і поріг заздалегідь (інваріант 8); порожньо для етапу-вибору.</summary>
        public static string QuestCheckLine(QuestOfferView offer, Gender gender)
        {
            if (offer == null || string.IsNullOrEmpty(offer.CheckSkillKey)) return string.Empty;
            return UkrainianText.Format("ui.quest.check.line", gender,
                "skill", SkillLabel(offer.CheckSkillKey, gender), "threshold", offer.CheckThreshold.ToString());
        }

        /// <summary>Підписи вкладок хаба за індексом: кнопки вкладок і підказка «E — зайти» на прогулянці.</summary>
        public static readonly string[] HubTabKeys =
        {
            "ui.tab.posts", "ui.tab.buildings", "ui.tab.council", "ui.tab.expedition",
            "ui.tab.gear", "ui.tab.people", "ui.tab.quests", "ui.tab.factions",
            "ui.tab.readiness", "ui.tab.save", "ui.tab.journal"
        };

        public static string HubTabKey(int tab) => tab >= 0 && tab < HubTabKeys.Length ? HubTabKeys[tab] : HubTabKeys[0];

        /// <summary>«1 ворог», «2 вороги», «5 ворогів», «21 ворог» — українська форма числа.</summary>
        public static string EnemiesCount(int n)
        {
            int n10 = n % 10, n100 = n % 100;
            string word = n10 == 1 && n100 != 11 ? "ворог"
                : n10 >= 2 && n10 <= 4 && (n100 < 12 || n100 > 14) ? "вороги"
                : "ворогів";
            return n + " " + word;
        }

        public static string SkillLabel(string skillKey, Gender gender)
        {
            if (string.IsNullOrEmpty(skillKey)) return string.Empty;
            string key = "skill." + skillKey.ToLowerInvariant();
            return UkrainianText.Has(key, gender) ? UkrainianText.Get(key, gender) : skillKey;
        }

        public static string BandLabel(OutcomeBand band, Gender gender)
            => UkrainianText.Get("band." + band.ToString().ToLowerInvariant(), gender);

        /// <summary>SessionView.TensionBand ("Calm".."Fracture") — коротке слово-чіп для верхньої панелі, без жодного числа (інваріант 3).</summary>
        public static string MoodChip(string rawTensionBand, Gender gender)
        {
            if (string.IsNullOrEmpty(rawTensionBand)) return "";
            string key = "ui.mood." + rawTensionBand.ToLowerInvariant();
            return UkrainianText.Has(key, gender) ? UkrainianText.Get(key, gender) : rawTensionBand;
        }

        /// <summary>SessionView.CrowdBand ("Hamlet".."City") — той самий тір, що й Tier, словом.</summary>
        public static string CrowdChip(string rawCrowdBand, Gender gender)
        {
            if (string.IsNullOrEmpty(rawCrowdBand)) return "";
            string key = "ui.crowd." + rawCrowdBand.ToLowerInvariant();
            return UkrainianText.Has(key, gender) ? UkrainianText.Get(key, gender) : rawCrowdBand;
        }

        /// <summary>FactionSummary.Band ("Hostile".."Allied") — GameSession віддає сирий <c>ToString()</c> enum, R7 вимагає перекладу за ключем, не сирого <c>DisplayName</c>.</summary>
        public static string FactionBandLabel(string rawBand, Gender gender)
        {
            if (string.IsNullOrEmpty(rawBand)) return "";
            string key = "faction.band." + rawBand.ToLowerInvariant();
            return UkrainianText.Has(key, gender) ? UkrainianText.Get(key, gender) : rawBand;
        }

        /// <summary>DungeonView.ThreatBand ("Calm".."Deadly").</summary>
        public static string ThreatChip(string rawThreatBand, Gender gender)
        {
            if (string.IsNullOrEmpty(rawThreatBand)) return "";
            string key = "ui.threat." + rawThreatBand.ToLowerInvariant();
            return UkrainianText.Has(key, gender) ? UkrainianText.Get(key, gender) : rawThreatBand;
        }

        /// <summary>ReadinessView.Band ("Unprepared".."Fortified").</summary>
        public static string ReadinessLabel(string rawReadinessBand, Gender gender)
        {
            if (string.IsNullOrEmpty(rawReadinessBand)) return "";
            string key = "readiness.band." + rawReadinessBand.ToLowerInvariant();
            return UkrainianText.Has(key, gender) ? UkrainianText.Get(key, gender) : rawReadinessBand;
        }

        /// <summary>Будь-яка сира полоса-рядок (ExpeditionPreviewView.ExpectedBand тощо) — публічна обгортка над <see cref="BandWordsFor"/> для екранів поза цим файлом.</summary>
        public static string BandLabelFromRaw(string rawBand, Gender gender) => BandWordsFor(rawBand, gender);

        // ===================== довідкові підписи роcтера =====================

        /// <summary>
        /// Фікс-ревью (Фаза F, знайдено тур-автоплеєм): "status.dead"/
        /// "status.injured" мають варіанти .f/.m (на відміну від
        /// loyalty.band.*, це вже гендерно-нейтральні прикметники) — стара
        /// перевантаженість без companionId завжди брала рід ГЛЯДАЧА
        /// (SummaryScreen/HubScreen.DrawPeople передавали ProtagonistGender),
        /// тож підсумок писав "Максим Беркут ... Загинула" (жіноча форма)
        /// щойно гравець обирав жіночий рід для протагоніста — незалежно від
        /// того, хто насправді загинув. Той самий SubjectGender, що вже
        /// коректно працює в EventLine.
        /// </summary>
        public static string CompanionStatusLabel(string companionId, CompanionStatus status, Gender viewerGender)
            => CompanionStatusLabel(status, SubjectGender(companionId, viewerGender));

        public static string CompanionStatusLabel(CompanionStatus status, Gender gender)
        {
            switch (status)
            {
                case CompanionStatus.Idle: return UkrainianText.Get("status.idle", gender);
                case CompanionStatus.Assigned: return UkrainianText.Get("status.assigned", gender);
                case CompanionStatus.OnMission: return UkrainianText.Get("status.on_mission", gender);
                case CompanionStatus.Injured: return UkrainianText.Get("status.injured", gender);
                case CompanionStatus.Resting: return UkrainianText.Get("status.resting", gender);
                case CompanionStatus.Dead: return UkrainianText.Get("status.dead", gender);
                case CompanionStatus.Antagonist: return UkrainianText.Get("status.antagonist", gender);
                default: return status.ToString();
            }
        }

        public static string LoyaltyLabel(LoyaltyBand? band, Gender gender)
        {
            if (band == null) return UkrainianText.Get("ui.common.none", gender);
            return UkrainianText.Get("loyalty.band." + band.Value.ToString().ToLowerInvariant(), gender);
        }

        /// <summary>
        /// Фікс-ревью (Поправка №7.8, п.1, знайдено тур-автоплеєм на Choice-
        /// екрані): протагоніст — ІМ'Я ГРАВЦЯ (роСтер, SetProtagonistName),
        /// не лорова константа. Загальний пошук "char."+id нижче коректний
        /// для іменного складу (Мирослава/Захар/Тугар — там ім'я справді
        /// незмінний лорсько-текстовий факт), але "char.protagonist.m"/".f"
        /// — це лише ЗАГЛУШКИ-ПІДКАЗКИ (той самий текст, що
        /// "ui.creation.name.default.*" на екрані створення, ДО того, як
        /// гравець щось увів) — без цього винятку вони підміняли б справжнє
        /// обране ім'я ("Оксана") генеричним "Провідниця" на КОЖНОМУ
        /// портреті й підписі мовця сцени, включно з новим Choice-екраном.
        /// </summary>
        public static string ResolveCompanionName(string companionId, Gender gender, RosterView roster)
        {
            if (string.IsNullOrEmpty(companionId)) return string.Empty;

            if (companionId == GameSession.ProtagonistId)
            {
                // Власне ім'я гравця — так; заглушка ядра (створення пропущено) —
                // ні: тоді «Провідник»/«Провідниця» за родом із char.protagonist.
                var protagonist = FindCompanion(roster, companionId);
                if (protagonist != null && !string.IsNullOrEmpty(protagonist.DisplayName) &&
                    protagonist.DisplayName != Game.Core.Scenes.OpeningScenes.ProtagonistPlaceholderName)
                    return protagonist.DisplayName;
            }

            string charKey = "char." + companionId;
            if (UkrainianText.Has(charKey, gender)) return UkrainianText.Get(charKey, gender);

            var summary = FindCompanion(roster, companionId);
            if (summary != null && !string.IsNullOrEmpty(summary.DisplayName)) return summary.DisplayName;
            return companionId;
        }

        // ===================== збереження =====================

        /// <summary>
        /// Той самий сентинел, що <c>Game.Gameplay.SaveFileStore.AutosaveSlot</c>
        /// (не звертаємось до нього напряму — SaveFileStore.cs не підключений
        /// до tools/Alpha.Play, куди цей файл теж іде прямим Compile Include).
        /// </summary>
        private const int AutosaveSlotSentinel = -1;

        /// <summary>
        /// Фікс-ревью (Фаза F, знайдено тур-автоплеєм): SaveFileStore.ListHeaders
        /// завжди додає слот автозбереження (AutosaveSlot = -1) до звичайних
        /// іменованих слотів — без цієї підстановки гравець бачив би
        /// "Слот -1: порожньо" замість людського підпису.
        /// </summary>
        public static string SaveSlotLine(int slot, bool occupied, string headline, int day, Gender gender)
        {
            string slotLabel = slot == AutosaveSlotSentinel
                ? UkrainianText.Get("ui.save.slot.auto_label", gender)
                : slot.ToString();
            return occupied
                ? UkrainianText.Format("ui.save.slot", gender, "slot", slotLabel, "headline", headline ?? "", "day", day.ToString())
                : UkrainianText.Format("ui.save.slot.empty", gender, "slot", slotLabel);
        }

        // ===================== фідбек результатів команд =====================

        public static string AssignResultText(AssignmentResult r, Gender g)
        {
            switch (r)
            {
                case AssignmentResult.Success: return UkrainianText.Get("ui.feedback.assign.success", g);
                case AssignmentResult.SlotNotFound: return UkrainianText.Get("ui.feedback.assign.slot_not_found", g);
                case AssignmentResult.SlotLocked: return UkrainianText.Get("ui.feedback.assign.slot_locked", g);
                case AssignmentResult.SlotOccupied: return UkrainianText.Get("ui.feedback.assign.slot_occupied", g);
                case AssignmentResult.CompanionNotFound: return UkrainianText.Get("ui.feedback.assign.companion_not_found", g);
                default: return UkrainianText.Get("ui.feedback.assign.companion_unavailable", g);
            }
        }

        /// <summary>
        /// Полірування (ціль 2 «Прозорість дій»): «золото/матеріали, N діб» —
        /// той самий рядок, що і в OrderBuilding-фідбеку, але ДО кліку, поруч
        /// із назвою будівлі, а не лише постфактум у LastMessage.
        /// </summary>
        /// <summary>
        /// Тест-збірка (Поправка №7.8, п.3): <paramref name="testBuildOneDayConstruction"/>
        /// (<c>CityView.TestBuildOneDayConstruction</c>) підмінює проєктний
        /// <c>def.Days</c> ЕФЕКТИВНИМ терміном (1 доба) — картка показує те
        /// число, яке справді діє в цьому режимі, а не те, яке ніколи не
        /// спрацює (R17: жодного невірного/прихованого числа).
        /// </summary>
        public static string BuildingCostLine(Game.Core.Base.BuildingDefinition def, Gender g, bool testBuildOneDayConstruction = false)
        {
            if (def == null) return string.Empty;
            string cost = def.MaterialsCost > 0
                ? UkrainianText.Format("ui.buildings.cost_both", g,
                    "gold", def.GoldCost.ToString(), "materials", def.MaterialsCost.ToString())
                : UkrainianText.Format("ui.buildings.cost_gold", g, "gold", def.GoldCost.ToString());
            int days = testBuildOneDayConstruction ? 1 : def.Days;
            return cost + ", " + UkrainianText.Format("ui.buildings.days", g, "days", days.ToString());
        }

        /// <summary>
        /// Коротка назва статy для рядка "Покращує: ..." — skill./attr. для
        /// тих осей (той самий текст, що й картка персонажа), "ui.stat.&lt;x&gt;"
        /// для похідних (Армія/Точність/...). Ніколи не сире ім'я enum'а.
        /// </summary>
        public static string StatKeyLabel(StatKey key, Gender g)
        {
            if (StatKeys.TryToAttribute(key, out var a)) return UkrainianText.Get("attr." + a.ToString().ToLowerInvariant(), g);
            if (StatKeys.TryToSkill(key, out var s)) return UkrainianText.Get("skill." + Game.Core.Stats.Skills.KeyId(s), g);
            string shortKey = "ui.stat." + key.ToString().ToLowerInvariant();
            return UkrainianText.Has(shortKey, g) ? UkrainianText.Get(shortKey, g) : key.ToString();
        }

        /// <summary>Рядок "Броня +1, Живучість +1" — усі статMods предмета, той самий підпис для "Покращує:" на схованці.</summary>
        public static string ItemStatSummary(ItemInstance item, Gender g)
        {
            if (item == null) return string.Empty;
            var parts = new List<string>();
            foreach (var m in item.StatMods)
            {
                string sign = m.Value >= 0 ? "+" : "";
                parts.Add(StatKeyLabel(m.Key, g) + " " + sign + m.Value.ToString("0.#"));
            }
            return parts.Count == 0 ? UkrainianText.Get("ui.sheet.none", g) : string.Join(", ", parts);
        }

        /// <summary>Рядок "Броня 1→2, Живучість 1→2" — CraftSystem.PreviewUpgrade без мутації предмета.</summary>
        public static string CraftPreviewText(IReadOnlyList<Game.Core.Items.StatPreviewLine> preview, Gender g)
        {
            if (preview == null || preview.Count == 0) return string.Empty;
            var parts = new List<string>();
            foreach (var line in preview)
                parts.Add(UkrainianText.Format("ui.gear.craft_preview", g,
                    "stat", StatKeyLabel(line.Key, g), "before", line.Before.ToString(), "after", line.After.ToString()));
            return string.Join(", ", parts);
        }

        public static string BuildResultText(BuildOrderResult r, Gender g)
        {
            switch (r)
            {
                case BuildOrderResult.Started: return UkrainianText.Get("ui.feedback.build.started", g);
                case BuildOrderResult.UnknownBuilding: return UkrainianText.Get("ui.feedback.build.unknown_building", g);
                case BuildOrderResult.AlreadyBuilt: return UkrainianText.Get("ui.feedback.build.already_built", g);
                case BuildOrderResult.AlreadyInProgress: return UkrainianText.Get("ui.feedback.build.already_in_progress", g);
                case BuildOrderResult.QuestOnly: return UkrainianText.Get("ui.feedback.build.quest_only", g);
                case BuildOrderResult.NotEnoughGold: return UkrainianText.Get("ui.feedback.build.not_enough_gold", g);
                default: return UkrainianText.Get("ui.feedback.build.not_enough_materials", g);
            }
        }

        public static string CouncilResultText(CouncilOrderResult r, Gender g)
        {
            switch (r)
            {
                case CouncilOrderResult.Queued: return UkrainianText.Get("ui.council.result.queued", g);
                case CouncilOrderResult.Applied: return UkrainianText.Get("ui.council.result.applied", g);
                case CouncilOrderResult.NoCouncilHall: return UkrainianText.Get("ui.council.result.no_council_hall", g);
                case CouncilOrderResult.AlreadyQueued: return UkrainianText.Get("ui.council.result.already_queued", g);
                case CouncilOrderResult.OnCooldown: return UkrainianText.Get("ui.council.result.on_cooldown", g);
                case CouncilOrderResult.NotEnoughGold: return UkrainianText.Get("ui.council.result.not_enough_gold", g);
                case CouncilOrderResult.NotEnoughFood: return UkrainianText.Get("ui.council.result.not_enough_food", g);
                case CouncilOrderResult.UnknownFaction: return UkrainianText.Get("ui.council.result.unknown_faction", g);
                default: return UkrainianText.Get("ui.council.result.building_not_built", g);
            }
        }

        public static string DispatchResultText(DispatchResult r, Gender g)
        {
            switch (r)
            {
                case DispatchResult.Success: return UkrainianText.Get("ui.feedback.dispatch.success", g);
                case DispatchResult.NoSuchSite: return UkrainianText.Get("ui.feedback.dispatch.no_such_site", g);
                case DispatchResult.EmptyParty: return UkrainianText.Get("ui.feedback.dispatch.empty_party", g);
                case DispatchResult.PartyTooLarge: return UkrainianText.Get("ui.feedback.dispatch.party_too_large", g);
                case DispatchResult.UnknownCompanion: return UkrainianText.Get("ui.feedback.dispatch.unknown_companion", g);
                case DispatchResult.CompanionUnavailable: return UkrainianText.Get("ui.feedback.dispatch.companion_unavailable", g);
                case DispatchResult.DuplicateCompanion: return UkrainianText.Get("ui.feedback.dispatch.duplicate_companion", g);
                default: return UkrainianText.Get("ui.feedback.dispatch.party_already_away", g);
            }
        }

        public static string CraftResultText(CraftResult r, Gender g)
        {
            switch (r)
            {
                case CraftResult.Success: return UkrainianText.Get("ui.feedback.craft.success", g);
                case CraftResult.InvalidItem: return UkrainianText.Get("ui.feedback.craft.invalid_item", g);
                case CraftResult.NamedNotUpgradable: return UkrainianText.Get("ui.feedback.craft.named_not_upgradable", g);
                case CraftResult.AlreadyMaxRarity: return UkrainianText.Get("ui.feedback.craft.already_max_rarity", g);
                case CraftResult.WorkshopClosed: return UkrainianText.Get("ui.feedback.craft.workshop_closed", g);
                default: return UkrainianText.Get("ui.feedback.craft.cannot_afford", g);
            }
        }

        public static string BuildPlanResultText(BuildPlanStatus r, Gender g)
        {
            switch (r)
            {
                case BuildPlanStatus.Ok: return UkrainianText.Get("ui.feedback.buildplan.ok", g);
                case BuildPlanStatus.NotEnoughPoints: return UkrainianText.Get("ui.feedback.buildplan.not_enough_points", g);
                case BuildPlanStatus.AboveSkillCeiling: return UkrainianText.Get("ui.feedback.buildplan.above_skill_ceiling", g);
                case BuildPlanStatus.PerkUnavailable: return UkrainianText.Get("ui.feedback.buildplan.perk_unavailable", g);
                default: return UkrainianText.Get("ui.feedback.buildplan.not_confirmed", g);
            }
        }

        // ===================== стрічка подій (DayLog) =====================

        /// <summary>
        /// Один рядок стрічки подій із <c>GameEvent</c>: якщо ключ є в таблиці —
        /// підставляє відомі плейсхолдери (companion/post/band/item/...), інакше
        /// — чесний резервний рядок "ключ (аргументи)" замість мовчазного
        /// зникнення інформації.
        /// </summary>
        public static string EventLine(GameEvent evt, Gender gender, RosterView roster)
        {
            if (evt == null || string.IsNullOrEmpty(evt.Key)) return string.Empty;
            var a = evt.Args;

            // Фікс-ревью (major, раунд 2, знайдено QA): "char.seen" (і будь-яка
            // інша подія, що називає суб'єкта через сирий аргумент "char", а не
            // "companionId" — GameSession.LogEvent("char.seen", Args("char",
            // actorId))) раніше давала companion = "" (ResolveCompanionName з
            // null), і цей порожній рядок ставав парою "char" РАНІШЕ за сирий
            // цикл-фолбек нижче — стрічка показувала " тут." замість "Тугар
            // Вовк тут.". Тепер companionId-аргумент має пріоритет, а "char" —
            // резервне джерело id того самого підмета.
            string companion = ResolveCompanionName(Arg(a, "companionId") ?? Arg(a, "char"), gender, roster);
            string post = ContentLabel("post", Arg(a, "slotId") ?? Arg(a, "post"), gender);
            string band = BandWordsFor(Arg(a, "band"), gender);
            string item = ContentLabel("item", Arg(a, "itemId") ?? Arg(a, "item"), gender);
            string building = ContentLabel("building", Arg(a, "buildingId"), gender);
            string site = ContentLabel("site", Arg(a, "siteId"), gender);
            string faction = ContentLabel("faction", Arg(a, "factionId") ?? Arg(a, "favored"), gender);
            string scar = ContentLabel("scar", Arg(a, "scarId"), gender);
            string domain = ContentLabel("domain", Arg(a, "domain"), gender);
            string day = Arg(a, "day") ?? "";
            string slot = Arg(a, "slot") ?? "";
            string level = Arg(a, "level") ?? "";
            // Глава арки: назва за ключем, який ядро кладе в подію
            // (chapterTitleKey = ArcChapter.TitleKey). Раніше сюди йшов сирий
            // arcId, а шаблон "{chapterId}" брав сирий "ch1" з хвоста пар.
            string chapter = ChapterLabel(Arg(a, "chapterTitleKey"), Arg(a, "chapterId"), gender);
            // Фікс-ревью (major, знайдено тур-автоплеєм): раніше тут був сирий
            // Arg(a, "questId") ?? "" — на відміну від companion/post/item/
            // building/site/faction/scar/domain нижче, questId НЕ проходив
            // через ContentLabel, тож стрічка подій показувала гравцю сирий
            // QuestDefinition.Id ("Нова пропозиція: hafiya.") замість
            // перекладеного імені ("quest.<id>" у таблиці — той самий
            // префіксний принцип, що й решта).
            string quest = ContentLabel("quest", Arg(a, "questId"), gender);
            string path = PathWords(Arg(a, "path"), gender);
            // Фікс-ревью (major, знайдено QA): "scene.choice.made" — єдина
            // подія, чиї sceneId/optionId ішли в стрічку СИРИМИ ("opening.
            // neighbour: вибір ухвалено — refuse (Базова)." замість "Сусід з
            // претензією: вибір ухвалено — Відмовити (Базова)."), доки решта
            // ContentLabel-полів вище (companion/post/item/...) вже
            // перекладались. Ключі сцен пишуться Core/Scenes/*.cs двома
            // способами: sceneId уже з префіксом "scene." (CompanionScenes —
            // "scene.myroslava.confrontation" → сам собою + ".title"/
            // ".option.<id>") або без нього (OpeningScenes — "opening.
            // neighbour", де заголовок "scene.opening.neighbour.title", а
            // варіанти лишились коротким сегментом "scene.neighbour.
            // option.<id>") — ResolveSceneLabel пробує обидва, тоді останній
            // сегмент sceneId, той самий "кілька спроб, перший збіг" прийом,
            // що вже в NightScreen.DrawOneQuestOffer (offerKeyPrefixed/Bare).
            string sceneIdRaw = Arg(a, "sceneId");
            string optionIdRaw = Arg(a, "optionId");
            string sceneName = ResolveSceneLabel(sceneIdRaw, ".title", gender) ?? sceneIdRaw ?? "";
            string sceneOption = !string.IsNullOrEmpty(optionIdRaw)
                ? ResolveSceneLabel(sceneIdRaw, ".option." + optionIdRaw, gender) ?? optionIdRaw
                : "";

            // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): "companion.died.m"/
            // ".f" (і подібні ключі, що описують КОГОСЬ конкретного, а не
            // мовця) раніше завжди обирались за родом ГЛЯДАЧА (переданий
            // `gender` — рід протагоніста, DrawEventFeed передає
            // ProtagonistGender) — стрічка показувала "Максим Беркут
            // загинула" (жіноча форма), щойно протагоніст обирав жіночий рід,
            // незалежно від того, хто насправді загинув. Ключ вибирається за
            // родом ІМЕННОГО суб'єкта події (companionId), коли він відомий.
            Gender subjectGender = SubjectGender(Arg(a, "companionId") ?? Arg(a, "char"), gender);
            if (!UkrainianText.Has(evt.Key, subjectGender)) return FallbackLine(evt.Key, a);

            // Шаблони таблиці пишуть підстановки двома способами: читабельними
            // іменами ({companion}, {post}) і сирими іменами аргументів події
            // ({companionId}, {slotId}). Обидва варіанти отримують ПЕРЕКЛАДЕНЕ
            // значення — гравець не повинен бачити id. Format бере перший
            // збіг імені, тому переклади йдуть раніше сирих аргументів.
            var pairs = new List<string>
            {
                "companion", companion, "post", post, "band", band, "item", item,
                "building", building, "site", site, "faction", faction, "scar", scar,
                "domain", domain, "day", day, "slot", slot, "level", level,
                "chapter", chapter, "quest", quest, "char", companion, "path", path,
                "companionId", companion, "slotId", post, "itemId", item,
                "buildingId", building, "siteId", site, "factionId", faction,
                "favored", faction, "scarId", scar, "questId", quest, "chapterId", chapter,
                "attackerId", ResolveCompanionName(Arg(a, "attackerId"), gender, roster),
                "targetId", ResolveCompanionName(Arg(a, "targetId"), gender, roster),
                "trigger", ResolveCompanionName(Arg(a, "triggerId"), gender, roster),
                "triggerId", ResolveCompanionName(Arg(a, "triggerId"), gender, roster),
                "incidentId", ContentLabel("incident", Arg(a, "incidentId"), gender),
                "resource", ContentLabel("resource", Arg(a, "resource"), gender),
                "sceneId", sceneName, "optionId", sceneOption,
            };
            if (a != null)
                foreach (var kv in a) { pairs.Add(kv.Key); pairs.Add(kv.Value); }

            return UkrainianText.Format(evt.Key, subjectGender, pairs.ToArray());
        }

        /// <summary>
        /// Рід ІМЕННОГО суб'єкта (companionId), не глядача: фіксований каст
        /// (§3.0 TEST_BUILD.md) має відомий рід кожного, крім протагоніста —
        /// той бере рід глядача (<paramref name="viewerGender"/>), бо глядач
        /// його й обрав (той самий принцип, що вже коректно працює в
        /// BattleArenaController.IsFemaleCompanion, окремій копії для боєвого
        /// шва — тут не викликаний напряму, щоб ScreenText лишався чистим C#
        /// без залежності на лінт-виключений Gameplay-файл).
        /// </summary>
        public static Gender SubjectGender(string companionId, Gender viewerGender)
        {
            if (string.IsNullOrEmpty(companionId)) return viewerGender;
            if (companionId == GameSession.ProtagonistId) return viewerGender;
            switch (companionId)
            {
                case "myroslava":
                case "healer": // Знахарка Гафія
                    return Gender.Female;
                default:
                    return Gender.Male; // Максим, Захар, Дід Овсій, вороги/NPC — чоловічий рід за замовчуванням
            }
        }

        private static string PathWords(string raw, Gender gender)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            return string.Equals(raw, "Bloody", System.StringComparison.OrdinalIgnoreCase)
                ? UkrainianText.Get("ui.decision.path.bloody", gender)
                : UkrainianText.Get("ui.decision.path.quiet", gender);
        }

        private static string BandWordsFor(string rawBand, Gender gender)
        {
            if (string.IsNullOrEmpty(rawBand)) return "";
            string lower = rawBand.ToLowerInvariant();
            if (UkrainianText.Has("band." + lower, gender)) return UkrainianText.Get("band." + lower, gender);
            if (UkrainianText.Has("loyalty.band." + lower, gender)) return UkrainianText.Get("loyalty.band." + lower, gender);
            if (UkrainianText.Has("faction.band." + lower, gender)) return UkrainianText.Get("faction.band." + lower, gender);
            if (UkrainianText.Has("readiness.band." + lower, gender)) return UkrainianText.Get("readiness.band." + lower, gender);
            // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): "dungeon.threat_band_changed"
            // (GameSession.cs) шле сирий DungeonThreatBand.ToString() ("Dangerous",
            // "Tense", "Deadly", "Calm") як "band" — без цього рядка стрічка подій
            // показувала англійське слово напряму (R7 такого не дозволяє).
            if (UkrainianText.Has("dungeon.threat." + lower, gender)) return UkrainianText.Get("dungeon.threat." + lower, gender);
            return rawBand;
        }

        /// <summary>
        /// Назва глави арки для стрічки: ключ назви з події, інакше сирий id
        /// (його ловить автопрогін, а не гравець — ключ є в кожній події арки).
        /// </summary>
        private static string ChapterLabel(string titleKey, string chapterId, Gender gender)
        {
            if (!string.IsNullOrEmpty(titleKey) && UkrainianText.Has(titleKey, gender))
                return "«" + UkrainianText.Get(titleKey, gender) + "»";
            return chapterId ?? "";
        }

        private static string ContentLabel(string prefix, string id, Gender gender)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string key = prefix + "." + id;
            return UkrainianText.Has(key, gender) ? UkrainianText.Get(key, gender) : id;
        }

        /// <summary>
        /// §EventLine (sceneId/optionId). Core/Scenes/*.cs пише sceneId і
        /// ключ тексту РІЗНИМИ конвенціями (сам факт перевірено по джерелу,
        /// не вгадано): "scene.myroslava.confrontation" (CompanionScenes) —
        /// sceneId уже сам є префіксом ключа; "arc.myroslava.ch1"
        /// (CompanionScenes, глави арки) — перший сегмент sceneId ("arc")
        /// заміняється на "scene." ("scene.myroslava.ch1.title"/".option.
        /// trust"); "opening.neighbour" (OpeningScenes) — заголовок
        /// лишає sceneId ЦІЛИМ під "scene." ("scene.opening.neighbour.
        /// title"), а варіанти той самий перший сегмент відкидають
        /// ("scene.neighbour.option.refuse", без "opening"). Спроба 1
        /// (заміна першого сегмента) покриває і "scene."-, і "arc."-
        /// sceneId, і option-гілку "opening."; спроба 2 (sceneId цілим під
        /// "scene.") лишається лише для title-гілки "opening.". Без цього
        /// порядку "arc.myroslava.ch1"+".title" за сирим sceneId+suffix
        /// (без спроби 1) хибно збігався б із ЗОВСІМ ІНШИМ, вже зайнятим
        /// ключем "arc.myroslava.ch1.title" (заголовок глави арки в
        /// журналі, не заголовок самої сцени) — Core/Companions/DefaultArcs.cs
        /// заводить його для СВОЄЇ мети, і рядок сирого sceneId+suffix БЕЗ
        /// префікса "scene." ніколи не є правильним ключем сцени сам собою.
        /// null, якщо жодна спроба не влучила (виклик сам падає на сирий id).
        /// </summary>
        private static string ResolveSceneLabel(string sceneId, string suffix, Gender gender)
        {
            if (string.IsNullOrEmpty(sceneId)) return null;

            int firstDot = sceneId.IndexOf('.');
            string afterFirstSegment = firstDot >= 0 ? sceneId.Substring(firstDot + 1) : sceneId;
            string stripped = "scene." + afterFirstSegment + suffix;
            if (UkrainianText.Has(stripped, gender)) return UkrainianText.Get(stripped, gender);

            string prefixed = "scene." + sceneId + suffix;
            if (UkrainianText.Has(prefixed, gender)) return UkrainianText.Get(prefixed, gender);

            int lastDot = sceneId.LastIndexOf('.');
            if (lastDot >= 0 && lastDot + 1 < sceneId.Length)
            {
                string shortKey = "scene." + sceneId.Substring(lastDot + 1) + suffix;
                if (UkrainianText.Has(shortKey, gender)) return UkrainianText.Get(shortKey, gender);
            }

            return null;
        }

        private static string Arg(IReadOnlyDictionary<string, string> args, string name)
        {
            if (args == null || string.IsNullOrEmpty(name)) return null;
            string v;
            return args.TryGetValue(name, out v) ? v : null;
        }

        private static string FallbackLine(string key, IReadOnlyDictionary<string, string> args)
        {
            if (args == null || args.Count == 0) return key;
            var parts = new List<string>();
            foreach (var kv in args) parts.Add(kv.Key + "=" + kv.Value);
            return key + " (" + string.Join(", ", parts) + ")";
        }

        // ===================== стрічка подій: згортання дублів =====================

        /// <summary>Один рядок готової до показу стрічки — текст і скільки разів він повторився ПІДРЯД.</summary>
        public sealed class FeedLine
        {
            public string Text;
            public int Count;

            /// <summary>
            /// Фікс-ревью (minor, знайдено QA): групова реакція складу
            /// (roster.rippled.*) — той самий шаблон, той самий тригер, але
            /// РІЗНИЙ підмет (companionId), тож звичайне ×N-згортання вище
            /// (порівняння за вже резолвненим текстом, де ім'я вже вшите) їх
            /// не бачить: п'ять "Мирослава: ... мовчки слухає ..." рядків з
            /// різними іменами топили невелику панель стрічки. Імена решти
            /// реагуючих — тут, а не в <see cref="Text"/>: перший рядок
            /// лишається граматично цілим (однина дієслова), решта учасників
            /// дописуються окремим переліком (GameShell.DrawEventFeed).
            /// </summary>
            public List<string> AlsoNames;
        }

        /// <summary>
        /// Ціль 5 «Якість стрічки» (owner feedback): кілька ідентичних рядків
        /// підряд (напр. ряба по кільком напарникам з однаковим типом зв'язку,
        /// або "Ніч минає спокійно" кілька фаз поспіль) згортаються в один із
        /// «×N» замість того, щоб топити стрічку повторами. Порядок — той
        /// самий, що GameShell.DrawEventFeed малював раніше (найновіше
        /// зверху): DayLog зберігає хронологічний порядок, тут ідемо з кінця.
        /// Порівняння — за вже РЕЗОЛВНЕНИМ текстом (не за ключем/аргументами
        /// події): два різні ключі, що випадково дали однаковий рядок, теж
        /// мають право згорнутись — гравець бачить текст, не машинерію.
        /// </summary>
        public static List<FeedLine> BuildFeedLines(IReadOnlyList<GameEvent> log, Gender gender, RosterView roster)
        {
            var result = new List<FeedLine>();
            if (log == null) return result;
            GameEvent prevEvt = null;
            for (int i = log.Count - 1; i >= 0; i--)
            {
                var evt = log[i];
                string text = EventLine(evt, gender, roster);
                if (string.IsNullOrEmpty(text)) continue;

                if (result.Count > 0 && result[result.Count - 1].Text == text)
                {
                    result[result.Count - 1].Count++;
                }
                else if (result.Count > 0 && IsGroupReactionOf(prevEvt, evt))
                {
                    // Той самий тригер/подія, інший реагуючий підмет (див.
                    // FeedLine.AlsoNames) — не новий рядок, а ім'я до вже
                    // доданого.
                    var last = result[result.Count - 1];
                    if (last.AlsoNames == null) last.AlsoNames = new List<string>();
                    last.AlsoNames.Add(ResolveCompanionName(Arg(evt.Args, "companionId"), gender, roster));
                }
                else
                {
                    result.Add(new FeedLine { Text = text, Count = 1 });
                }
                prevEvt = evt;
            }
            return result;
        }

        /// <summary>Див. <see cref="FeedLine.AlsoNames"/>: групова реакція — той самий ключ і той самий triggerId, інший companionId.</summary>
        private static bool IsGroupReactionOf(GameEvent prev, GameEvent evt)
        {
            if (prev == null || evt == null) return false;
            if (!string.Equals(prev.Key, evt.Key, System.StringComparison.Ordinal)) return false;
            if (prev.Key == null || !prev.Key.StartsWith("roster.rippled.", System.StringComparison.Ordinal)) return false;
            string prevTrigger = Arg(prev.Args, "triggerId");
            string evtTrigger = Arg(evt.Args, "triggerId");
            if (string.IsNullOrEmpty(prevTrigger) || !string.Equals(prevTrigger, evtTrigger, System.StringComparison.Ordinal)) return false;
            string prevSubject = Arg(prev.Args, "companionId");
            string evtSubject = Arg(evt.Args, "companionId");
            return !string.Equals(prevSubject, evtSubject, System.StringComparison.Ordinal);
        }
    }
}
