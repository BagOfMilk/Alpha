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
        private Vector2 _peopleScroll;

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

        private void DrawBuildings(GameShell shell, Gender g)
        {
            var city = shell.Session.GetCityView();
            _buildingsScroll = Widgets.ScrollListBegin(_buildingsScroll, GUILayout.ExpandHeight(true));
            foreach (var id in BuildingIds)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(UkrainianText.Get("building." + id, g), AlphaSkin.Body, GUILayout.Width(220f));

                int stage = StageOf(city, id, out bool built);
                if (built)
                    GUILayout.Label(UkrainianText.Get("ui.buildings.built", g), AlphaSkin.Body, GUILayout.ExpandWidth(true));
                else if (stage > 0)
                    GUILayout.Label(UkrainianText.Format("ui.buildings.in_progress", g, "stage", stage.ToString()), AlphaSkin.Body, GUILayout.ExpandWidth(true));
                else
                    GUILayout.Label(UkrainianText.Get("ui.common.empty", g), AlphaSkin.Tooltip, GUILayout.ExpandWidth(true));

                if (!built && stage == 0)
                {
                    string buildingId = id;
                    if (Widgets.PrimaryButton(UkrainianText.Get("ui.buildings.order", g), GUILayout.Width(160f)))
                        shell.TryRun(() => shell.Session.OrderBuilding(buildingId));
                }
                GUILayout.EndHorizontal();
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

        private void DrawCouncil(GameShell shell, Gender g)
        {
            var city = shell.Session.GetCityView();

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(UkrainianText.Get("ui.council.raid", g), AlphaSkin.Body, GUILayout.ExpandWidth(true));
            if (city != null && city.RaidReady)
            {
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.council.raid", g), GUILayout.Width(200f)))
                    shell.TryRun(() => shell.Session.OrderRaid());
            }
            else Widgets.DisabledButton(UkrainianText.Get("ui.council.raid", g), UkrainianText.Get("ui.common.none", g), GUILayout.Width(200f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(UkrainianText.Get("ui.council.settlers", g), AlphaSkin.Body, GUILayout.ExpandWidth(true));
            if (city != null && city.SettlersReady)
            {
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.council.settlers", g), GUILayout.Width(200f)))
                    shell.TryRun(() => shell.Session.OrderSettlers());
            }
            else Widgets.DisabledButton(UkrainianText.Get("ui.council.settlers", g), UkrainianText.Get("ui.common.none", g), GUILayout.Width(200f));
            GUILayout.EndHorizontal();

            Widgets.Section(UkrainianText.Get("ui.council.decree", g), () =>
            {
                GUILayout.BeginHorizontal();
                foreach (var factionId in FactionIds)
                {
                    string fid = factionId;
                    if (Widgets.SecondaryButton(UkrainianText.Get("faction." + fid, g), GUILayout.Width(180f)))
                        shell.TryRun(() => shell.Session.OrderDecree(fid));
                }
                GUILayout.EndHorizontal();
            });

            Widgets.Section(UkrainianText.Get("ui.council.diplomacy", g), () =>
            {
                GUILayout.BeginHorizontal();
                foreach (var factionId in FactionIds)
                {
                    string fid = factionId;
                    if (Widgets.SecondaryButton(UkrainianText.Get("faction." + fid, g), GUILayout.Width(180f)))
                        shell.TryRun(() => shell.Session.OrderDiplomacy(fid));
                }
                GUILayout.EndHorizontal();
            });

            Widgets.Section(UkrainianText.Get("ui.council.investment", g), () =>
            {
                GUILayout.BeginHorizontal();
                foreach (var id in BuildingIds)
                {
                    if (StageOf(city, id, out bool built) == 0 && !built) continue;
                    string buildingId = id;
                    if (Widgets.SecondaryButton(UkrainianText.Get("building." + id, g), GUILayout.Width(180f)))
                        shell.TryRun(() => shell.Session.OrderInvestment(buildingId));
                }
                GUILayout.EndHorizontal();
            });

            if (Widgets.SecondaryButton(UkrainianText.Get("ui.council.prepare_threat", g)))
                shell.TryRun(() => shell.Session.OrderPrepareThreat());

            Widgets.Section(UkrainianText.Get("ui.council.outfit_expedition", g), () =>
            {
                GUILayout.BeginHorizontal();
                foreach (var site in SiteIds)
                {
                    string sid = site;
                    if (Widgets.SecondaryButton(UkrainianText.Get("site." + site, g), GUILayout.Width(180f)))
                        shell.TryRun(() => shell.Session.OrderOutfitExpedition(sid));
                }
                GUILayout.EndHorizontal();
            });
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
                    if (Widgets.TabButton(UkrainianText.Get("site." + site, g), _siteId == site, GUILayout.Width(180f)))
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
                        bool now = GUILayout.Toggle(wasIn, ScreenText.ResolveCompanionName(c.Id, g, roster) +
                            (legality.Enabled ? "" : " (" + UkrainianText.Get(legality.ReasonKey, g) + ")"));
                        GUI.enabled = previousEnabled;
                        if (now && !wasIn) { _party.Add(c.Id); _preview = null; }
                        else if (!now && wasIn) { _party.Remove(c.Id); _preview = null; }
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
                Widgets.DisabledButton(UkrainianText.Get("ui.expedition.preview", g), UkrainianText.Get(partyLegality.ReasonKey, g), GUILayout.Width(200f));
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

        private static void DrawGear(GameShell shell, Gender g)
        {
            var roster = shell.Session.GetRosterView();
            var stash = shell.Session.GetStash();

            Widgets.Section(UkrainianText.Get("ui.tab.people", g), () =>
            {
                if (roster?.Companions == null) return;
                foreach (var c in roster.Companions)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(ScreenText.ResolveCompanionName(c.Id, g, roster), AlphaSkin.SubHeader);
                    string equipped = (c.Equipped != null && c.Equipped.Count > 0)
                        ? string.Join(", ", ToLabels(c.Equipped, g))
                        : UkrainianText.Get("ui.people.equipped.none", g);
                    GUILayout.Label(UkrainianText.Format("ui.people.equipped", g, "items", equipped), AlphaSkin.Body);

                    GUILayout.BeginHorizontal();
                    DrawUnequip(shell, c.Id, EquipSlot.Weapon, "ui.gear.slot.weapon", g);
                    DrawUnequip(shell, c.Id, EquipSlot.Armor, "ui.gear.slot.armor", g);
                    DrawUnequip(shell, c.Id, EquipSlot.Accessory, "ui.gear.slot.accessory", g);
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }
            });

            Widgets.Section(UkrainianText.Get("ui.gear.stash.title", g), () =>
            {
                if (stash == null || stash.Count == 0)
                {
                    GUILayout.Label(UkrainianText.Get("ui.gear.stash.empty", g), AlphaSkin.Tooltip);
                    return;
                }

                foreach (var item in stash)
                {
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    GUILayout.Label(UkrainianText.Has("item." + item.Definition.Id, g) ? UkrainianText.Get("item." + item.Definition.Id, g) : item.Definition.Id,
                        AlphaSkin.Body, GUILayout.ExpandWidth(true));

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

                    if (Widgets.SecondaryButton(UkrainianText.Get("ui.gear.craft", g), GUILayout.Width(140f)))
                        shell.TryRun(() => shell.Session.CraftUpgrade(instanceId));
                    GUILayout.EndHorizontal();
                }
            });
        }

        private static void DrawUnequip(GameShell shell, string companionId, EquipSlot slot, string labelKey, Gender g)
        {
            if (Widgets.SecondaryButton(UkrainianText.Get("ui.gear.unequip", g) + ": " + UkrainianText.Get(labelKey, g), GUILayout.Width(220f)))
                shell.TryRun(() => shell.Session.Unequip(companionId, slot));
        }

        private static IEnumerable<string> ToLabels(IReadOnlyList<string> itemIds, Gender g)
        {
            foreach (var id in itemIds)
                yield return UkrainianText.Has("item." + id, g) ? UkrainianText.Get("item." + id, g) : id;
        }

        // ===================== Люди =====================

        private void DrawPeople(GameShell shell, Gender g)
        {
            var roster = shell.Session.GetRosterView();
            Widgets.TooltipLine(UkrainianText.Get("ui.people.sheet.limited", g));

            _peopleScroll = Widgets.ScrollListBegin(_peopleScroll, GUILayout.ExpandHeight(true));
            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(ScreenText.ResolveCompanionName(c.Id, g, roster), AlphaSkin.SubHeader);
                    Widgets.LabeledRow(UkrainianText.Format("ui.people.level", g, "level", c.Level.ToString()), "");
                    Widgets.LabeledRow(UkrainianText.Get("ui.people.status", g), ScreenText.CompanionStatusLabel(c.Status, g));
                    Widgets.LabeledRow(UkrainianText.Get("ui.people.loyalty", g), ScreenText.LoyaltyLabel(c.Loyalty, g));
                    Widgets.LabeledRow(UkrainianText.Format("ui.people.scars", g, "count", c.ScarCount.ToString()), "");
                    GUILayout.EndVertical();
                }
            Widgets.ScrollListEnd();

            DrawBuildPlanner(shell, g);
        }

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

            GUILayout.Label(UkrainianText.Format("ui.quests.stage", g, "stage", offer.Stage.ToString()), AlphaSkin.SubHeader);
            if (offer.Stage == 0 && UkrainianText.Has(DefaultQuests.OfferKey, g))
                GUILayout.Label(UkrainianText.Get(DefaultQuests.OfferKey, g), AlphaSkin.Body);

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
