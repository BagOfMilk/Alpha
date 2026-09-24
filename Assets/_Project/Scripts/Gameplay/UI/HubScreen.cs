using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters.Build;
using Game.Core.Characters.Creation;
using Game.Core.Expeditions;
using Game.Core.Items;
using Game.Core.Quests;
using Game.Core.Session.Views;
using Game.Core.Stats;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Ранковий/денний хаб (§ "Screens" пакета E1b): дев'ять вкладок + «Почати
    /// день». Кожна вкладка читає лише GameSession View-шар/команди —
    /// жодного типу Game.Core.Combat/Dungeons/Base-внутрішнього стану.
    /// </summary>
    public sealed class HubScreen
    {
        private static readonly string[] PostIds =
        {
            "council_seat", "storehouse_dock", "infirmary_bed",
            "settlement_market", "settlement_farms", "workshop_bench", "scouting_post"
        };

        private static readonly string[] BuildingIds =
        {
            DefaultBuildings.Infirmary, DefaultBuildings.Workshop, DefaultBuildings.Storehouse,
            DefaultBuildings.CouncilHall, DefaultBuildings.Market, DefaultBuildings.Tavern,
            DefaultBuildings.Temple, DefaultBuildings.Fortifications, DefaultBuildings.Armory,
            DefaultBuildings.Laboratory
        };

        private static readonly string[] SiteIds = { "outskirts", "old_workshop", "far_highway", "abandoned_camp" };
        private static readonly string[] FactionIds = { "community", "tuhar_boyars", "horde" };

        private int _tab;

        /// <summary>Фаза F (UI-tour autoplay): дозволяє <c>GameShell.SetHubTab</c> перемкнути вкладку ззовні, щоб дим-тест міг зняти скріншот кожної.</summary>
        public void SetTab(int tab) => _tab = tab < 0 ? 0 : (tab > 9 ? 9 : tab);

        // Expedition
        private string _siteId = SiteIds[0];
        private ExpeditionApproach _approach = ExpeditionApproach.Quiet;
        private readonly HashSet<string> _party = new HashSet<string>();
        private ExpeditionPreviewView _preview;

        // Build planner
        private BuildPlan _plan = new BuildPlan();

        // Скрол-позиції довгих списків — persist між кадрами, інакше кожен
        // кадр обнуляв би прокрутку (Widgets.ScrollListBegin повертає нову
        // позицію, її треба тримати як стан екрана, не як Vector2.zero).
        private Vector2 _postsScroll;
        private Vector2 _buildingsScroll;
        private Vector2 _peopleListScroll;
        private Vector2 _sheetScroll;

        /// <summary>
        /// Полірування (ціль 1 «Картка персонажа»): хто обраний у лівому
        /// списку вкладки Люди — persist між кадрами, як і скрол-позиції
        /// вище. null до першого малювання → DrawPeople підставляє
        /// протагоніста (завжди є в ростері).
        /// </summary>
        private string _selectedCompanionId;

        // Кеш пропозиції квесту (фікс-ревью, блокер): OfferQuestStage сам
        // логує "quest.offered" на КОЖЕН виклик (GameSession.cs:593-620), а
        // OnGUI малює DrawQuests кілька разів за кадр і кожен кадр, доки
        // гравець стоїть на вкладці — без кешу за секунди стрічка топилась у
        // сотнях дублів "Нова пропозиція: ...". Перезапит лише коли доба
        // змінилась, або явно скинуто після ResolveQuestChoice.
        private QuestOfferView _questOffer;
        private int _questOfferDay = int.MinValue;

        public void Draw(GameShell shell)
        {
            var g = shell.ProtagonistGender;

            DrawTabBar(g);
            GUILayout.Space(6f);

            switch (_tab)
            {
                case 0: DrawPosts(shell, g); break;
                case 1: DrawBuildings(shell, g); break;
                case 2: DrawCouncil(shell, g); break;
                case 3: DrawExpedition(shell, g); break;
                case 4: DrawGear(shell, g); break;
                case 5: DrawPeople(shell, g); break;
                case 6: DrawQuests(shell, g); break;
                case 7: DrawFactions(shell, g); break;
                case 8: DrawReadiness(shell, g); break;
                case 9: DrawSave(shell, g); break;
            }

            GUILayout.FlexibleSpace();
            if (Widgets.PrimaryButton(UkrainianText.Get("ui.start_day", g)))
                shell.TryRun(() => shell.Session.ConfirmMorning());
        }

        private void DrawTabBar(Gender g)
        {
            string[] keys =
            {
                "ui.tab.posts", "ui.tab.buildings", "ui.tab.council", "ui.tab.expedition",
                "ui.tab.gear", "ui.tab.people", "ui.tab.quests", "ui.tab.factions",
                "ui.tab.readiness", "ui.tab.save"
            };

            GUILayout.BeginHorizontal();
            for (int i = 0; i < keys.Length; i++)
            {
                if (Widgets.TabButton(UkrainianText.Get(keys[i], g), _tab == i))
                    _tab = i;
            }
            GUILayout.EndHorizontal();
        }

        // ===================== Пости =====================

        private void DrawPosts(GameShell shell, Gender g)
        {
            var roster = shell.Session.GetRosterView();
            _postsScroll = Widgets.ScrollListBegin(_postsScroll, GUILayout.ExpandHeight(true));
            foreach (var postId in PostIds)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(UkrainianText.Get("post." + postId, g), AlphaSkin.Body, GUILayout.Width(220f));

                string occupant = FindOccupant(roster, postId);
                if (occupant != null)
                {
                    GUILayout.Label(ScreenText.ResolveCompanionName(occupant, g, roster), AlphaSkin.Body, GUILayout.ExpandWidth(true));
                    if (Widgets.SecondaryButton(UkrainianText.Get("ui.posts.unassign", g), GUILayout.Width(160f)))
                        shell.TryRun(() => shell.Session.Unassign(postId));
                }
                else
                {
                    GUILayout.Label(UkrainianText.Get("post.empty", g), AlphaSkin.Tooltip, GUILayout.ExpandWidth(true));
                }
                GUILayout.EndHorizontal();

                if (roster?.Companions != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(24f);
                    foreach (var c in roster.Companions)
                    {
                        if (c.Id == occupant || !string.IsNullOrEmpty(c.AssignedSlotId)) continue;
                        var legality = ScreenText.AssignCandidateLegality(c);
                        if (!legality.Enabled) continue;
                        string slot = postId;
                        string companionId = c.Id;
                        if (Widgets.SecondaryButton(UkrainianText.Get("ui.posts.assign", g) + ": " + ScreenText.ResolveCompanionName(companionId, g, roster), GUILayout.Width(280f)))
                            shell.TryRun(() => shell.Session.Assign(companionId, slot));
                    }
                    GUILayout.EndHorizontal();
                }
            }
            Widgets.ScrollListEnd();
        }

        private static string FindOccupant(RosterView roster, string postId)
        {
            if (roster?.Companions == null) return null;
            foreach (var c in roster.Companions)
                if (c.AssignedSlotId == postId) return c.Id;
            return null;
        }

        // ===================== Будівлі =====================

        /// <summary>
        /// Полірування (ціль 2 «Прозорість дій», owner: "every action button
        /// ... shows its cost, time/stages ... and a one-line effect in
        /// words ... disabled-with-reason"). Раніше картка показувала лише
        /// назву й стан — ціна й ефект дізнавались тільки постфактум, із
        /// рядка фідбеку після кліку.
        /// </summary>
        private void DrawBuildings(GameShell shell, Gender g)
        {
            var city = shell.Session.GetCityView();
            var economy = shell.Session.GetEconomyView();
            _buildingsScroll = Widgets.ScrollListBegin(_buildingsScroll, GUILayout.ExpandHeight(true));
            foreach (var id in BuildingIds)
            {
                var def = Game.Core.Base.DefaultBuildings.Get(id);
                GUILayout.BeginVertical(GUI.skin.box);

                GUILayout.BeginHorizontal();
                GUILayout.Label(UkrainianText.Get("building." + id, g), AlphaSkin.SubHeader, GUILayout.Width(220f));
                if (def != null) GUILayout.Label(ScreenText.BuildingCostLine(def, g), AlphaSkin.Body, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                if (UkrainianText.Has("building." + id + ".effect", g))
                    GUILayout.Label(UkrainianText.Get("building." + id + ".effect", g), AlphaSkin.Tooltip);

                GUILayout.BeginHorizontal();
                int stage = StageOf(city, id, out bool built);
                if (built)
                    GUILayout.Label(UkrainianText.Get("ui.buildings.built", g), AlphaSkin.Body, GUILayout.ExpandWidth(true));
                else if (stage > 0)
                    GUILayout.Label(UkrainianText.Format("ui.buildings.in_progress", g, "stage", stage.ToString()), AlphaSkin.Body, GUILayout.ExpandWidth(true));
                else
                    GUILayout.Label(UkrainianText.Get("ui.common.empty", g), AlphaSkin.Tooltip, GUILayout.ExpandWidth(true));

                if (!built && stage == 0 && def != null)
                {
                    string buildingId = id;
                    bool enoughGold = economy == null || economy.Gold >= def.GoldCost;
                    bool enoughMaterials = economy == null || economy.Materials >= def.MaterialsCost;
                    if (def.QuestOnly)
                        Widgets.DisabledButton(UkrainianText.Get("ui.buildings.order", g), UkrainianText.Get("ui.feedback.build.quest_only", g), GUILayout.Width(160f));
                    else if (enoughGold && enoughMaterials)
                    {
                        if (Widgets.PrimaryButton(UkrainianText.Get("ui.buildings.order", g), GUILayout.Width(160f)))
                            shell.TryRun(() => shell.Session.OrderBuilding(buildingId));
                    }
                    else
                    {
                        string reasonKey = !enoughGold ? "ui.feedback.build.not_enough_gold" : "ui.feedback.build.not_enough_materials";
                        Widgets.DisabledButton(UkrainianText.Get("ui.buildings.order", g), UkrainianText.Get(reasonKey, g), GUILayout.Width(160f));
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            Widgets.ScrollListEnd();
        }

        private static int StageOf(CityView city, string id, out bool built)
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

        // ===================== Рада =====================

        private static readonly Game.Core.Balance.CityBalance CouncilCity = new Game.Core.Balance.CityBalance();
        private static readonly Game.Core.Balance.FactionBalance CouncilFaction = new Game.Core.Balance.FactionBalance();

        /// <summary>
        /// Полірування (ціль 2 «Прозорість дій», owner: "every action button
        /// ... shows its cost ... and a one-line effect in words ...
        /// disabled-with-reason"). Раніше кожна дія ради показувала лише
        /// назву — ціна й наслідок дізнавались тільки постфактум, із рядка
        /// фідбеку після кліку. Відкат (скільки діб лишилось) тут і раніше
        /// не показувався — CityWorks тримає його internal-полями без
        /// публічного геттера; це відома межа цього проходу, не приховано.
        /// </summary>
        private void DrawCouncil(GameShell shell, Gender g)
        {
            var city = shell.Session.GetCityView();
            var economy = shell.Session.GetEconomyView();

            DrawCouncilCostEffect("ui.council.raid", g, "gold", CouncilCity.RaidGoldCost.ToString());
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(UkrainianText.Get("ui.council.raid", g), AlphaSkin.Body, GUILayout.ExpandWidth(true));
            if (city != null && city.RaidReady)
            {
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.council.raid", g)))
                    shell.TryRun(() => shell.Session.OrderRaid());
            }
            else Widgets.DisabledButton(UkrainianText.Get("ui.council.raid", g), UkrainianText.Get("ui.council.result.on_cooldown", g));
            GUILayout.EndHorizontal();

            DrawCouncilCostEffect("ui.council.settlers", g, "food", CouncilCity.SettlersFoodCost.ToString());
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(UkrainianText.Get("ui.council.settlers", g), AlphaSkin.Body, GUILayout.ExpandWidth(true));
            if (city != null && city.SettlersReady)
            {
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.council.settlers", g)))
                    shell.TryRun(() => shell.Session.OrderSettlers());
            }
            else Widgets.DisabledButton(UkrainianText.Get("ui.council.settlers", g), UkrainianText.Get("ui.council.result.on_cooldown", g));
            GUILayout.EndHorizontal();

            // Фікс-ревью (minor): та сама обрізка, що вище — "Громада
            // Тухольщини" (найдовша назва фракції) не влазила у фіксовані
            // 180px і читалась як "ромада Тухольщин" з обох країв.
            Widgets.Section(UkrainianText.Get("ui.council.decree", g), () =>
            {
                DrawCouncilCostEffect("ui.council.decree", g, "gold", CouncilFaction.DecreeGoldCost.ToString());
                GUILayout.BeginHorizontal();
                foreach (var factionId in FactionIds)
                    DrawCouncilFactionButton(shell, g, factionId, economy, CouncilFaction.DecreeGoldCost, fid => shell.Session.OrderDecree(fid));
                GUILayout.EndHorizontal();
            });

            Widgets.Section(UkrainianText.Get("ui.council.diplomacy", g), () =>
            {
                DrawCouncilCostEffect("ui.council.diplomacy", g, "gold", CouncilFaction.DiplomacyGoldCost.ToString());
                GUILayout.BeginHorizontal();
                foreach (var factionId in FactionIds)
                    DrawCouncilFactionButton(shell, g, factionId, economy, CouncilFaction.DiplomacyGoldCost, fid => shell.Session.OrderDiplomacy(fid));
                GUILayout.EndHorizontal();
            });

            Widgets.Section(UkrainianText.Get("ui.council.investment", g), () =>
            {
                DrawCouncilCostEffect("ui.council.investment", g, "gold", CouncilFaction.InvestmentGoldCost.ToString());
                GUILayout.BeginHorizontal();
                foreach (var id in BuildingIds)
                {
                    if (StageOf(city, id, out bool built) == 0 && !built) continue;
                    string buildingId = id;
                    bool canAfford = economy == null || economy.Gold >= CouncilFaction.InvestmentGoldCost;
                    if (canAfford)
                    {
                        if (Widgets.SecondaryButton(UkrainianText.Get("building." + id, g)))
                            shell.TryRun(() => shell.Session.OrderInvestment(buildingId));
                    }
                    else Widgets.DisabledButton(UkrainianText.Get("building." + id, g), UkrainianText.Get("ui.council.result.not_enough_gold", g));
                }
                GUILayout.EndHorizontal();
            });

            DrawCouncilCostEffect("ui.council.prepare_threat", g, "gold", CouncilFaction.PrepareThreatGoldCost.ToString());
            if (economy == null || economy.Gold >= CouncilFaction.PrepareThreatGoldCost)
            {
                if (Widgets.SecondaryButton(UkrainianText.Get("ui.council.prepare_threat", g)))
                    shell.TryRun(() => shell.Session.OrderPrepareThreat());
            }
            else Widgets.DisabledButton(UkrainianText.Get("ui.council.prepare_threat", g), UkrainianText.Get("ui.council.result.not_enough_gold", g));

            Widgets.Section(UkrainianText.Get("ui.council.outfit_expedition", g), () =>
            {
                DrawCouncilCostEffect("ui.council.outfit_expedition", g, "gold", CouncilFaction.OutfitExpeditionGoldCost.ToString());
                GUILayout.BeginHorizontal();
                foreach (var site in SiteIds)
                {
                    string sid = site;
                    bool canAfford = economy == null || economy.Gold >= CouncilFaction.OutfitExpeditionGoldCost;
                    // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): фіксовані
                    // 180px замалі для довших назв ("Покинутий табір
                    // авангарду") — AlphaSkin.ButtonStyle.wordWrap=false, тож
                    // текст не переносився, а виїжджав ЗА межі кнопки й
                    // налягав на сусідню. Без явної ширини GUILayout сам
                    // підбирає розмір під напис.
                    if (canAfford)
                    {
                        if (Widgets.SecondaryButton(UkrainianText.Get("site." + site, g)))
                            shell.TryRun(() => shell.Session.OrderOutfitExpedition(sid));
                    }
                    else Widgets.DisabledButton(UkrainianText.Get("site." + site, g), UkrainianText.Get("ui.council.result.not_enough_gold", g));
                }
                GUILayout.EndHorizontal();
            });
        }

        /// <summary>«Ціна: N золота — рухає уклад, −Напруга» — один рядок над кожною групою кнопок ради.</summary>
        private static void DrawCouncilCostEffect(string actionKey, Gender g, string costArgName, string costArgValue)
        {
            string cost = UkrainianText.Format(actionKey + ".cost", g, costArgName, costArgValue);
            string effect = UkrainianText.Get(actionKey + ".effect", g);
            GUILayout.Label(cost + " — " + effect, AlphaSkin.Tooltip);
        }

        private static void DrawCouncilFactionButton(GameShell shell, Gender g, string factionId, EconomyView economy, int goldCost, System.Action<string> order)
        {
            bool canAfford = economy == null || economy.Gold >= goldCost;
            if (canAfford)
            {
                if (Widgets.SecondaryButton(UkrainianText.Get("faction." + factionId, g)))
                    shell.TryRun(() => order(factionId));
            }
            else Widgets.DisabledButton(UkrainianText.Get("faction." + factionId, g), UkrainianText.Get("ui.council.result.not_enough_gold", g));
        }

        // ===================== Вилазка =====================

        private void DrawExpedition(GameShell shell, Gender g)
        {
            var roster = shell.Session.GetRosterView();

            Widgets.Section(UkrainianText.Get("ui.tab.expedition", g), () =>
            {
                GUILayout.BeginHorizontal();
                foreach (var site in SiteIds)
                {
                    // Той самий фікс ширини, що DrawCouncil вище.
                    if (Widgets.TabButton(UkrainianText.Get("site." + site, g), _siteId == site))
                    {
                        _siteId = site;
                        _preview = null;
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (Widgets.TabButton(UkrainianText.Get("ui.expedition.approach.quiet", g), _approach == ExpeditionApproach.Quiet, GUILayout.Width(150f)))
                    SetApproach(ExpeditionApproach.Quiet);
                if (Widgets.TabButton(UkrainianText.Get("ui.expedition.approach.forceful", g), _approach == ExpeditionApproach.Forceful, GUILayout.Width(150f)))
                    SetApproach(ExpeditionApproach.Forceful);
                if (_siteId == "abandoned_camp" &&
                    Widgets.TabButton(UkrainianText.Get("ui.expedition.approach.delve", g), _approach == ExpeditionApproach.Delve, GUILayout.Width(150f)))
                    SetApproach(ExpeditionApproach.Delve);
                GUILayout.EndHorizontal();
            });

            Widgets.Section(UkrainianText.Get("ui.tab.people", g), () =>
            {
                if (roster?.Companions != null)
                    foreach (var c in roster.Companions)
                    {
                        var legality = ScreenText.AssignCandidateLegality(c);
                        bool wasIn = _party.Contains(c.Id);
                        // Фікс-ревью (minor): відновлюємо попереднє GUI.enabled,
                        // а не хардкодимо true — GameShell.DrawHubBody навмисно
                        // вимикає GUI.enabled=false навколо _hub.Draw() під час
                        // Decision (модалка сама не блокує клік крізь фон),
                        // і цей рядок раніше мовчки скасовував той захист до
                        // кінця виклику HubScreen.Draw, якщо гравець стояв на
                        // вкладці «Вилазка».
                        bool previousEnabled = GUI.enabled;
                        GUI.enabled = legality.Enabled || wasIn;
                        // Фікс-ревью (ціль D, знайдено тур-автоплеєм): голий
                        // GUILayout.Toggle малює НЕЗМІНЕНИМ вбудованим скіном
                        // Unity (AlphaSkin.Build() не перевизначає skin.toggle
                        // — той самий розрив, що вже задокументований і
                        // закритий для TitleScreen.cs, §Widgets.TabButton) —
                        // на темній темі це чекбокс зі СВІТЛИМ текстом за
                        // замовчуванням для світлого скіну, майже невидимий на
                        // темному тлі. TabButton — та сама кнопка, акцентна,
                        // коли обрано: той самий прийом, що вже коректно
                        // читається у "Тихо"/"Силою" рядком вище.
                        string label = ScreenText.ResolveCompanionName(c.Id, g, roster) +
                            (legality.Enabled ? "" : " (" + ScreenText.ReasonText(legality, g) + ")");
                        if (Widgets.TabButton(label, wasIn))
                        {
                            if (wasIn) _party.Remove(c.Id); else _party.Add(c.Id);
                            _preview = null;
                        }
                        GUI.enabled = previousEnabled;
                    }
            });

            GUILayout.BeginHorizontal();
            var partyIds = new List<string>(_party);
            var partyLegality = ScreenText.ExpeditionPartyLegality(partyIds, roster);

            if (partyLegality.Enabled)
            {
                if (Widgets.SecondaryButton(UkrainianText.Get("ui.expedition.preview", g), GUILayout.Width(200f)))
                    _preview = shell.TryRun(() => shell.Session.PreviewExpedition(_siteId, _approach, partyIds));
            }
            else
            {
                Widgets.DisabledButton(UkrainianText.Get("ui.expedition.preview", g), ScreenText.ReasonText(partyLegality, g), GUILayout.Width(200f));
            }

            if (_preview != null && partyLegality.Enabled)
            {
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.expedition.depart", g), GUILayout.Width(200f)))
                {
                    var days = _preview.Days;
                    shell.TryRun(() => shell.Session.DepartExpedition(_siteId, _approach, partyIds, days));
                    _preview = null;
                    _party.Clear();
                }
            }
            GUILayout.EndHorizontal();

            if (_preview != null)
            {
                Widgets.LabeledRow(UkrainianText.Get("ui.expedition.threshold", g), _preview.Threshold.ToString());
                Widgets.LabeledRow(UkrainianText.Get("ui.expedition.party_value", g), _preview.PartyValue.ToString());
                Widgets.LabeledRow(UkrainianText.Get("ui.expedition.days", g), _preview.Days.ToString());
                Widgets.LabeledRow(UkrainianText.Get("ui.expedition.expected", g), ScreenText.BandLabelFromRaw(_preview.ExpectedBand, g));
                Widgets.LabeledRow(UkrainianText.Get("resource.materials", g), _preview.ExpectedMaterials.ToString());
                Widgets.LabeledRow(UkrainianText.Get("resource.gold", g), _preview.ExpectedGold.ToString());
            }
        }

        private void SetApproach(ExpeditionApproach approach)
        {
            _approach = approach;
            _preview = null;
        }

        // ===================== Спорядження / Крафт =====================

        /// <summary>
        /// Полірування (ціль 2 «Прозорість дій», owner: "show the stash with
        /// items (name, rarity, what it improves), Equip to a chosen
        /// person/slot, show Unequip only for occupied slots, Craft upgrade
        /// with cost and before→after preview"). Раніше "Зняти" малювалось
        /// для ВСІХ трьох слотів завжди, і схованка не показувала ані
        /// рідкість, ані що саме предмет покращує, ані ціну/наслідок крафту.
        /// </summary>
        private static void DrawGear(GameShell shell, Gender g)
        {
            var roster = shell.Session.GetRosterView();
            var stash = shell.Session.GetStash();
            var city = shell.Session.GetCityView();
            var economy = shell.Session.GetEconomyView();
            StageOf(city, Game.Core.Base.DefaultBuildings.Workshop, out bool workshopOpen);

            Widgets.Section(UkrainianText.Get("ui.tab.people", g), () =>
            {
                if (roster?.Companions == null) return;
                foreach (var c in roster.Companions)
                {
                    var sheet = shell.Session.GetCharacterSheet(c.Id);
                    if (sheet == null) continue;
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(ScreenText.ResolveCompanionName(c.Id, g, roster), AlphaSkin.SubHeader);

                    GUILayout.BeginHorizontal();
                    DrawSlot(shell, sheet.CompanionId, EquipSlot.Weapon, sheet.Equipment.WeaponId, "ui.gear.slot.weapon", g);
                    DrawSlot(shell, sheet.CompanionId, EquipSlot.Armor, sheet.Equipment.ArmorId, "ui.gear.slot.armor", g);
                    DrawSlot(shell, sheet.CompanionId, EquipSlot.Accessory, sheet.Equipment.AccessoryId, "ui.gear.slot.accessory", g);
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }
            });

            Widgets.Section(UkrainianText.Get("ui.gear.stash.title", g), () =>
            {
                // Фікс-ревью (minor, знайдено QA): раніше повідомлення про
                // Майстерню малювалось лише крізь DrawCraftButton — на
                // предметах схованки. Порожня схованка (типовий старт)
                // показувала тільки "Схованка порожня." — жодного натяку,
                // чому крафту нема і чого бракує, на відміну від Buildings/
                // Council, де причина видно завжди, ще до кліку.
                if (!workshopOpen)
                    GUILayout.Label(UkrainianText.Get("ui.gear.craft.workshop_note", g), AlphaSkin.Tooltip);

                if (stash == null || stash.Count == 0)
                {
                    GUILayout.Label(UkrainianText.Get("ui.gear.stash.empty", g), AlphaSkin.Tooltip);
                    return;
                }

                foreach (var item in stash)
                {
                    GUILayout.BeginVertical(GUI.skin.box);

                    string name = UkrainianText.Has("item." + item.Definition.Id, g) ? UkrainianText.Get("item." + item.Definition.Id, g) : item.Definition.Id;
                    string rarity = UkrainianText.Get("rarity." + item.Rarity.ToString().ToLowerInvariant(), g);
                    GUILayout.Label(name + " (" + rarity + ")", AlphaSkin.Body);
                    GUILayout.Label(UkrainianText.Format("ui.gear.improves", g, "stats", ScreenText.ItemStatSummary(item, g)), AlphaSkin.Tooltip);

                    GUILayout.BeginHorizontal();
                    string instanceId = item.InstanceId;
                    var slot = item.Slot;
                    if (roster?.Companions != null)
                        foreach (var c in roster.Companions)
                        {
                            if (c.Loyalty == null && c.Id != Game.Core.Session.GameSession.ProtagonistId) continue;
                            string companionId = c.Id;
                            if (Widgets.SecondaryButton(UkrainianText.Get("ui.gear.equip", g) + ": " + ScreenText.ResolveCompanionName(companionId, g, roster), GUILayout.Width(260f)))
                                shell.TryRun(() => shell.Session.Equip(companionId, instanceId, slot));
                        }
                    GUILayout.EndHorizontal();

                    DrawCraftButton(shell, g, item, workshopOpen, economy);
                    GUILayout.EndVertical();
                }
            });
        }

        private static void DrawSlot(GameShell shell, string companionId, EquipSlot slot, string itemId, string labelKey, Gender g)
        {
            GUILayout.BeginVertical(GUILayout.Width(230f));
            string itemLabel = string.IsNullOrEmpty(itemId)
                ? UkrainianText.Get("ui.sheet.none", g)
                : (UkrainianText.Has("item." + itemId, g) ? UkrainianText.Get("item." + itemId, g) : itemId);
            GUILayout.Label(UkrainianText.Get(labelKey, g) + ": " + itemLabel, AlphaSkin.Body);

            // Owner: "show Unequip only for occupied slots" — раніше кнопка
            // малювалась завжди, навіть для порожнього слота.
            if (!string.IsNullOrEmpty(itemId))
            {
                if (Widgets.SecondaryButton(UkrainianText.Get("ui.gear.unequip", g), GUILayout.Width(220f)))
                    shell.TryRun(() => shell.Session.Unequip(companionId, slot));
            }
            GUILayout.EndVertical();
        }

        private static void DrawCraftButton(GameShell shell, Gender g, Game.Core.Items.ItemInstance item, bool workshopOpen, EconomyView economy)
        {
            if (item.Definition.IsNamed)
            {
                Widgets.DisabledButton(UkrainianText.Get("ui.gear.craft", g), UkrainianText.Get("ui.feedback.craft.named_not_upgradable", g));
                return;
            }
            if (item.Rarity >= Game.Core.Items.Rarity.Epic)
            {
                Widgets.DisabledButton(UkrainianText.Get("ui.gear.craft", g), UkrainianText.Get("ui.feedback.craft.already_max_rarity", g));
                return;
            }

            var itemBalance = new Game.Core.Balance.ItemBalance();
            var preview = Game.Core.Items.CraftSystem.PreviewUpgrade(item);
            string previewText = ScreenText.CraftPreviewText(preview, g);
            string costLine = UkrainianText.Format("ui.gear.craft_cost", g, "gold", itemBalance.CraftGoldCost.ToString(), "materials", itemBalance.CraftMaterialsCost.ToString());
            GUILayout.Label(costLine + (string.IsNullOrEmpty(previewText) ? "" : " · " + previewText), AlphaSkin.Tooltip);

            if (!workshopOpen)
            {
                Widgets.DisabledButton(UkrainianText.Get("ui.gear.craft", g), UkrainianText.Get("ui.feedback.craft.workshop_closed", g), GUILayout.Width(220f));
                return;
            }
            if (economy != null && (economy.Gold < itemBalance.CraftGoldCost || economy.Materials < itemBalance.CraftMaterialsCost))
            {
                Widgets.DisabledButton(UkrainianText.Get("ui.gear.craft", g), UkrainianText.Get("ui.feedback.craft.cannot_afford", g), GUILayout.Width(220f));
                return;
            }

            string instanceId = item.InstanceId;
            if (Widgets.SecondaryButton(UkrainianText.Get("ui.gear.craft", g), GUILayout.Width(220f)))
                shell.TryRun(() => shell.Session.CraftUpgrade(instanceId));
        }

        // ===================== Люди =====================

        private const float SheetHeight = 460f;
        private const float PeopleListWidth = 260f;

        /// <summary>
        /// Полірування (ціль 1 «Картка персонажа», owner feedback: "roster
        /// list on the left and the full sheet of the selected person on the
        /// right ... NO overlapping panels — today the planner is drawn over
        /// the roster"). Раніше короткий список карток і план білда йшли
        /// одне під одним у тому самому вертикальному потоці й ділили
        /// фіксовану висоту — тепер дві незалежні колонки в одній рамці:
        /// список зліва (вибір), повна картка обраного справа.
        /// </summary>
        private void DrawPeople(GameShell shell, Gender g)
        {
            var roster = shell.Session.GetRosterView();
            if (string.IsNullOrEmpty(_selectedCompanionId))
                _selectedCompanionId = Game.Core.Session.GameSession.ProtagonistId;

            GUILayout.BeginHorizontal(GUILayout.Height(SheetHeight));
            DrawPeopleList(g, roster);
            DrawCharacterSheet(shell, g, roster);
            GUILayout.EndHorizontal();
        }

        private void DrawPeopleList(Gender g, RosterView roster)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(PeopleListWidth), GUILayout.Height(SheetHeight));
            GUILayout.Label(UkrainianText.Get("ui.tab.people", g), AlphaSkin.SubHeader);
            _peopleListScroll = Widgets.ScrollListBegin(_peopleListScroll, GUILayout.ExpandHeight(true));
            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                {
                    bool selected = c.Id == _selectedCompanionId;
                    if (Widgets.TabButton(ScreenText.ResolveCompanionName(c.Id, g, roster), selected, GUILayout.ExpandWidth(true)))
                        _selectedCompanionId = c.Id;
                }
            Widgets.ScrollListEnd();
            GUILayout.EndVertical();
        }

        private void DrawCharacterSheet(GameShell shell, Gender g, RosterView roster)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.Height(SheetHeight));

            var sheet = shell.Session.GetCharacterSheet(_selectedCompanionId);
            if (sheet == null)
            {
                GUILayout.Label(UkrainianText.Get("ui.sheet.pick_someone", g), AlphaSkin.Tooltip);
                GUILayout.EndVertical();
                return;
            }

            _sheetScroll = Widgets.ScrollListBegin(_sheetScroll, GUILayout.ExpandHeight(true));

            GUILayout.Label(ScreenText.ResolveCompanionName(sheet.CompanionId, g, roster), AlphaSkin.Header);
            GUILayout.Label(UkrainianText.Format("ui.people.level", g, "level", sheet.Level.ToString()), AlphaSkin.Body);
            GUILayout.Label(UkrainianText.Format("ui.sheet.xp", g, "xp", sheet.Xp.ToString(), "next", sheet.XpToNextLevel.ToString()), AlphaSkin.Body);
            GUILayout.Label(UkrainianText.Format("ui.people.status", g, "status", ScreenText.CompanionStatusLabel(sheet.CompanionId, sheet.Status, g)), AlphaSkin.Body);
            GUILayout.Label(UkrainianText.Format("ui.people.loyalty", g, "loyalty", ScreenText.LoyaltyLabel(sheet.Loyalty, g)), AlphaSkin.Body);

            Widgets.Section(UkrainianText.Get("ui.sheet.section.attributes", g), () =>
            {
                GUILayout.BeginHorizontal();
                foreach (var a in sheet.Attributes)
                    GUILayout.Label(UkrainianText.Get("attr." + a.AttributeKey, g) + ": " + a.Score, AlphaSkin.Body, GUILayout.Width(150f));
                GUILayout.EndHorizontal();
            });

            Widgets.Section(UkrainianText.Get("ui.sheet.section.skills", g), () =>
            {
                // Два стовпці по п'ять — десять скілів одним рядком не влізли б.
                for (int row = 0; row < sheet.Skills.Count; row += 2)
                {
                    GUILayout.BeginHorizontal();
                    for (int col = 0; col < 2 && row + col < sheet.Skills.Count; col++)
                    {
                        var s = sheet.Skills[row + col];
                        GUILayout.Label(UkrainianText.Get("skill." + s.SkillKey, g) + ": " + s.Score, AlphaSkin.Body, GUILayout.Width(220f));
                    }
                    GUILayout.EndHorizontal();
                }
            });

            Widgets.Section(UkrainianText.Get("ui.sheet.section.traits", g), () =>
            {
                if (sheet.Traits.Count == 0) { GUILayout.Label(UkrainianText.Get("ui.sheet.none", g), AlphaSkin.Tooltip); return; }
                foreach (var tr in sheet.Traits)
                {
                    string polarity = UkrainianText.Get("ui.trait.polarity." + tr.Polarity.ToLowerInvariant(), g);
                    GUILayout.Label("• " + UkrainianText.Get("trait." + tr.TraitId, g) + " (" + polarity + "): " +
                        UkrainianText.Get("trait." + tr.TraitId + ".effect", g), AlphaSkin.Body);
                }
            });

            Widgets.Section(UkrainianText.Get("ui.sheet.section.scars", g), () =>
            {
                if (sheet.ScarIds.Count == 0) { GUILayout.Label(UkrainianText.Get("ui.sheet.none", g), AlphaSkin.Tooltip); return; }
                foreach (var scarId in sheet.ScarIds)
                    GUILayout.Label("• " + UkrainianText.Get("scar." + scarId, g), AlphaSkin.Body);
            });

            Widgets.Section(UkrainianText.Get("ui.sheet.section.perks_unlocked", g), () =>
            {
                if (sheet.UnlockedPerkIds.Count == 0) { GUILayout.Label(UkrainianText.Get("ui.sheet.none", g), AlphaSkin.Tooltip); return; }
                foreach (var perkId in sheet.UnlockedPerkIds)
                    GUILayout.Label("• " + UkrainianText.Get("perk." + perkId, g) + ": " + UkrainianText.Get("perk." + perkId + ".effect", g), AlphaSkin.Body);
            });

            Widgets.Section(UkrainianText.Get("ui.sheet.section.perks_available", g), () =>
            {
                if (sheet.AvailablePerks.Count == 0) { GUILayout.Label(UkrainianText.Get("ui.sheet.none", g), AlphaSkin.Tooltip); return; }
                foreach (var p in sheet.AvailablePerks)
                {
                    string line = UkrainianText.Get("perk." + p.PerkId, g) + ": " + UkrainianText.Get("perk." + p.PerkId + ".effect", g);
                    if (p.Available) GUILayout.Label("• " + line, AlphaSkin.Body);
                    else GUILayout.Label("• " + line + " (" + UkrainianText.Get(p.ReasonKey, g) + ")", AlphaSkin.Tooltip);
                }
            });

            Widgets.Section(UkrainianText.Get("ui.sheet.section.combat", g), () =>
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(UkrainianText.Format("ui.sheet.combat.hp", g, "value", sheet.Combat.HpMax.ToString()), AlphaSkin.Body, GUILayout.Width(150f));
                GUILayout.Label(UkrainianText.Format("ui.sheet.combat.ap", g, "value", sheet.Combat.ApMax.ToString()), AlphaSkin.Body, GUILayout.Width(150f));
                GUILayout.Label(UkrainianText.Format("ui.sheet.combat.initiative", g, "value", sheet.Combat.Initiative.ToString()), AlphaSkin.Body, GUILayout.Width(150f));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(UkrainianText.Format("ui.sheet.combat.accuracy", g, "value", sheet.Combat.Accuracy.ToString()), AlphaSkin.Body, GUILayout.Width(150f));
                GUILayout.Label(UkrainianText.Format("ui.sheet.combat.defense", g, "value", sheet.Combat.Defense.ToString()), AlphaSkin.Body, GUILayout.Width(150f));
                GUILayout.Label(UkrainianText.Format("ui.sheet.combat.armor", g, "value", sheet.Combat.Armor.ToString()), AlphaSkin.Body, GUILayout.Width(150f));
                GUILayout.Label(UkrainianText.Format("ui.sheet.combat.crit", g, "value", sheet.Combat.CritChance.ToString()), AlphaSkin.Body, GUILayout.Width(150f));
                GUILayout.EndHorizontal();
            });

            Widgets.Section(UkrainianText.Get("ui.sheet.section.equipment", g), () =>
            {
                GUILayout.Label(UkrainianText.Get("ui.gear.slot.weapon", g) + ": " + EquipLabel(sheet.Equipment.WeaponId, g), AlphaSkin.Body);
                GUILayout.Label(UkrainianText.Get("ui.gear.slot.armor", g) + ": " + EquipLabel(sheet.Equipment.ArmorId, g), AlphaSkin.Body);
                GUILayout.Label(UkrainianText.Get("ui.gear.slot.accessory", g) + ": " + EquipLabel(sheet.Equipment.AccessoryId, g), AlphaSkin.Body);
            });

            // Білд-планувальник протагоніста — чиста секція КАРТКИ (owner:
            // "the protagonist's build planner ... is a clean section of the
            // sheet — NO overlapping panels"), не окремий блок під ростером,
            // як було раніше (план малювався просто під скролвʼю списку).
            if (sheet.CompanionId == Game.Core.Session.GameSession.ProtagonistId)
                DrawBuildPlanner(shell, g);

            Widgets.ScrollListEnd();
            GUILayout.EndVertical();
        }

        private static string EquipLabel(string itemId, Gender g)
            => string.IsNullOrEmpty(itemId)
                ? UkrainianText.Get("ui.sheet.none", g)
                : (UkrainianText.Has("item." + itemId, g) ? UkrainianText.Get("item." + itemId, g) : itemId);

        private void DrawBuildPlanner(GameShell shell, Gender g)
        {
            Widgets.Section(UkrainianText.Get("ui.buildplanner.title", g), () =>
            {
                var preview = shell.TryRun(() => shell.Session.PreviewBuildPlan(Game.Core.Session.GameSession.ProtagonistId, _plan));
                if (preview == null) return;

                GUILayout.Label(UkrainianText.Format("ui.buildplanner.points", g, "points", preview.PointsAvailable.ToString()), AlphaSkin.Body);

                foreach (var skill in Skills.All)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(UkrainianText.Get("skill." + skill.ToString().ToLowerInvariant(), g), AlphaSkin.Body, GUILayout.Width(200f));
                    GUILayout.Label(_plan.InvestedIn(skill).ToString(), AlphaSkin.Body, GUILayout.Width(30f));
                    var s = skill;
                    if (preview.PointCost < preview.PointsAvailable && Widgets.SecondaryButton(UkrainianText.Get("ui.buildplanner.invest", g), GUILayout.Width(60f)))
                        _plan.Invest(s, 1);
                    GUILayout.EndHorizontal();
                }

                if (preview.PointsAvailable == 0) GUILayout.Label(UkrainianText.Get("ui.buildplanner.no_points", g), AlphaSkin.Tooltip);

                GUILayout.Label(ScreenText.BuildPlanResultText(preview.Status, g), AlphaSkin.Tooltip);

                GUILayout.BeginHorizontal();
                if (Widgets.SecondaryButton(UkrainianText.Get("ui.buildplanner.reset", g), GUILayout.Width(160f)))
                    _plan = new BuildPlan();
                if (preview.CanCommit && !_plan.IsEmpty)
                {
                    GUILayout.Label(BuildPreview.IrreversibleWarning, AlphaSkin.Tooltip);
                    if (Widgets.DangerButton(UkrainianText.Get("ui.buildplanner.commit", g), GUILayout.Width(220f)))
                    {
                        // Fallback — навмисно НЕ BuildPlanStatus.Ok (0 = дефолт enum'а):
                        // виняток (не в Morning/FreePlay) не має читатись як «успіх».
                        var status = shell.TryRun(() => shell.Session.CommitBuildPlan(Game.Core.Session.GameSession.ProtagonistId, _plan, true),
                            BuildPlanStatus.NotConfirmed);
                        if (status == BuildPlanStatus.Ok) _plan = new BuildPlan();
                    }
                }
                GUILayout.EndHorizontal();
            });
        }

        // ===================== Квести =====================

        private void DrawQuests(GameShell shell, Gender g)
        {
            int day = shell.Session.CurrentView.Day;
            if (_questOffer == null || _questOfferDay != day)
            {
                _questOffer = shell.TryRun(() => shell.Session.OfferQuestStage(DefaultQuests.HafiyaId));
                _questOfferDay = day;
            }

            var offer = _questOffer;
            if (offer == null)
            {
                GUILayout.Label(UkrainianText.Get("ui.quests.none_active", g), AlphaSkin.Tooltip);
                return;
            }

            // Фікс-ревью (minor, знайдено тур-автоплеєм): за межами Stage==0
            // (пропозиція) ця вкладка не показувала НІЧОГО, крім голого
            // "Етап N" — ні імені квесту, ні підказки, чому нема ні тексту, ні
            // кнопок (проміжний етап-перевірка "grass" резолвиться самою
            // вечірньою/нічною пропозицією, не тут) — з боку виглядало як
            // недороблена вкладка. "quest.<id>" — той самий ключ, що тепер
            // резолвить і стрічку подій (ScreenText.EventLine).
            string questKey = "quest." + offer.QuestId;
            string questName = UkrainianText.Has(questKey, g) ? UkrainianText.Get(questKey, g) : offer.QuestId;
            GUILayout.Label(UkrainianText.Format("ui.quests.stage_named", g, "quest", questName, "stage", offer.Stage.ToString()), AlphaSkin.SubHeader);

            bool hasStageText = offer.Stage == 0 && UkrainianText.Has(DefaultQuests.OfferKey, g);
            if (hasStageText)
                GUILayout.Label(UkrainianText.Get(DefaultQuests.OfferKey, g), AlphaSkin.Body);
            else if (offer.Options == null || offer.Options.Count == 0)
                GUILayout.Label(UkrainianText.Get("ui.quests.check_stage_hint", g), AlphaSkin.Tooltip);

            if (offer.Options != null)
                for (int i = 0; i < offer.Options.Count; i++)
                {
                    int index = i;
                    var option = offer.Options[i];
                    string label = UkrainianText.Has(option.TextKey, g) ? UkrainianText.Get(option.TextKey, g) : option.TextKey;
                    if (option.HasCandidate && Widgets.PrimaryButton(label))
                    {
                        shell.TryRun(() => shell.Session.ResolveQuestChoice(index));
                        _questOffer = null; // етап міг змінитись — перезапит наступним кадром
                    }
                    else if (!option.HasCandidate)
                        Widgets.DisabledButton(label, UkrainianText.Get("ui.common.none", g));
                }
        }

        // ===================== Фракції =====================

        private static void DrawFactions(GameShell shell, Gender g)
        {
            var view = shell.Session.GetFactionsView();
            if (view?.Factions == null) return;
            foreach (var f in view.Factions)
                Widgets.LabeledRow(UkrainianText.Get("faction." + f.Id, g), ScreenText.FactionBandLabel(f.Band, g));
        }

        // ===================== Готовність =====================

        private static void DrawReadiness(GameShell shell, Gender g)
        {
            var view = shell.Session.GetReadinessView();
            if (view == null) return;
            Widgets.LabeledRow(UkrainianText.Get("ui.readiness.title", g), ScreenText.ReadinessLabel(view.Band, g));
            Widgets.LabeledRow(UkrainianText.Format("ui.readiness.milestones", g,
                "reached", view.MilestonesReached.ToString(), "total", view.MilestonesTotal.ToString()), "");
        }

        // ===================== Збереження =====================

        private static void DrawSave(GameShell shell, Gender g)
        {
            var headers = Game.Gameplay.SaveFileStore.ListHeaders();
            foreach (var h in headers)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(ScreenText.SaveSlotLine(h.Slot, h.Occupied, h.Headline, h.Day, g), AlphaSkin.Body, GUILayout.ExpandWidth(true));
                if (h.Slot >= 0 && Widgets.PrimaryButton(UkrainianText.Get("ui.common.confirm", g), GUILayout.Width(160f)))
                {
                    int slot = h.Slot;
                    string blob = shell.TryRun(() => shell.Session.SaveState(slot));
                    if (blob != null)
                    {
                        var view = shell.Session.CurrentView;
                        Game.Gameplay.SaveFileStore.Write(slot, blob, view.TensionBand, view.Day);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }
    }
}
