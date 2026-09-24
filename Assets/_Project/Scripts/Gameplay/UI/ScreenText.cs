using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Characters.Build;
using Game.Core.Characters.Creation;
using Game.Core.Checks;
using Game.Core.Items;
using Game.Core.Session;
using Game.Core.Session.Views;
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

            public Legality(bool enabled, string reasonKey)
            {
                Enabled = enabled;
                ReasonKey = reasonKey;
            }

            public static readonly Legality Ok = new Legality(true, null);
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
                case CompanionStatus.Dead: return new Legality(false, "ui.reason.dead");
                case CompanionStatus.OnMission: return new Legality(false, "ui.reason.on_mission");
                case CompanionStatus.Antagonist: return new Legality(false, "ui.reason.antagonist");
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
            string skill = SkillLabel(option.SkillKey, gender);
            string candidate = option.HasCandidate
                ? UkrainianText.Format("ui.decision.candidate", gender, "name", ResolveCompanionName(option.BestActorId, gender, null))
                : UkrainianText.Get("ui.decision.no_candidate", gender);
            string band = string.IsNullOrEmpty(option.ExpectedBand) ? "" : BandWordsFor(option.ExpectedBand, gender);

            return UkrainianText.Format("ui.decision.option_line", gender,
                "path", path, "skill", skill, "threshold", option.Threshold.ToString(),
                "candidate", candidate, "band", band);
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

        public static string ResolveCompanionName(string companionId, Gender gender, RosterView roster)
        {
            if (string.IsNullOrEmpty(companionId)) return string.Empty;
            string charKey = "char." + companionId;
            if (UkrainianText.Has(charKey, gender)) return UkrainianText.Get(charKey, gender);

            var summary = FindCompanion(roster, companionId);
            if (summary != null && !string.IsNullOrEmpty(summary.DisplayName)) return summary.DisplayName;
            return companionId;
        }

        // ===================== збереження =====================

        public static string SaveSlotLine(int slot, bool occupied, string headline, int day, Gender gender)
        {
            return occupied
                ? UkrainianText.Format("ui.save.slot", gender, "slot", slot.ToString(), "headline", headline ?? "", "day", day.ToString())
                : UkrainianText.Format("ui.save.slot.empty", gender, "slot", slot.ToString());
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

            string companion = ResolveCompanionName(Arg(a, "companionId"), gender, roster);
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
            string chapter = Arg(a, "arcId") ?? "";
            string quest = Arg(a, "questId") ?? "";
            string path = PathWords(Arg(a, "path"), gender);

            if (!UkrainianText.Has(evt.Key, gender)) return FallbackLine(evt.Key, a);

            return UkrainianText.Format(evt.Key, gender,
                "companion", companion, "post", post, "band", band, "item", item,
                "building", building, "site", site, "faction", faction, "scar", scar,
                "domain", domain, "day", day, "slot", slot, "level", level,
                "chapter", chapter, "quest", quest, "char", companion, "path", path);
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
            return rawBand;
        }

        private static string ContentLabel(string prefix, string id, Gender gender)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string key = prefix + "." + id;
            return UkrainianText.Has(key, gender) ? UkrainianText.Get(key, gender) : id;
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
    }
}
