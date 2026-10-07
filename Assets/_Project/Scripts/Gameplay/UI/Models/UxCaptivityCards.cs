using System.Collections.Generic;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Полонені й «наші в полоні» на Вічі — картки з діями, а не IMGUI-блок під
    /// панеллю (борг огляду 06.10.2026, Поправка №22: дія має бути досяжна тим
    /// самим входом, що й решта, — людиною, автотуром і ботом через
    /// <see cref="IUxInput"/>). Зміст і правила ті самі, що були в
    /// <c>PrisonersPanel</c>/<c>CaptivesPanel</c> (Поправка №14.2, №14.7): готовність і
    /// неспокій полосою без чисел (інваріант 3), ціна викупу й поріг перемовин до
    /// кліку (Статут UI-02, інваріант 8), склад рейду обирає гравець, недоступне —
    /// сіре з причиною (UI-04).
    /// </summary>
    public static class UxCaptivityCards
    {
        public static void AddTo(IUxHost h, UxPanelModel panel)
        {
            Prisoners(h, panel);
            Captives(h, panel);
        }

        /// <summary>Полонені громади (№14.2): переманити, викуп, відпустити.</summary>
        private static void Prisoners(IUxHost h, UxPanelModel panel)
        {
            var g = h.Gender;
            var prisoners = h.Session.GetPrisonersView();
            if (prisoners == null || prisoners.Count == 0) return;
            string group = UkrainianText.Get("ui.prisoners.title", g);
            for (int i = 0; i < prisoners.Count; i++)
            {
                var p = prisoners[i];
                string id = p.Id;
                var card = new UxCard { Title = EnemyName(p.DisplayNameKey, g), Subtitle = group };
                card.Lines.Add(UkrainianText.Get("ui.prisoners.disposition." + p.Disposition, g) + " · "
                    + UkrainianText.Get("ui.prisoners.restless." + p.Restlessness, g));
                if (i == 0 && !p.Guarded) card.Chips.Add(new UxChip(UkrainianText.Get("ui.prisoners.unguarded", g), UxTone.Bad));

                var recruit = UxBricks.Act("prisoner:recruit:" + id, UkrainianText.Get("ui.prisoners.recruit", g), UxIntent.Primary,
                    () => UxBricks.Run(h, () => h.Session.RecruitPrisoner(id)))
                    .Calls(nameof(GameSession.RecruitPrisoner));
                if (!p.CanRecruitNow)
                    recruit.DisabledReason = UkrainianText.Get(p.NeverRecruitable ? "ui.prisoners.never" : "ui.prisoners.not_ready", g);
                card.Actions.Add(recruit);

                card.Actions.Add(UxBricks.Act("prisoner:ransom:" + id,
                    UkrainianText.Format("ui.prisoners.ransom", g, "gold", p.RansomGold.ToString()), UxIntent.Secondary,
                    () => UxBricks.Run(h, () => h.Session.RansomPrisoner(id)))
                    .Calls(nameof(GameSession.RansomPrisoner)));
                card.Actions.Add(UxBricks.Act("prisoner:release:" + id, UkrainianText.Get("ui.prisoners.release", g), UxIntent.Secondary,
                    () => UxBricks.Run(h, () => h.Session.ReleasePrisoner(id)))
                    .Calls(nameof(GameSession.ReleasePrisoner)));
                panel.Cards.Add(card);
            }
        }

        /// <summary>Наші в полоні (№14.7): викуп, перемовини, рейд зі складом, який обирає гравець.</summary>
        private static void Captives(IUxHost h, UxPanelModel panel)
        {
            var g = h.Gender;
            var captives = h.Session.GetCaptivesView();
            if (captives == null || captives.Count == 0) return;
            var roster = h.Session.GetRosterView();
            var parties = h.PanelState.CaptiveRaidParty;
            string group = UkrainianText.Get("ui.captives.title", g);
            foreach (var c in captives)
            {
                string id = c.CompanionId;
                var card = new UxCard
                {
                    Title = UkrainianText.Format("ui.captives.held_by", g, "name", ScreenText.ResolveCompanionName(id, g, roster),
                        "captor", EnemyName(c.CaptorNameKey, g)),
                    Subtitle = group
                };
                card.Chips.Add(new UxChip(UkrainianText.Get("ui.captives.band." + c.Band, g), c.Band == "Breaking" ? UxTone.Bad : UxTone.Neutral));

                var ransom = UxBricks.Act("captive:ransom:" + id,
                    UkrainianText.Format("ui.captives.ransom", g, "gold", c.RansomGold.ToString()), UxIntent.Secondary,
                    () => UxBricks.Run(h, () => h.Session.RansomCaptive(id)))
                    .Calls(nameof(GameSession.RansomCaptive));
                if (!c.CanAffordRansom) ransom.DisabledReason = UkrainianText.Get("ui.captives.ransom.poor", g);
                card.Actions.Add(ransom);

                var talk = UxBricks.Act("captive:talk:" + id,
                    UkrainianText.Format("ui.captives.talk", g, "value", c.BestPersuade.ToString(), "threshold", c.TalkThreshold.ToString()),
                    UxIntent.Secondary,
                    () => UxBricks.Run(h, () => h.Session.NegotiateCaptive(id)))
                    .Calls(nameof(GameSession.NegotiateCaptive));
                if (!c.CanTalk)
                    talk.DisabledReason = UkrainianText.Format("ui.captives.talk.weak", g,
                        "value", c.BestPersuade.ToString(), "threshold", c.TalkThreshold.ToString());
                card.Actions.Add(talk);

                List<string> party;
                if (!parties.TryGetValue(id, out party)) parties[id] = party = new List<string>();
                party.RemoveAll(p => c.RaidCandidateIds == null || !Contains(c.RaidCandidateIds, p));

                if (c.CanRaid)
                {
                    // Склад рейду — гравець, як загін вилазки (той самий перемикач «до загону»).
                    card.Lines.Add(UkrainianText.Format("ui.captives.raid.pick", g, "max", c.RaidPartyMax.ToString()));
                    foreach (var candidate in c.RaidCandidateIds)
                    {
                        string who = candidate;
                        var pick = UxBricks.Go("captive:pick:" + id + ":" + who, ScreenText.ResolveCompanionName(who, g, roster), () =>
                        {
                            if (party.Contains(who)) party.Remove(who); else party.Add(who);
                        });
                        pick.Selected = party.Contains(who);
                        card.Actions.Add(pick);
                    }
                    card.Lines.Add(UkrainianText.Format("ui.captives.raid.enemies", g, "enemies", Names(c.RaidEnemyIds, e => EnemyName(e, g))));
                }

                var chosen = new List<string>(party);
                var raid = UxBricks.Act("captive:raid:" + id, UkrainianText.Get("ui.captives.raid", g), UxIntent.Danger, () =>
                {
                    var outcome = UxBricks.Run(h, () => h.Session.RaidCaptors(id, chosen));
                    if (outcome.Ok) party.Clear();
                    return outcome;
                }).Calls(nameof(GameSession.RaidCaptors));
                if (!c.CanRaid) raid.DisabledReason = UkrainianText.Get("ui.captives.raid.none", g);
                else if (party.Count == 0) raid.DisabledReason = UkrainianText.Get("ui.captives.raid.pick_first", g);
                else if (party.Count > c.RaidPartyMax)
                    raid.DisabledReason = UkrainianText.Format("ui.captives.raid.too_many", g, "max", c.RaidPartyMax.ToString());
                card.Actions.Add(raid);
                panel.Cards.Add(card);
            }
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return true;
            return false;
        }

        private static string EnemyName(string key, Game.Core.Characters.Creation.Gender g)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            string bare = key.StartsWith("enemy.") ? key.Substring(6) : key;
            return UkrainianText.Has("enemy." + bare, g) ? UkrainianText.Get("enemy." + bare, g) : bare;
        }

        private static string Names(IReadOnlyList<string> ids, System.Func<string, string> resolve)
        {
            if (ids == null || ids.Count == 0) return string.Empty;
            var parts = new List<string>(ids.Count);
            foreach (var id in ids) parts.Add(resolve(id));
            return string.Join(", ", parts);
        }
    }
}
