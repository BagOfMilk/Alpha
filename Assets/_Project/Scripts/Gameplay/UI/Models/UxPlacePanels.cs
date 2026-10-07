using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Expeditions;
using Game.Core.Items;
using Game.Core.Quests;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using Game.Gameplay.Walk;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Картка пропозиції квесту (docs/UX_DESIGN.md §4.7): текст етапу,
    /// поріг, варіанти. Кожен варіант — дія; вибір — вранці, увечері чи
    /// вночі (гейт ядра), причина видна до кліку.
    /// </summary>
    public static class UxQuestCards
    {
        public static UxCard Offer(IUxHost h, QuestOfferView offer)
        {
            var g = h.Gender;
            var card = new UxCard { Title = ScreenText.QuestName(offer.QuestId, g) };
            string prefixed = "quest." + offer.QuestId + ".offer", bare = offer.QuestId + ".offer";
            string bodyKey = UkrainianText.Has(prefixed, g) ? prefixed : bare;
            if (offer.Stage == 0 && UkrainianText.Has(bodyKey, g))
                card.Lines.Add(UkrainianText.Get(bodyKey, g));
            else if (offer.Options == null || offer.Options.Count == 0)
            {
                string check = ScreenText.QuestCheckLine(offer, g);
                if (check.Length > 0) card.Lines.Add(check);
                // Етап-перевірка: «Спробувати» (з порогом) чи «Підтвердити» — у світі, а не лише в NightScreen
                // (огляд 06.10.2026: вранці квест було не довести до кінця на Дошці чи в людини).
                string questId = offer.QuestId;
                var attempt = new UxAction
                {
                    Id = "quest:" + questId + ":check",
                    Label = UkrainianText.Get(check.Length > 0 ? "ui.quest.check.attempt" : "ui.common.confirm", g),
                    Intent = UxIntent.Primary,
                    Command = nameof(GameSession.ResolveQuestChoice),
                    Execute = () =>
                    {
                        var outcome = UxBricks.Run(h, () =>
                        {
                            h.Session.OfferQuestStage(questId);
                            h.Session.ResolveQuestChoice(0);
                        });
                        h.PanelState.Quests.Invalidate(questId);
                        return outcome;
                    }
                };
                attempt.AllowedStates.Add(SessionState.Morning);
                attempt.AllowedStates.Add(SessionState.Evening);
                attempt.AllowedStates.Add(SessionState.Night);
                card.Actions.Add(attempt);
                return card;
            }
            if (offer.Options == null) return card;
            for (int i = 0; i < offer.Options.Count; i++)
            {
                int index = i;
                string questId = offer.QuestId;
                var option = offer.Options[i];
                string label = UkrainianText.Has(option.TextKey, g) ? UkrainianText.Get(option.TextKey, g) : option.TextKey;
                var action = new UxAction
                {
                    Id = "quest:" + questId + ":" + i, Label = label,
                    Intent = option.Path == IncidentPathView.Bloody ? UxIntent.Danger : UxIntent.Secondary,
                    Command = nameof(GameSession.ResolveQuestChoice),
                    // Дві лінії квесту ділять один вказівник пропозиції в ядрі:
                    // перезапит саме цього квесту прямо перед вибором (як у старому хабі).
                    Execute = () =>
                    {
                        var outcome = UxBricks.Run(h, () =>
                        {
                            h.Session.OfferQuestStage(questId);
                            h.Session.ResolveQuestChoice(index);
                        });
                        h.PanelState.Quests.Invalidate(questId);
                        return outcome;
                    }
                };
                action.AllowedStates.Add(SessionState.Morning);
                action.AllowedStates.Add(SessionState.Evening);
                action.AllowedStates.Add(SessionState.Night);
                if (!string.IsNullOrEmpty(option.SkillKey) && option.Threshold > 0)
                    action.Chips.Add(new UxChip(UkrainianText.Get("skill." + option.SkillKey, g) + " ≥ " + option.Threshold));
                if (!option.HasCandidate) action.DisabledReason = UkrainianText.Get("ux.quest.no_candidate", g);
                card.Actions.Add(action);
            }
            return card;
        }
    }

    /// <summary>
    /// Панелі місць: станції в будівлях і просто неба, збори на Заставі,
    /// схованка, верстак, дошка оголошень, тренувальний майданчик, журнал
    /// механік. Кожна — з карток і дій, таблиці як примітиву немає (§6).
    /// </summary>
    public static class UxPlacePanels
    {
        /// <summary>Станція за id (BuildingCatalog): пост працівника і те, що ця станція показує.</summary>
        public static UxPanelModel Station(IUxHost h, string stationId)
        {
            var g = h.Gender;
            var def = BuildingCatalog.FindStation(stationId);
            var panel = UxCityPanels.New(h, UxPanelId.Station);
            if (def == null) { panel.EmptyText = UxBricks.T(h, "ux.station.unknown"); return panel; }
            panel.Title = UxBricks.T(h, def.LabelKey);
            var roster = h.Session.GetRosterView();
            var city = h.Session.GetCityView();

            if (!string.IsNullOrEmpty(def.PostId))
            {
                var post = UxCityCards.Post(h, def.PostId, roster, city);
                string what = "ux.station." + stationId + ".what";
                if (UkrainianText.Has(what, g)) post.Subtitle = UkrainianText.Get(what, g);
                panel.Cards.Add(post);
            }

            switch (stationId)
            {
                case "watch_wall": WatchWall(h, panel); break;
                case "pantry": Pantry(h, panel); break;
                case "infirmary_beds": InfirmaryBeds(h, panel, roster); break;
                case "market_traders": MarketTraders(h, panel); break;
                case "tavern_tables": TavernTables(h, panel, roster); break;
                case "temple_memorial": Memorial(h, panel, roster); break;
            }
            return panel;
        }

        private static void WatchWall(IUxHost h, UxPanelModel panel)
        {
            var card = new UxCard { Title = UxBricks.T(h, "building.watch"), Subtitle = UxBricks.T(h, "ux.station.watch_wall.what") };
            var readiness = h.Session.GetReadinessView();
            if (readiness != null && !string.IsNullOrEmpty(readiness.Band))
                card.Chips.Add(new UxChip(UxBricks.F(h, "ux.readiness.word", "band", ScreenText.ReadinessLabel(readiness.Band, h.Gender))));
            card.Actions.Add(UxBricks.Go("open:journal", UxBricks.T(h, "ux.watch.open_threat"), () => h.OpenPanel(UxPanelId.Journal, null)));
            panel.Cards.Add(card);
        }

        /// <summary>Комора: відкриті числа запасів (R17) — золото, будматеріал, сировина, їжа.</summary>
        private static void Pantry(IUxHost h, UxPanelModel panel)
        {
            var e = h.Session.GetEconomyView();
            var card = new UxCard { Title = UxBricks.T(h, "ux.station.pantry"), Subtitle = UxBricks.T(h, "ux.station.pantry.what") };
            if (e != null)
            {
                card.Chips.Add(new UxChip(UxBricks.T(h, "resource.gold") + ": " + e.Gold));
                card.Chips.Add(new UxChip(UxBricks.T(h, "resource.build_component") + ": " + e.BuildComponent));
                card.Chips.Add(new UxChip(UxBricks.T(h, "resource.craft_component") + ": " + e.CraftComponent));
                card.Chips.Add(new UxChip(UxBricks.T(h, "resource.food") + ": " + e.Food, e.Food <= 0 ? UxTone.Bad : UxTone.Neutral));
            }
            panel.Cards.Add(card);
        }

        private static void InfirmaryBeds(IUxHost h, UxPanelModel panel, RosterView roster)
        {
            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                {
                    if (c.Status != CompanionStatus.Injured && c.Status != CompanionStatus.Resting) continue;
                    var card = new UxCard { Title = UxBricks.Name(h, c.Id, roster) };
                    card.Chips.Add(new UxChip(ScreenText.CompanionStatusLabel(c.Id, c.Status, h.Gender), UxTone.Bad));
                    panel.Cards.Add(card);
                }
            if (panel.Cards.Count <= 1) panel.EmptyText = UxBricks.T(h, "ux.infirmary.empty");
        }

        private static void MarketTraders(IUxHost h, UxPanelModel panel)
        {
            var factions = h.Session.GetFactionsView();
            if (factions?.Factions != null)
                foreach (var f in factions.Factions)
                {
                    var card = new UxCard { Title = UkrainianText.Has("faction." + f.Id, h.Gender) ? UxBricks.T(h, "faction." + f.Id) : f.DisplayName };
                    card.Chips.Add(new UxChip(ScreenText.FactionBandLabel(f.Band, h.Gender)));
                    panel.Cards.Add(card);
                }
            var toll = h.PanelState.Quests.Get(h.Session, DefaultQuests.MarketTollId);
            if (toll != null) panel.Cards.Add(UxQuestCards.Offer(h, toll));
        }

        /// <summary>Столи Таверни: хто з людей хоче поговорити (UX-15) — «Поговорити» веде до розмови.</summary>
        private static void TavernTables(IUxHost h, UxPanelModel panel, RosterView roster)
        {
            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                {
                    if (!VillagePeople.IsInVillage(c) || !UxTalkPanel.HasSomethingToSay(h, c.Id)) continue;
                    string id = c.Id;
                    var card = new UxCard { Title = UxBricks.Name(h, id, roster), Subtitle = UxBricks.T(h, "ux.talk.has_news") };
                    card.Actions.Add(UxBricks.Go("talk:" + id, UxBricks.T(h, "ux.talk.open"), () => h.OpenPanel(UxPanelId.Talk, id)));
                    panel.Cards.Add(card);
                }
            if (panel.Cards.Count == 0) panel.EmptyText = UxBricks.T(h, "ux.tavern.quiet");
        }

        /// <summary>Меморіал Храму: загиблі й ті, хто пішов (статуси ростера публічні).</summary>
        private static void Memorial(IUxHost h, UxPanelModel panel, RosterView roster)
        {
            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                {
                    if (c.Status != CompanionStatus.Dead && c.Status != CompanionStatus.Antagonist) continue;
                    var card = new UxCard { Title = UxBricks.Name(h, c.Id, roster) };
                    card.Chips.Add(new UxChip(ScreenText.CompanionStatusLabel(c.Id, c.Status, h.Gender), UxTone.Bad));
                    panel.Cards.Add(card);
                }
            if (panel.Cards.Count == 0) panel.EmptyText = UxBricks.T(h, "ux.memorial.empty");
        }

        /// <summary>
        /// Збори на Заставі: куди (точка й підхід), загін, прогноз і «Вирушати».
        /// Вибір точки й людей — у стані панелі, ядро кличеться лише прогнозом і виходом.
        /// </summary>
        public static UxPanelModel Muster(IUxHost h)
        {
            var g = h.Gender;
            var st = h.PanelState;
            var panel = UxCityPanels.New(h, UxPanelId.Muster);
            var roster = h.Session.GetRosterView();
            var city = h.Session.GetCityView();
            string where = UxBricks.T(h, "ux.muster.section.where");
            string who = UxBricks.T(h, "ux.muster.section.party");
            string go = UxBricks.T(h, "ux.muster.section.go");

            var scout = UxCityCards.Post(h, "scouting_post", roster, city);
            scout.Section = where;
            panel.Cards.Add(scout);

            foreach (var site in UxBricks.MusterSiteIds())
            {
                string siteId = site;
                var card = new UxCard { Section = where, Title = UxBricks.T(h, "site." + site) };
                var pick = UxBricks.Go("site:" + site, UxBricks.T(h, st.MusterSite == site ? "ux.muster.picked" : "ux.muster.pick"), () =>
                {
                    st.MusterSite = siteId;
                    st.MusterPreview = null;
                    // Данж — лише вглиб; звичайна точка — тихо чи силою (у ядрі данжу немає серед точок вилазок).
                    if (UxBricks.IsDungeonSite(siteId)) st.MusterApproach = ExpeditionApproach.Delve;
                    else if (st.MusterApproach == ExpeditionApproach.Delve) st.MusterApproach = ExpeditionApproach.Quiet;
                });
                pick.Selected = st.MusterSite == site;
                card.Actions.Add(pick);
                panel.Cards.Add(card);
            }

            var approach = new UxCard { Section = where, Title = UxBricks.T(h, "ux.muster.approach") };
            if (UxBricks.IsDungeonSite(st.MusterSite))
                approach.Actions.Add(Approach(h, ExpeditionApproach.Delve, "ui.expedition.approach.delve"));
            else
            {
                approach.Actions.Add(Approach(h, ExpeditionApproach.Quiet, "ui.expedition.approach.quiet"));
                approach.Actions.Add(Approach(h, ExpeditionApproach.Forceful, "ui.expedition.approach.forceful"));
            }
            panel.Cards.Add(approach);

            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                {
                    if (c.Status == CompanionStatus.NotArrived) continue;
                    string id = c.Id;
                    bool inParty = st.MusterParty.Contains(id);
                    var card = new UxCard { Section = who, Title = UxBricks.Name(h, id, roster) };
                    if (!string.IsNullOrEmpty(c.AssignedSlotId))
                        card.Chips.Add(new UxChip(UxBricks.T(h, "post." + c.AssignedSlotId), UxTone.Own));
                    var toggle = UxBricks.Go("party:" + id, UxBricks.T(h, inParty ? "ux.muster.in_party" : "ux.muster.add"), () =>
                    {
                        if (st.MusterParty.Contains(id)) st.MusterParty.Remove(id); else st.MusterParty.Add(id);
                        st.MusterPreview = null;
                        st.MusterDeputies.Clear(); // склад змінився — інші пости, інші кандидати
                    });
                    toggle.Selected = inParty;
                    var legality = ScreenText.PartyCandidateLegality(c);
                    if (!legality.Enabled && !inParty) toggle.DisabledReason = ScreenText.ReasonText(legality, g);
                    card.Actions.Add(toggle);
                    panel.Cards.Add(card);
                }

            // Заступники на пости (Поправка №8.3, M1.6): гравець обирає сам, автоматики нема.
            string posts = UxBricks.T(h, "ux.muster.section.posts");
            bool deputiesUndecided = false;
            if (st.MusterParty.Count > 0 && (h.Session.State == SessionState.Morning || h.Session.State == SessionState.FreePlay))
                deputiesUndecided = DeputyCards(h, panel, posts, new List<string>(st.MusterParty), roster);

            var summary = new UxCard { Section = go, Title = UxBricks.T(h, "site." + st.MusterSite) };
            summary.Chips.Add(new UxChip(UxBricks.T(h, ApproachKey(st.MusterApproach)), UxTone.Own));
            var party = new List<string>(st.MusterParty);
            foreach (var id in party) summary.Chips.Add(new UxChip(UxBricks.Name(h, id, roster), UxTone.Own));
            var partyLegality = ScreenText.ExpeditionPartyLegality(party, roster);

            var preview = UxBricks.Act("muster:preview", UxBricks.T(h, "ui.expedition.preview"), UxIntent.Secondary, () =>
            {
                ExpeditionPreviewView view;
                var outcome = UxCommandRunner.Run(() => h.Session.PreviewExpedition(st.MusterSite, st.MusterApproach, party), null, UxBricks.Female(h), out view);
                if (outcome.Ok) st.MusterPreview = view;
                return outcome;
            }).Calls(nameof(GameSession.PreviewExpedition));
            if (!partyLegality.Enabled) preview.DisabledReason = ScreenText.ReasonText(partyLegality, g);
            summary.Actions.Add(preview);

            var p = st.MusterPreview;
            if (p != null && partyLegality.Enabled)
            {
                summary.Lines.Add(UxBricks.F(h, "ux.muster.check", "threshold", p.Threshold.ToString(), "value", p.PartyValue.ToString()));
                summary.Lines.Add(UxBricks.F(h, "ux.muster.forecast", "days", UkrainianText.DayCount(p.Days),
                    "band", ScreenText.BandLabelFromRaw(p.ExpectedBand, g)));
                summary.Lines.Add(UxBricks.F(h, "ux.muster.loot", "build", p.ExpectedBuildComponent.ToString(),
                    "craft", p.ExpectedCraftComponent.ToString(), "gold", p.ExpectedGold.ToString()));
                // Поправка №21.2: негода вже врахована в здобичі вище — тут лише причина.
                if (!string.IsNullOrEmpty(p.WeatherKey))
                    summary.Lines.Add(UxBricks.T(h, p.WeatherKey));
                if (!string.IsNullOrEmpty(p.WaitingSpecialistId))
                    summary.Lines.Add(UxBricks.F(h, "ux.muster.waiting", "name", UxBricks.Name(h, p.WaitingSpecialistId, roster)));
                int days = p.Days;
                var depart = UxBricks.Act("muster:depart", UxBricks.T(h, "ui.expedition.depart"), UxIntent.Primary, () =>
                {
                    var deputies = new Dictionary<string, string>(st.MusterDeputies);
                    var outcome = UxBricks.Reported(h, () => h.Session.DepartExpedition(st.MusterSite, st.MusterApproach, party, days, deputies),
                        r => ScreenText.DispatchFailure(r, g));
                    if (outcome.Ok) { st.MusterParty.Clear(); st.MusterDeputies.Clear(); st.MusterPreview = null; }
                    return outcome;
                }).Calls(nameof(GameSession.DepartExpedition));
                if (deputiesUndecided) depart.DisabledReason = UxBricks.T(h, "ux.muster.deputy.depart_blocked");
                summary.Actions.Add(depart);
            }
            panel.Cards.Add(summary);
            return panel;
        }

        /// <summary>
        /// Картки «Хто стане на пости»: на кожен пост, що його звільняє загін, —
        /// вибір заступника з вільних або явне «лишити порожнім». Повертає true,
        /// якщо вибір ще обов'язковий (є пост без рішення й лишились вільні).
        /// Стан вибору — <see cref="UxPanelState.MusterDeputies"/>; ядро
        /// перевірить те саме (<c>DispatchResult.SubstituteNotChosen</c>).
        /// </summary>
        private static bool DeputyCards(IUxHost h, UxPanelModel panel, string section, List<string> party, RosterView roster)
        {
            var st = h.PanelState;
            var muster = h.Session.GetMusterView(party);
            if (muster?.Vacancies == null || muster.Vacancies.Count == 0)
            {
                st.MusterDeputies.Clear();
                return false;
            }

            // Застарілі вибори (інша людина вже не вільна, пост не з цього загону) — прибрати.
            foreach (var slot in new List<string>(st.MusterDeputies.Keys))
            {
                bool stillVacant = false;
                foreach (var v in muster.Vacancies) if (v.SlotId == slot) stillVacant = true;
                string who = st.MusterDeputies[slot];
                if (!stillVacant || (!string.IsNullOrEmpty(who) && !ContainsId(muster.FreeIds, who))) st.MusterDeputies.Remove(slot);
            }

            int chosen = 0;
            foreach (var v in st.MusterDeputies.Values) if (!string.IsNullOrEmpty(v)) chosen++;
            bool anyUndecided = false;

            foreach (var vacancy in muster.Vacancies)
            {
                string slotId = vacancy.SlotId;
                var card = new UxCard
                {
                    Section = section,
                    Title = UxBricks.F(h, "ux.muster.deputy.title", "post", UxBricks.T(h, "post." + slotId),
                        "holder", UxBricks.Name(h, vacancy.HolderId, roster))
                };

                string current;
                bool decided = st.MusterDeputies.TryGetValue(slotId, out current);
                if (!decided)
                {
                    if (muster.FreeIds.Count - chosen > 0) { card.Chips.Add(new UxChip(UxBricks.T(h, "ux.muster.deputy.undecided"), UxTone.Bad)); anyUndecided = true; }
                    else card.Lines.Add(UxBricks.T(h, "ux.muster.deputy.nobody"));
                }
                else if (string.IsNullOrEmpty(current))
                    card.Chips.Add(new UxChip(UxBricks.T(h, "ux.muster.deputy.empty_chosen"), UxTone.Neutral));
                else
                    card.Chips.Add(new UxChip(UxBricks.F(h, "ux.muster.deputy.picked", "name", UxBricks.Name(h, current, roster)), UxTone.Own));

                foreach (var candidateId in vacancy.CandidateIds)
                {
                    string cid = candidateId;
                    bool takenElsewhere = false;
                    foreach (var kv in st.MusterDeputies) if (kv.Value == cid && kv.Key != slotId) takenElsewhere = true;
                    var pick = UxBricks.Go("deputy:" + slotId + ":" + cid,
                        UxBricks.F(h, "ux.post.take", "name", UxBricks.Name(h, cid, roster)),
                        () => { st.MusterDeputies[slotId] = cid; });
                    pick.Selected = decided && current == cid;
                    if (takenElsewhere) pick.DisabledReason = UxBricks.T(h, "ux.muster.deputy.taken");
                    card.Actions.Add(pick);
                }
                var none = UxBricks.Go("deputy:" + slotId + ":none", UxBricks.T(h, "ux.muster.deputy.none"),
                    () => { st.MusterDeputies[slotId] = string.Empty; });
                none.Selected = decided && string.IsNullOrEmpty(current);
                card.Actions.Add(none);
                panel.Cards.Add(card);
            }
            return anyUndecided;
        }

        private static bool ContainsId(IReadOnlyList<string> list, string id)
        {
            if (list != null) foreach (var x in list) if (x == id) return true;
            return false;
        }

        private static UxAction Approach(IUxHost h, ExpeditionApproach approach, string key)
        {
            var st = h.PanelState;
            var a = UxBricks.Go("approach:" + approach, UxBricks.T(h, key), () => { st.MusterApproach = approach; st.MusterPreview = null; });
            a.Selected = st.MusterApproach == approach;
            return a;
        }

        private static string ApproachKey(ExpeditionApproach a) =>
            a == ExpeditionApproach.Forceful ? "ui.expedition.approach.forceful"
            : a == ExpeditionApproach.Delve ? "ui.expedition.approach.delve" : "ui.expedition.approach.quiet";

        /// <summary>Схованка Складу: хто що має (зняти) і речі схованки (спорядити людину).</summary>
        public static UxPanelModel Stash(IUxHost h)
        {
            var g = h.Gender;
            var panel = UxCityPanels.New(h, UxPanelId.Stash);
            var roster = h.Session.GetRosterView();
            string people = UxBricks.T(h, "ux.stash.section.people");
            string items = UxBricks.T(h, "ux.stash.section.items");

            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                {
                    if (!CanWear(c)) continue;
                    var sheet = h.Session.GetCharacterSheet(c.Id);
                    if (sheet == null) continue;
                    var card = new UxCard { Section = people, Title = UxBricks.Name(h, c.Id, roster) };
                    Slot(h, card, sheet.CompanionId, EquipSlot.Weapon, sheet.Equipment.WeaponId, "ui.gear.slot.weapon");
                    Slot(h, card, sheet.CompanionId, EquipSlot.Armor, sheet.Equipment.ArmorId, "ui.gear.slot.armor");
                    Slot(h, card, sheet.CompanionId, EquipSlot.Accessory, sheet.Equipment.AccessoryId, "ui.gear.slot.accessory");
                    panel.Cards.Add(card);
                }

            var stash = h.Session.GetStash();
            if (stash == null || stash.Count == 0)
                panel.Cards.Add(new UxCard { Section = items, Title = UxBricks.T(h, "ui.gear.stash.empty"), Subtitle = UxBricks.T(h, "ux.stash.where_from") });
            else
                foreach (var item in stash)
                {
                    var card = ItemCard(h, item, items);
                    string instanceId = item.InstanceId;
                    var slot = item.Slot;
                    if (roster?.Companions != null)
                        foreach (var c in roster.Companions)
                        {
                            if (!CanWear(c)) continue;
                            string companionId = c.Id;
                            card.Actions.Add(UxBricks.Act("equip:" + instanceId + ":" + companionId,
                                UxBricks.F(h, "ux.stash.equip", "name", UxBricks.Name(h, companionId, roster)), UxIntent.Secondary,
                                () => UxBricks.Reported(h, () => h.Session.Equip(companionId, instanceId, slot),
                                    ok => ok ? null : UkrainianText.Get("ux.stash.cannot_equip", g))).Calls(nameof(GameSession.Equip)));
                        }
                    panel.Cards.Add(card);
                }
            return panel;
        }

        private static bool CanWear(CompanionSummary c) =>
            c != null && (c.Loyalty != null || c.Id == GameSession.ProtagonistId) &&
            c.Status != CompanionStatus.Dead && c.Status != CompanionStatus.Antagonist && c.Status != CompanionStatus.NotArrived;

        private static void Slot(IUxHost h, UxCard card, string companionId, EquipSlot slot, string itemId, string slotKey)
        {
            string label = string.IsNullOrEmpty(itemId) ? UxBricks.T(h, "ui.sheet.none") : ItemName(h, itemId);
            card.Chips.Add(new UxChip(UxBricks.T(h, slotKey) + ": " + label, string.IsNullOrEmpty(itemId) ? UxTone.Neutral : UxTone.Own));
            if (string.IsNullOrEmpty(itemId)) return;
            card.Actions.Add(UxBricks.Act("unequip:" + companionId + ":" + slot, UxBricks.F(h, "ux.stash.unequip", "item", label),
                UxIntent.Secondary, () => UxBricks.Run(h, () => h.Session.Unequip(companionId, slot))).Calls(nameof(GameSession.Unequip)));
        }

        private static string ItemName(IUxHost h, string itemId) =>
            UkrainianText.Has("item." + itemId, h.Gender) ? UxBricks.T(h, "item." + itemId) : itemId;

        private static UxCard ItemCard(IUxHost h, ItemInstance item, string section)
        {
            var g = h.Gender;
            var card = new UxCard { Section = section, Title = ItemName(h, item.Definition.Id) };
            card.Chips.Add(new UxChip(UkrainianText.Get("rarity." + item.Rarity.ToString().ToLowerInvariant(), g)));
            card.Lines.Add(UkrainianText.Format("ui.gear.improves", g, "stats", ScreenText.ItemStatSummary(item, g)));
            return card;
        }

        /// <summary>Верстак Майстерні: покращення речі — ціна, до → після, причина відмови.</summary>
        public static UxPanelModel Workbench(IUxHost h)
        {
            var g = h.Gender;
            var panel = UxCityPanels.New(h, UxPanelId.Workbench);
            var economy = h.Session.GetEconomyView();
            bool workshop = UxBricks.IsBuilt(h.Session.GetCityView(), DefaultBuildings.Workshop);
            var balance = new Game.Core.Balance.ItemBalance();
            var stash = h.Session.GetStash();
            if (stash == null || stash.Count == 0) { panel.EmptyText = UxBricks.T(h, "ux.workbench.empty"); return panel; }
            foreach (var item in stash)
            {
                var card = ItemCard(h, item, null);
                string instanceId = item.InstanceId;
                var craft = UxBricks.Act("craft:" + instanceId, UxBricks.T(h, "ui.gear.craft"), UxIntent.Primary,
                    () => UxBricks.Reported(h, () => h.Session.CraftUpgrade(instanceId), r => ScreenText.CraftFailure(r, g))).Calls(nameof(GameSession.CraftUpgrade));
                craft.Chips.Add(new UxChip(UkrainianText.Format("ui.gear.craft_cost", g, "gold", balance.CraftGoldCost.ToString(),
                    "craft", balance.CraftComponentCost.ToString())));
                if (item.Definition.IsNamed) craft.DisabledReason = UxBricks.T(h, "ui.feedback.craft.named_not_upgradable");
                else if (item.Rarity >= Rarity.Epic) craft.DisabledReason = UxBricks.T(h, "ui.feedback.craft.already_max_rarity");
                else
                {
                    string preview = ScreenText.CraftPreviewText(CraftSystem.PreviewUpgrade(item), g);
                    if (!string.IsNullOrEmpty(preview)) card.Lines.Add(preview);
                    if (!workshop) craft.DisabledReason = UxBricks.T(h, "ui.feedback.craft.workshop_closed");
                    else if (economy != null && (economy.Gold < balance.CraftGoldCost || economy.CraftComponent < balance.CraftComponentCost))
                        craft.DisabledReason = UxBricks.T(h, "ui.feedback.craft.cannot_afford");
                }
                card.Actions.Add(craft);
                panel.Cards.Add(card);
            }
            return panel;
        }

        /// <summary>Дошка оголошень: міські квести (зокрема ті, що раніше ніде не пропонувались — «юшка з каменю», «мито на ринку»).</summary>
        public static UxPanelModel NoticeBoard(IUxHost h)
        {
            var panel = UxCityPanels.New(h, UxPanelId.NoticeBoard);
            foreach (var questId in new[] { DefaultQuests.StoneSoupId, DefaultQuests.MarketTollId, DefaultQuests.HafiyaId, DefaultQuests.MaksymCh1Id })
            {
                var offer = h.PanelState.Quests.Get(h.Session, questId);
                if (offer != null) panel.Cards.Add(UxQuestCards.Offer(h, offer));
            }
            if (panel.Cards.Count == 0)
                panel.EmptyText = QuestOfferCache.CanOffer(h.Session.State)
                    ? UxBricks.T(h, "ui.quests.none_active")
                    : UxBricks.T(h, "ux.notice_board.not_now");
            return panel;
        }

        /// <summary>Тренувальний майданчик: бій-пісочниця без наслідків і готовність громади словом (без «N з M»).</summary>
        public static UxPanelModel TrainingGround(IUxHost h)
        {
            var panel = UxCityPanels.New(h, UxPanelId.TrainingGround);
            var card = new UxCard { Title = UxBricks.T(h, "ui.readiness.training"), Subtitle = UxBricks.T(h, "ui.readiness.training.hint") };
            var readiness = h.Session.GetReadinessView();
            if (readiness != null && !string.IsNullOrEmpty(readiness.Band))
                card.Chips.Add(new UxChip(UxBricks.F(h, "ux.readiness.word", "band", ScreenText.ReadinessLabel(readiness.Band, h.Gender))));
            card.Actions.Add(UxBricks.Act("training", UxBricks.T(h, "ux.training.start"), UxIntent.Primary, () =>
                UxBricks.Run(h, () => h.Session.NewTrainingBattle(new TrainingBattleOptions { HitRule = h.Session.HitRule }))).Calls(nameof(GameSession.NewTrainingBattle)));
            panel.Cards.Add(card);
            return panel;
        }

        /// <summary>
        /// Твій намет біля Віча (Поправка №22): розвиток героя — у світі, а не лише
        /// в панелі C. Та сама картка, що в «Люди → Ти» (UX-04: одна дія — одна картка).
        /// </summary>
        public static UxPanelModel HeroTent(IUxHost h)
        {
            var panel = UxCityPanels.New(h, UxPanelId.HeroTent);
            var growth = UxPersonPanel.Growth(h, null);
            growth.Actions.Add(UxBricks.Go("tent:sheet", UxBricks.T(h, "ux.hero_tent.sheet"),
                () => h.OpenPanel(UxPanelId.People, GameSession.ProtagonistId)));
            panel.Cards.Add(growth);
            return panel;
        }

        /// <summary>Журнал механік (F10, тестова збірка): що вже побачено цим прогоном.</summary>
        public static UxPanelModel MechanicsJournal(IUxHost h)
        {
            var g = h.Gender;
            var panel = UxCityPanels.New(h, UxPanelId.MechanicsJournal);
            var journal = h.Session.GetMechanicsJournal();
            int total = journal != null ? journal.Count : 0, seen = 0;
            if (journal != null) foreach (var e in journal) if (e.Seen) seen++;
            panel.Cards.Add(new UxCard { Title = UkrainianText.Format("ui.journal.progress", g, "seen", seen.ToString(), "total", total.ToString()) });
            if (journal != null)
                foreach (var e in journal)
                {
                    var card = new UxCard { Title = UkrainianText.Has(e.TitleKey, g) ? UkrainianText.Get(e.TitleKey, g) : e.TitleKey };
                    card.Chips.Add(new UxChip(UkrainianText.Get(e.Seen ? "ui.journal.seen" : "ui.journal.not_seen", g), e.Seen ? UxTone.Good : UxTone.Neutral));
                    card.Lines.Add(UkrainianText.Has(e.HintKey, g) ? UkrainianText.Get(e.HintKey, g) : e.HintKey);
                    panel.Cards.Add(card);
                }
            return panel;
        }
    }
}
