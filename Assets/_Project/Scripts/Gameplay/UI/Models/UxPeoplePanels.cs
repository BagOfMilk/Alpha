using Game.Core.Characters.Build;
using Game.Core.Quests;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Core.Stats;
using Game.Gameplay.Text;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Розмова з людиною на її місці (власник, 30.09.2026: «досі неможна …
    /// поговорити з персонажем»). Ядро окремої команди «поговорити» не має —
    /// картка складається з того, що вже є: привітання, пост, глава арки
    /// («є що сказати» — сцена чи прохання), квест квестодавця. Розмова сама
    /// лояльність не змінює: це було б нове правило ядра (поза треком UX).
    /// </summary>
    public static class UxTalkPanel
    {
        /// <summary>Квест, який дає ця людина (квестодавець на своєму місці, UX_DESIGN §4.6).</summary>
        public static string QuestOf(string companionId)
        {
            switch (companionId)
            {
                case "healer": return DefaultQuests.HafiyaId;
                case DefaultQuests.MaksymId: return DefaultQuests.MaksymCh1Id;
                default: return null;
            }
        }

        /// <summary>
        /// Позначка «!» над головою (UX-15): відкрита глава арки. Лише
        /// перевірка, без побічних дій — пропозицію квесту тут не питаємо,
        /// бо вона пише в стрічку.
        /// </summary>
        public static bool HasSomethingToSay(IUxHost h, string companionId)
        {
            try { return h.Session.IsArcChapterAvailable(companionId); }
            catch (System.InvalidOperationException) { return false; }
        }

        public static UxPanelModel Build(IUxHost h, string companionId)
        {
            var g = h.Gender;
            var panel = UxCityPanels.New(h, UxPanelId.Talk);
            var roster = h.Session.GetRosterView();
            var who = ScreenText.FindCompanion(roster, companionId);
            if (who == null || !Walk.VillagePeople.IsInVillage(who))
            {
                panel.EmptyText = UxBricks.T(h, "ux.talk.not_here");
                return panel;
            }
            panel.Title = UxBricks.Name(h, companionId, roster);

            var card = new UxCard
            {
                Title = UxBricks.Name(h, companionId, roster),
                Subtitle = ScreenText.ClassLabel(who.Class, g) + " · " + ScreenText.CompanionStatusLabel(companionId, who.Status, g)
            };
            if (who.Loyalty.HasValue) card.Chips.Add(new UxChip(ScreenText.LoyaltyLabel(who.Loyalty, g)));
            if (!string.IsNullOrEmpty(who.AssignedSlotId))
                card.Chips.Add(new UxChip(UxBricks.T(h, "post." + who.AssignedSlotId), UxTone.Own));
            string greet = "talk.greet." + companionId;
            card.Lines.Add(UkrainianText.Has(greet, g) ? UkrainianText.Get(greet, g) : UkrainianText.Get("talk.greet.generic", g));

            if (HasSomethingToSay(h, companionId))
            {
                if (h.Session.IsArcChapterSceneContent(companionId))
                    card.Actions.Add(Talk(h, "talk:arc:" + companionId, UxBricks.T(h, "ux.talk.arc_scene"), UxIntent.Primary, () =>
                    {
                        SceneStepView first;
                        var outcome = UxCommandRunner.Run(() => h.Session.BeginArcChapterScene(companionId), null, UxBricks.Female(h), out first);
                        if (outcome.Ok && first != null) h.BeginScene(first);
                        return outcome;
                    }).Calls(nameof(GameSession.BeginArcChapterScene)));
                else if (h.Session.IsArcChapterQuestContent(companionId))
                    card.Actions.Add(Talk(h, "talk:arcquest:" + companionId, UxBricks.T(h, "ux.talk.arc_quest"), UxIntent.Primary, () =>
                    {
                        var outcome = UxBricks.Run(h, () => h.Session.BeginArcChapterQuest(companionId));
                        string quest = QuestOf(companionId);
                        if (quest != null) h.PanelState.Quests.Invalidate(quest);
                        return outcome;
                    }).Calls(nameof(GameSession.BeginArcChapterQuest)));
            }

            card.Actions.Add(UxBricks.Go("talk:sheet:" + companionId, UxBricks.T(h, "ux.talk.sheet"), () => h.OpenPanel(UxPanelId.People, companionId)));
            card.Actions.Add(UxBricks.Go("talk:duty", UxBricks.T(h, "ux.talk.duty"), () => h.OpenPanel(UxPanelId.DutyBoard, null)));
            panel.Cards.Add(card);

            string questId = QuestOf(companionId);
            if (questId != null)
            {
                var offer = h.PanelState.Quests.Get(h.Session, questId);
                if (offer != null) panel.Cards.Add(UxQuestCards.Offer(h, offer));
            }
            return panel;
        }

        /// <summary>Дія розмови: ядро пускає главу арки вранці, увечері, вночі й у вільній грі.</summary>
        private static UxAction Talk(IUxHost h, string id, string label, UxIntent intent, System.Func<UxOutcome> execute)
        {
            var a = new UxAction { Id = id, Label = label, Intent = intent, Execute = execute };
            a.AllowedStates.Add(SessionState.Morning);
            a.AllowedStates.Add(SessionState.FreePlay);
            a.AllowedStates.Add(SessionState.Evening);
            a.AllowedStates.Add(SessionState.Night);
            return a;
        }
    }

    /// <summary>
    /// Картка людини (C → людина, docs/UX_DESIGN.md §5.11): чотири підвкладки —
    /// огляд, навички, спорядження, розвиток. Числа — лише ті, що дозволені
    /// (атрибути й скіли звіряють з порогами, R17); лояльність — словом (UI-06).
    /// </summary>
    public static class UxPersonPanel
    {
        public static UxPanelModel Build(IUxHost h, string companionId)
        {
            var g = h.Gender;
            var panel = UxCityPanels.New(h, UxPanelId.People);
            var roster = h.Session.GetRosterView();
            var sheet = h.Session.GetCharacterSheet(companionId);
            if (sheet == null) { panel.EmptyText = UxBricks.T(h, "ui.sheet.pick_someone"); return panel; }
            panel.Title = UxBricks.Name(h, companionId, roster);

            string overview = UxBricks.T(h, "ux.person.section.overview");
            string skills = UxBricks.T(h, "ux.person.section.skills");
            string gear = UxBricks.T(h, "ux.person.section.gear");
            string growth = UxBricks.T(h, "ux.person.section.growth");

            var head = new UxCard { Section = overview, Title = UxBricks.Name(h, companionId, roster), Subtitle = ScreenText.ClassLabel(sheet.Class, g) };
            head.Chips.Add(new UxChip(UkrainianText.Format("ux.people.level", g, "n", sheet.Level.ToString())));
            head.Chips.Add(new UxChip(ScreenText.CompanionStatusLabel(companionId, sheet.Status, g)));
            if (sheet.Loyalty.HasValue) head.Chips.Add(new UxChip(ScreenText.LoyaltyLabel(sheet.Loyalty, g)));
            head.Lines.Add(UkrainianText.Format("ui.sheet.xp", g, "xp", sheet.Xp.ToString(), "next", sheet.XpToNextLevel.ToString()));
            head.Actions.Add(UxBricks.Go("person:back", UxBricks.T(h, "ux.person.back"), () => h.OpenPanel(UxPanelId.People, null)));
            panel.Cards.Add(head);

            var traits = new UxCard { Section = overview, Title = UxBricks.T(h, "ui.sheet.section.traits") };
            foreach (var tr in sheet.Traits)
            {
                bool bad = tr.Polarity != null && tr.Polarity.ToLowerInvariant().StartsWith("neg");
                traits.Chips.Add(new UxChip(UxBricks.T(h, "trait." + tr.TraitId), bad ? UxTone.Bad : UxTone.Good));
                traits.Lines.Add(UxBricks.T(h, "trait." + tr.TraitId) + ": " + UxBricks.T(h, "trait." + tr.TraitId + ".effect"));
            }
            foreach (var scarId in sheet.ScarIds)
                traits.Chips.Add(new UxChip(UxBricks.T(h, "scar." + scarId), UxTone.Bad));
            if (traits.Chips.Count == 0) traits.Lines.Add(UxBricks.T(h, "ui.sheet.none"));
            panel.Cards.Add(traits);

            var attrs = new UxCard { Section = skills, Title = UxBricks.T(h, "ui.sheet.section.attributes") };
            foreach (var a in sheet.Attributes) attrs.Chips.Add(new UxChip(UxBricks.T(h, "attr." + a.AttributeKey) + " " + a.Score));
            panel.Cards.Add(attrs);
            var skillCard = new UxCard { Section = skills, Title = UxBricks.T(h, "ui.sheet.section.skills") };
            foreach (var s in sheet.Skills) skillCard.Chips.Add(new UxChip(UxBricks.T(h, "skill." + s.SkillKey) + " " + s.Score, s.Score > 0 ? UxTone.Own : UxTone.Neutral));
            panel.Cards.Add(skillCard);
            var combat = new UxCard { Section = skills, Title = UxBricks.T(h, "ui.sheet.section.combat") };
            combat.Chips.Add(new UxChip(UkrainianText.Format("ui.sheet.combat.hp", g, "value", sheet.Combat.HpMax.ToString())));
            combat.Chips.Add(new UxChip(UkrainianText.Format("ui.sheet.combat.ap", g, "value", sheet.Combat.ApMax.ToString())));
            combat.Chips.Add(new UxChip(UkrainianText.Format("ui.sheet.combat.initiative", g, "value", sheet.Combat.Initiative.ToString())));
            combat.Chips.Add(new UxChip(UkrainianText.Format("ui.sheet.combat.accuracy", g, "value", sheet.Combat.Accuracy.ToString())));
            combat.Chips.Add(new UxChip(UkrainianText.Format("ui.sheet.combat.defense", g, "value", sheet.Combat.Defense.ToString())));
            combat.Chips.Add(new UxChip(UkrainianText.Format("ui.sheet.combat.armor", g, "value", sheet.Combat.Armor.ToString())));
            panel.Cards.Add(combat);

            var equipment = new UxCard { Section = gear, Title = UxBricks.T(h, "ui.sheet.section.equipment") };
            equipment.Chips.Add(new UxChip(UxBricks.T(h, "ui.gear.slot.weapon") + ": " + ItemLabel(h, sheet.Equipment.WeaponId)));
            equipment.Chips.Add(new UxChip(UxBricks.T(h, "ui.gear.slot.armor") + ": " + ItemLabel(h, sheet.Equipment.ArmorId)));
            equipment.Chips.Add(new UxChip(UxBricks.T(h, "ui.gear.slot.accessory") + ": " + ItemLabel(h, sheet.Equipment.AccessoryId)));
            equipment.Actions.Add(UxBricks.Go("person:stash", UxBricks.T(h, "ux.person.to_stash"), () => h.OpenPanel(UxPanelId.Stash, null)));
            panel.Cards.Add(equipment);

            if (companionId == GameSession.ProtagonistId)
                panel.Cards.Add(Growth(h, growth));
            return panel;
        }

        /// <summary>
        /// План розвитку протагоніста: +1 у скіл, скинути, затвердити.
        /// Затвердження незворотне — лише з підтвердженням (UX-12); раніше
        /// кнопка одразу передавала ядру «підтверджено».
        /// </summary>
        internal static UxCard Growth(IUxHost h, string section)
        {
            var g = h.Gender;
            var st = h.PanelState;
            var card = new UxCard { Section = section, Title = UxBricks.T(h, "ui.buildplanner.title") };
            BuildPreview preview;
            var ok = UxCommandRunner.Run(() => h.Session.PreviewBuildPlan(GameSession.ProtagonistId, st.Plan), null, UxBricks.Female(h), out preview);
            if (!ok.Ok || preview == null) { card.Lines.Add(ok.Refusal ?? string.Empty); return card; }

            card.Chips.Add(new UxChip(UkrainianText.Format("ui.buildplanner.points", g, "points", preview.PointsAvailable.ToString()),
                preview.PointsAvailable > 0 ? UxTone.Own : UxTone.Neutral));
            // Порожній стан навчає (UX-13): без очок і без плану — не десять сірих «+1» з однаковою
            // причиною (знімок туру 06.10.2026), а одне речення, звідки беруться очки.
            if (preview.PointsAvailable == 0 && st.Plan.IsEmpty)
            {
                card.Lines.Add(UxBricks.T(h, "ux.growth.no_points"));
                return card;
            }
            if (preview.PointsAvailable == 0) card.Lines.Add(UxBricks.T(h, "ui.buildplanner.no_points"));
            card.Lines.Add(ScreenText.BuildPlanResultText(preview.Status, g));

            foreach (var skill in Skills.All)
            {
                var s = skill;
                string name = UxBricks.T(h, "skill." + skill.ToString().ToLowerInvariant());
                int invested = st.Plan.InvestedIn(skill);
                var a = UxBricks.Act("plan:" + skill, UkrainianText.Format("ux.person.invest", g, "skill", name, "n", invested.ToString()),
                    UxIntent.Secondary, () => { st.Plan.Invest(s, 1); return UxOutcome.Success(); });
                if (preview.PointCost >= preview.PointsAvailable) a.DisabledReason = UxBricks.T(h, "ui.buildplanner.no_points");
                card.Actions.Add(a);
            }
            card.Actions.Add(UxBricks.Go("plan:reset", UxBricks.T(h, "ui.buildplanner.reset"), () => st.Plan = new BuildPlan()));
            if (preview.CanCommit && !st.Plan.IsEmpty)
            {
                var commit = UxBricks.Act("plan:commit", UxBricks.T(h, "ui.buildplanner.commit"), UxIntent.Danger, () =>
                {
                    BuildPlanStatus status;
                    var outcome = UxCommandRunner.Run(() => h.Session.CommitBuildPlan(GameSession.ProtagonistId, st.Plan, true),
                        r => r == BuildPlanStatus.Ok ? null : ScreenText.BuildPlanResultText(r, g), UxBricks.Female(h), out status);
                    if (outcome.Ok) st.Plan = new BuildPlan();
                    return outcome;
                }).Calls(nameof(GameSession.CommitBuildPlan));
                commit.Confirm = new UxConfirm(UxBricks.T(h, "ux.person.commit.question"), UxBricks.T(h, "ux.person.commit.verb"),
                    new[] { UxBricks.T(h, "ux.person.commit.loss") });
                card.Actions.Add(commit);
            }
            return card;
        }

        private static string ItemLabel(IUxHost h, string itemId) =>
            string.IsNullOrEmpty(itemId) ? UxBricks.T(h, "ui.sheet.none")
            : (UkrainianText.Has("item." + itemId, h.Gender) ? UxBricks.T(h, "item." + itemId) : itemId);
    }

    /// <summary>Одна точка входу: панель за id і контекстом (станція, людина, будівля).</summary>
    public static class UxPanelFactory
    {
        public static UxPanelModel Build(IUxHost h, UxPanelId id, string context)
        {
            switch (id)
            {
                case UxPanelId.DutyBoard: return UxCityPanels.DutyBoard(h);
                case UxPanelId.Blueprints: return UxCityPanels.Blueprints(h);
                case UxPanelId.PlotCard: return UxCityPanels.PlotCard(h, context);
                case UxPanelId.BuildingCard: return UxCityPanels.BuildingCard(h, context);
                case UxPanelId.Veche: return UxCouncilPanels.Veche(h);
                case UxPanelId.CouncilTable: return UxCouncilPanels.CouncilTable(h);
                case UxPanelId.Muster: return UxPlacePanels.Muster(h);
                case UxPanelId.Stash: return UxPlacePanels.Stash(h);
                case UxPanelId.Workbench: return UxPlacePanels.Workbench(h);
                case UxPanelId.NoticeBoard: return UxPlacePanels.NoticeBoard(h);
                case UxPanelId.TrainingGround: return UxPlacePanels.TrainingGround(h);
                case UxPanelId.MechanicsJournal: return UxPlacePanels.MechanicsJournal(h);
                case UxPanelId.Station: return UxPlacePanels.Station(h, context);
                case UxPanelId.HeroTent: return UxPlacePanels.HeroTent(h);
                case UxPanelId.Talk: return UxTalkPanel.Build(h, context);
                case UxPanelId.People:
                    return string.IsNullOrEmpty(context)
                        ? UxPeoplePanel.Build(h.Session.GetRosterView(), h.Gender, id2 => h.OpenPanel(UxPanelId.People, id2))
                        : UxPersonPanel.Build(h, context);
                case UxPanelId.Journal:
                    return UxJournalPanel.Build(h.Session.GetQuestOffer(), h.Session.GetFactionsView(), h.Session.GetReadinessView(), h.Gender);
                case UxPanelId.Save:
                    return UxSavePanel.Build(h.SaveSlots(), h.Gender, h.SaveToSlot, h.LoadSlot, true);
                default:
                    return new UxPanelModel { Id = id, Title = string.Empty, EmptyText = UxBricks.T(h, "ux.panel.soon") };
            }
        }
    }
}
