using System;
using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters.Creation;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Спільні цеглинки панелей: дії з дозволеними станами, стадія будівлі,
    /// виконання команди з кодом відмови. Жодного IMGUI — лише моделі (HP-4).
    /// </summary>
    public static class UxBricks
    {
        /// <summary>Будівлі в порядку креслень: спершу ремесла прибульців (№12.9), далі решта (US-7.1).</summary>
        public static readonly string[] BuildingIds =
        {
            DefaultBuildings.Watch, DefaultBuildings.Storehouse, DefaultBuildings.Infirmary,
            DefaultBuildings.Workshop, DefaultBuildings.Market, DefaultBuildings.CouncilHall,
            DefaultBuildings.Tavern, DefaultBuildings.Temple, DefaultBuildings.Fortifications,
            DefaultBuildings.Armory, DefaultBuildings.Laboratory
        };

        public static readonly string[] PostIds =
        {
            "council_seat", "storehouse_dock", "infirmary_bed", "settlement_market",
            "workshop_bench", "settlement_farms", "scouting_post"
        };

        public static readonly string[] SiteIds = { "outskirts", "old_workshop", "far_highway", "abandoned_camp" };
        public static readonly string[] FactionIds = { "community", "tuhar_boyars", "horde" };

        public static bool Female(IUxHost h) => h.Gender == Gender.Female;

        /// <summary>Дія, дозволена лише вранці й у вільній грі (більшість міських команд ядра).</summary>
        public static UxAction Act(string id, string label, UxIntent intent, Func<UxOutcome> execute)
        {
            var a = new UxAction { Id = id, Label = label, Intent = intent, Execute = execute };
            a.AllowedStates.Add(SessionState.Morning);
            a.AllowedStates.Add(SessionState.FreePlay);
            return a;
        }

        /// <summary>Дія без обмеження стану (перехід до іншої панелі чи місця).</summary>
        public static UxAction Go(string id, string label, Action execute)
        {
            return new UxAction
            {
                Id = id, Label = label, Intent = UxIntent.Secondary,
                Execute = () => { execute(); return UxOutcome.Success(); }
            };
        }

        public static UxOutcome Run(IUxHost h, Action command) => UxCommandRunner.Run(command, Female(h));

        public static UxOutcome Reported<T>(IUxHost h, Func<T> command, Func<T, string> failure)
        {
            T ignored;
            return UxCommandRunner.Run(command, failure, Female(h), out ignored);
        }

        /// <summary>Стадія будівлі 0..5 (5 — зведена).</summary>
        public static int Stage(CityView city, string id, out bool built)
        {
            built = false;
            if (city?.Built != null)
                foreach (var b in city.Built)
                    if (b.Id == id) { built = true; return 5; }
            if (city?.InProgress != null)
                foreach (var b in city.InProgress)
                    if (b.Id == id) return b.StageOf;
            return 0;
        }

        public static bool IsBuilt(CityView city, string id)
        {
            bool built;
            Stage(city, id, out built);
            return built;
        }

        public static string Name(IUxHost h, string companionId, RosterView roster) =>
            ScreenText.ResolveCompanionName(companionId, h.Gender, roster);

        public static string T(IUxHost h, string key) => UkrainianText.Get(key, h.Gender);

        public static string F(IUxHost h, string key, params string[] args) => UkrainianText.Format(key, h.Gender, args);
    }

    /// <summary>
    /// Картки міста: пост, ділянка, будівля (docs/UX_DESIGN.md §4.4, §4.8).
    /// Одна картка — одна дія (UX-04): та сама картка поста стоїть на
    /// станції в будівлі і на Дошці наряду.
    /// </summary>
    public static class UxCityCards
    {
        /// <summary>Картка поста: хто на ньому, «Зняти», кандидати «Поставити: {ім'я}» або причина, чому пост закритий.</summary>
        public static UxCard Post(IUxHost h, string postId, RosterView roster, CityView city)
        {
            var card = new UxCard { Title = UxBricks.T(h, "post." + postId) };
            string occupant = Walk.VillagePlaces.OccupantOf(roster, postId);
            bool open = city?.OpenPosts == null || Contains(city.OpenPosts, postId);
            if (occupant != null)
            {
                card.Chips.Add(new UxChip(UxBricks.Name(h, occupant, roster), UxTone.Own));
                card.Actions.Add(UxBricks.Act("unassign:" + postId, UxBricks.T(h, "ui.posts.unassign"), UxIntent.Secondary,
                    () => UxBricks.Run(h, () => h.Session.Unassign(postId))));
                return card;
            }
            if (!open)
            {
                card.Chips.Add(new UxChip(UxBricks.T(h, "ux.post.closed"), UxTone.Neutral));
                card.Lines.Add(ScreenText.PostLockedReason(postId, h.Gender));
                return card;
            }
            card.Chips.Add(new UxChip(UxBricks.T(h, "ux.post.empty"), UxTone.Bad));
            int candidates = 0;
            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                {
                    if (!string.IsNullOrEmpty(c.AssignedSlotId)) continue;
                    if (!ScreenText.AssignCandidateLegality(c).Enabled) continue;
                    string companionId = c.Id;
                    candidates++;
                    card.Actions.Add(UxBricks.Act("assign:" + postId + ":" + companionId,
                        UxBricks.F(h, "ux.post.take", "name", UxBricks.Name(h, companionId, roster)), UxIntent.Secondary,
                        () => UxBricks.Reported(h, () => h.Session.Assign(companionId, postId), r => ScreenText.AssignFailure(r, h.Gender))));
                }
            if (candidates == 0) card.Lines.Add(UxBricks.T(h, "ux.post.none_free"));
            return card;
        }

        /// <summary>
        /// Картка ділянки чи будівлі: ціна, строк, що дасть, стадії й «Замовити»
        /// з причиною недоступності поруч (UI-02, UI-03). Картка з ціною — сама
        /// собі підтвердження (UX-12).
        /// </summary>
        public static UxCard Plot(IUxHost h, string buildingId, CityView city, EconomyView economy, bool withLink)
        {
            var g = h.Gender;
            var def = DefaultBuildings.Get(buildingId);
            var card = new UxCard { Title = UxBricks.T(h, "building." + buildingId) };
            if (UkrainianText.Has("building." + buildingId + ".effect", g))
                card.Subtitle = UkrainianText.Get("building." + buildingId + ".effect", g);

            bool built;
            int stage = UxBricks.Stage(city, buildingId, out built);
            if (built)
                card.Chips.Add(new UxChip(UxBricks.T(h, "ui.buildings.built"), UxTone.Good));
            else if (stage > 0)
            {
                card.Chips.Add(new UxChip(UxBricks.F(h, "ux.plot.stage", "stage", stage.ToString()), UxTone.Own));
                card.PipsFilled = stage;
                card.PipsTotal = 5;
            }
            else if (def != null)
                card.Chips.Add(new UxChip(ScreenText.BuildingCostLine(def, g, city != null && city.TestBuildOneDayConstruction)));

            if (withLink)
                card.LinkPlaceId = (built ? Walk.VillagePlaces.BuildingPrefix : Walk.VillagePlaces.PlotPrefix) + buildingId;

            if (!built && stage == 0 && def != null)
            {
                var order = UxBricks.Act("order:" + buildingId, UxBricks.T(h, "ui.buildings.order"), UxIntent.Primary,
                    () => UxBricks.Reported(h, () => h.Session.OrderBuilding(buildingId), r => ScreenText.BuildFailure(r, g)));
                bool enoughGold = economy == null || economy.Gold >= def.GoldCost;
                bool enoughBuild = economy == null || economy.BuildComponent >= def.BuildComponentCost;
                if (def.QuestOnly) order.DisabledReason = UxBricks.T(h, "ui.feedback.build.quest_only");
                else if (!enoughGold) order.DisabledReason = UxBricks.T(h, "ui.feedback.build.not_enough_gold");
                else if (!enoughBuild) order.DisabledReason = UxBricks.T(h, "ui.feedback.build.not_enough_build_component");
                card.Actions.Add(order);
            }
            return card;
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == id) return true;
            return false;
        }
    }

    /// <summary>Ділянка, будівля без входу, креслення всіх ділянок, дошка наряду.</summary>
    public static class UxCityPanels
    {
        public static UxPanelModel PlotCard(IUxHost h, string buildingId)
        {
            var panel = New(h, UxPanelId.PlotCard);
            panel.Title = UxBricks.F(h, "ux.place.plot", "building", UxBricks.T(h, "building." + buildingId));
            panel.Cards.Add(UxCityCards.Plot(h, buildingId, h.Session.GetCityView(), h.Session.GetEconomyView(), false));
            return panel;
        }

        /// <summary>Зведена будівля без входу (Укріплення, Збройня, Лабораторія): чесна картка, без мертвого входу (UX-10).</summary>
        public static UxPanelModel BuildingCard(IUxHost h, string buildingId)
        {
            var panel = New(h, UxPanelId.BuildingCard);
            panel.Title = UxBricks.T(h, "building." + buildingId);
            var card = UxCityCards.Plot(h, buildingId, h.Session.GetCityView(), h.Session.GetEconomyView(), false);
            if (buildingId == DefaultBuildings.Fortifications)
            {
                var readiness = h.Session.GetReadinessView();
                if (readiness != null && !string.IsNullOrEmpty(readiness.Band))
                    card.Chips.Add(new UxChip(UxBricks.F(h, "ux.readiness.word", "band", ScreenText.ReadinessLabel(readiness.Band, h.Gender))));
            }
            card.Lines.Add(UxBricks.T(h, "ux.building.no_entry"));
            panel.Cards.Add(card);
            return panel;
        }

        /// <summary>Креслення (на Вічі): усі ділянки — огляд без ходьби, «Показати в селі» веде до місця.</summary>
        public static UxPanelModel Blueprints(IUxHost h)
        {
            var panel = New(h, UxPanelId.Blueprints);
            var city = h.Session.GetCityView();
            var economy = h.Session.GetEconomyView();
            string buildable = UxBricks.T(h, "ux.blueprints.section.plots");
            string done = UxBricks.T(h, "ux.blueprints.section.built");
            // Спершу те, що можна будувати, — підвкладка «Ділянки» відкривається першою.
            var built = new List<UxCard>();
            foreach (var id in UxBricks.BuildingIds)
            {
                var card = UxCityCards.Plot(h, id, city, economy, true);
                if (UxBricks.IsBuilt(city, id)) { card.Section = done; built.Add(card); }
                else { card.Section = buildable; panel.Cards.Add(card); }
            }
            panel.Cards.AddRange(built);
            return panel;
        }

        /// <summary>Дошка наряду (N): кожен пост — картка з кандидатами. Та сама картка, що й на станції в будівлі (UX-04).</summary>
        public static UxPanelModel DutyBoard(IUxHost h)
        {
            var panel = New(h, UxPanelId.DutyBoard);
            var roster = h.Session.GetRosterView();
            var city = h.Session.GetCityView();
            foreach (var postId in UxBricks.PostIds)
            {
                var card = UxCityCards.Post(h, postId, roster, city);
                var station = Walk.BuildingCatalog.StationOfPost(postId);
                var building = station != null ? Walk.BuildingCatalog.BuildingOfStation(station.Id) : null;
                if (building != null && UxBricks.IsBuilt(city, building.BuildingId))
                    card.LinkPlaceId = Walk.VillagePlaces.BuildingPrefix + building.BuildingId;
                else if (station != null && building == null)
                    card.LinkPlaceId = Walk.VillagePlaces.StationPrefix + station.Id;
                panel.Cards.Add(card);
            }
            return panel;
        }

        internal static UxPanelModel New(IUxHost h, UxPanelId id)
        {
            return new UxPanelModel { Id = id, Title = UxBricks.T(h, UxPanelCatalog.Get(id).TitleKey) };
        }
    }

    /// <summary>
    /// Віче просто неба (Поправка №12.9): радник, облава, прибульці, підготовка
    /// до загрози, креслення; і Стіл ради в Залі — указ, посольство,
    /// вкладення, спорядження загону. Полонені й «наші в полоні» малює
    /// оболонка під картками Віча (їхні панелі — з треку бою).
    /// </summary>
    public static class UxCouncilPanels
    {
        private static readonly Game.Core.Balance.CityBalance City = new Game.Core.Balance.CityBalance();
        private static readonly Game.Core.Balance.FactionBalance Faction = new Game.Core.Balance.FactionBalance();

        public static UxPanelModel Veche(IUxHost h)
        {
            var g = h.Gender;
            var panel = UxCityPanels.New(h, UxPanelId.Veche);
            var city = h.Session.GetCityView();
            var economy = h.Session.GetEconomyView();
            var roster = h.Session.GetRosterView();

            var seat = UxCityCards.Post(h, "council_seat", roster, city);
            seat.Subtitle = UxBricks.T(h, "ux.veche.seat");
            panel.Cards.Add(seat);

            var raid = CostCard(h, "ui.council.raid", "gold", City.RaidGoldCost.ToString());
            var raidAct = UxBricks.Act("raid", UxBricks.T(h, "ui.council.raid"), UxIntent.Primary,
                () => UxBricks.Reported(h, () => h.Session.OrderRaid(), r => ScreenText.CouncilFailure(r, g)));
            if (city != null && !city.RaidReady) raidAct.DisabledReason = UxBricks.T(h, "ui.council.result.on_cooldown");
            raid.Actions.Add(raidAct);
            panel.Cards.Add(raid);

            var settlers = CostCard(h, "ui.council.settlers", "food", City.SettlersFoodCost.ToString());
            var settlersAct = UxBricks.Act("settlers", UxBricks.T(h, "ui.council.settlers"), UxIntent.Secondary,
                () => UxBricks.Reported(h, () => h.Session.OrderSettlers(), r => ScreenText.CouncilFailure(r, g)));
            if (city != null && !city.SettlersReady) settlersAct.DisabledReason = UxBricks.T(h, "ui.council.result.on_cooldown");
            settlers.Actions.Add(settlersAct);
            panel.Cards.Add(settlers);

            var prepare = CostCard(h, "ui.council.prepare_threat", "gold", Faction.PrepareThreatGoldCost.ToString());
            var prepareAct = UxBricks.Act("prepare_threat", UxBricks.T(h, "ui.council.prepare_threat"), UxIntent.Secondary,
                () => UxBricks.Reported(h, () => h.Session.OrderPrepareThreat(), r => ScreenText.CouncilFailure(r, g)));
            if (economy != null && economy.Gold < Faction.PrepareThreatGoldCost)
                prepareAct.DisabledReason = UxBricks.T(h, "ui.council.result.not_enough_gold");
            prepare.Actions.Add(prepareAct);
            panel.Cards.Add(prepare);

            var plans = new UxCard { Title = UxBricks.T(h, "ux.panel.blueprints"), Subtitle = UxBricks.T(h, "ux.veche.blueprints") };
            plans.Actions.Add(UxBricks.Go("open:blueprints", UxBricks.T(h, "ux.veche.open_blueprints"),
                () => h.OpenPanel(UxPanelId.Blueprints, null)));
            panel.Cards.Add(plans);

            if (!UxBricks.IsBuilt(city, DefaultBuildings.CouncilHall))
            {
                var hall = new UxCard { Title = UxBricks.T(h, "building.council_hall"), Subtitle = UxBricks.T(h, "ui.council.no_hall") };
                panel.Cards.Add(hall);
            }
            return panel;
        }

        /// <summary>Стіл ради в Залі: чотири підвкладки — указ, посольство, вкладення, спорядження загону.</summary>
        public static UxPanelModel CouncilTable(IUxHost h)
        {
            var g = h.Gender;
            var panel = UxCityPanels.New(h, UxPanelId.CouncilTable);
            var city = h.Session.GetCityView();
            var economy = h.Session.GetEconomyView();
            var factions = h.Session.GetFactionsView();
            int gold = economy != null ? economy.Gold : int.MaxValue;

            string decree = UxBricks.T(h, "ui.council.decree");
            string diplomacy = UxBricks.T(h, "ui.council.diplomacy");
            string investment = UxBricks.T(h, "ui.council.investment");
            string outfit = UxBricks.T(h, "ui.council.outfit_expedition");

            var decreeCard = CostCard(h, "ui.council.decree", "gold", Faction.DecreeGoldCost.ToString());
            decreeCard.Section = decree;
            foreach (var fid in UxBricks.FactionIds)
            {
                string factionId = fid;
                var a = UxBricks.Act("decree:" + fid, UxBricks.T(h, "faction." + fid), UxIntent.Secondary,
                    () => UxBricks.Reported(h, () => h.Session.OrderDecree(factionId), r => ScreenText.CouncilFailure(r, g)));
                if (gold < Faction.DecreeGoldCost) a.DisabledReason = UxBricks.T(h, "ui.council.result.not_enough_gold");
                decreeCard.Actions.Add(a);
            }
            panel.Cards.Add(decreeCard);

            var diplomacyCard = CostCard(h, "ui.council.diplomacy", "gold", Faction.DiplomacyGoldCost.ToString());
            diplomacyCard.Section = diplomacy;
            foreach (var fid in UxBricks.FactionIds)
            {
                string factionId = fid;
                var a = UxBricks.Act("diplomacy:" + fid, UxBricks.T(h, "faction." + fid), UxIntent.Secondary,
                    () => UxBricks.Reported(h, () => h.Session.OrderDiplomacy(factionId), r => ScreenText.CouncilFailure(r, g)));
                if (IsHostile(factions, fid)) a.DisabledReason = UxBricks.T(h, "ui.council.result.standing_too_low");
                else if (gold < Faction.DiplomacyGoldCost) a.DisabledReason = UxBricks.T(h, "ui.council.result.not_enough_gold");
                diplomacyCard.Actions.Add(a);
            }
            panel.Cards.Add(diplomacyCard);

            var investCard = CostCard(h, "ui.council.investment", "gold", Faction.InvestmentGoldCost.ToString());
            investCard.Section = investment;
            foreach (var id in UxBricks.BuildingIds)
            {
                bool built;
                if (UxBricks.Stage(city, id, out built) == 0 && !built) continue;
                string buildingId = id;
                var a = UxBricks.Act("invest:" + id, UxBricks.T(h, "building." + id), UxIntent.Secondary,
                    () => UxBricks.Reported(h, () => h.Session.OrderInvestment(buildingId), r => ScreenText.CouncilFailure(r, g)));
                if (gold < Faction.InvestmentGoldCost) a.DisabledReason = UxBricks.T(h, "ui.council.result.not_enough_gold");
                investCard.Actions.Add(a);
            }
            if (investCard.Actions.Count == 0) investCard.Lines.Add(UxBricks.T(h, "ux.council.nothing_built"));
            panel.Cards.Add(investCard);

            var outfitCard = CostCard(h, "ui.council.outfit_expedition", "gold", Faction.OutfitExpeditionGoldCost.ToString());
            outfitCard.Section = outfit;
            foreach (var site in UxBricks.SiteIds)
            {
                string siteId = site;
                var a = UxBricks.Act("outfit:" + site, UxBricks.T(h, "site." + site), UxIntent.Secondary,
                    () => UxBricks.Reported(h, () => h.Session.OrderOutfitExpedition(siteId), r => ScreenText.CouncilFailure(r, g)));
                if (gold < Faction.OutfitExpeditionGoldCost) a.DisabledReason = UxBricks.T(h, "ui.council.result.not_enough_gold");
                outfitCard.Actions.Add(a);
            }
            panel.Cards.Add(outfitCard);
            return panel;
        }

        /// <summary>«Ціна: … — наслідок» однією карткою над кнопками дії (UI-02).</summary>
        private static UxCard CostCard(IUxHost h, string actionKey, string costArg, string costValue)
        {
            var card = new UxCard { Title = UxBricks.T(h, actionKey) };
            card.Chips.Add(new UxChip(UxBricks.F(h, actionKey + ".cost", costArg, costValue)));
            card.Lines.Add(UxBricks.T(h, actionKey + ".effect"));
            return card;
        }

        private static bool IsHostile(FactionsView view, string factionId)
        {
            if (view?.Factions == null) return false;
            foreach (var f in view.Factions)
                if (string.Equals(f.Id, factionId, StringComparison.Ordinal))
                    return string.Equals(f.Band, "Hostile", StringComparison.Ordinal);
            return false;
        }
    }
}
