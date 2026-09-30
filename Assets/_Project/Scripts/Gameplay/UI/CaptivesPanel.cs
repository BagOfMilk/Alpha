using System.Collections.Generic;
using System.Linq;
using Game.Core.Characters.Creation;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Наші в полоні на віче (Поправка №14.7; власник, 29.09.2026: «Але десь половина
    /// має втекти»). Хто в полоні й у кого, як тримається (полосою, без чисел —
    /// інваріант 3), і три шляхи порятунку: викуп (ціна до кліку), перемовини (поріг
    /// Переконання до кліку — інваріант 8), рейд (вороги до кліку — Статут UI-02; склад
    /// обирає гравець, як загін вилазки). Недоступне — сіре з поясненням (UI-04).
    /// Окремий файл — у вкладці віча лише один рядок виклику, як і в <see cref="PrisonersPanel"/>.
    /// </summary>
    public static class CaptivesPanel
    {
        /// <summary>Обраний гравцем склад рейду — на кожного бранця свій (ключ — id бранця).</summary>
        private static readonly Dictionary<string, List<string>> RaidParty = new Dictionary<string, List<string>>();

        public static void Draw(GameShell shell, Gender g)
        {
            var captives = shell?.Session?.GetCaptivesView();
            if (captives == null || captives.Count == 0) return;

            var roster = shell.Session.GetRosterView();
            Widgets.Section(UkrainianText.Get("ui.captives.title", g), () =>
            {
                foreach (var c in captives) DrawOne(shell, g, roster, c);
            });
        }

        private static void DrawOne(GameShell shell, Gender g, RosterView roster, CaptiveView c)
        {
            string name = ScreenText.ResolveCompanionName(c.CompanionId, g, roster);
            string captor = EnemyName(c.CaptorNameKey, g);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(UkrainianText.Format("ui.captives.held_by", g, "name", name, "captor", captor), AlphaSkin.Body);
            GUILayout.Label(UkrainianText.Get("ui.captives.band." + c.Band, g),
                c.Band == "Breaking" ? AlphaSkin.DangerText : AlphaSkin.HintLine);

            string id = c.CompanionId;
            GUILayout.BeginHorizontal();
            string ransom = UkrainianText.Format("ui.captives.ransom", g, "gold", c.RansomGold.ToString());
            if (c.CanAffordRansom)
            {
                if (Widgets.SecondaryButton(ransom)) shell.TryRun(() => shell.Session.RansomCaptive(id));
            }
            else Widgets.DisabledButton(ransom, UkrainianText.Get("ui.captives.ransom.poor", g));

            string talk = UkrainianText.Format("ui.captives.talk", g, "value", c.BestPersuade.ToString(), "threshold", c.TalkThreshold.ToString());
            if (c.CanTalk)
            {
                if (Widgets.SecondaryButton(talk)) shell.TryRun(() => shell.Session.NegotiateCaptive(id));
            }
            else Widgets.DisabledButton(talk, UkrainianText.Format("ui.captives.talk.weak", g,
                "value", c.BestPersuade.ToString(), "threshold", c.TalkThreshold.ToString()));

            if (!RaidParty.TryGetValue(id, out var party)) RaidParty[id] = party = new List<string>();
            party.RemoveAll(p => c.RaidCandidateIds == null || !c.RaidCandidateIds.Contains(p));

            string raid = UkrainianText.Get("ui.captives.raid", g);
            if (!c.CanRaid)
                Widgets.DisabledButton(raid, UkrainianText.Get("ui.captives.raid.none", g));
            else if (party.Count == 0)
                Widgets.DisabledButton(raid, UkrainianText.Get("ui.captives.raid.pick_first", g));
            else if (party.Count > c.RaidPartyMax)
                Widgets.DisabledButton(raid, UkrainianText.Format("ui.captives.raid.too_many", g, "max", c.RaidPartyMax.ToString()));
            else if (Widgets.PrimaryButton(raid))
            {
                var chosen = new List<string>(party);
                shell.TryRun(() => shell.Session.RaidCaptors(id, chosen));
                party.Clear();
            }
            GUILayout.EndHorizontal();

            if (c.CanRaid)
            {
                // Склад рейду — гравець, як загін вилазки (той самий вигляд перемикачів).
                GUILayout.Label(UkrainianText.Format("ui.captives.raid.pick", g, "max", c.RaidPartyMax.ToString()), AlphaSkin.HintLine);
                GUILayout.BeginHorizontal();
                foreach (var candidate in c.RaidCandidateIds)
                {
                    bool chosen = party.Contains(candidate);
                    if (Widgets.TabButton(ScreenText.ResolveCompanionName(candidate, g, roster), chosen))
                    {
                        if (chosen) party.Remove(candidate); else party.Add(candidate);
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Label(UkrainianText.Format("ui.captives.raid.enemies", g,
                    "enemies", Names(c.RaidEnemyIds, e => EnemyName(e, g))), AlphaSkin.HintLine);
            }
            GUILayout.EndVertical();
        }

        private static string EnemyName(string key, Gender g)
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
