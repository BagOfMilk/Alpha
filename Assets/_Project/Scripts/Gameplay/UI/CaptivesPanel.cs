using System.Collections.Generic;
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
    /// Переконання до кліку — інваріант 8), рейд (склад і вороги до кліку — Статут
    /// UI-02). Недоступне — сіре з поясненням (UI-04). Окремий файл — у вкладці віча
    /// лише один рядок виклику, як і в <see cref="PrisonersPanel"/>.
    /// </summary>
    public static class CaptivesPanel
    {
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

            string raid = UkrainianText.Get("ui.captives.raid", g);
            if (c.CanRaid)
            {
                if (Widgets.PrimaryButton(raid)) shell.TryRun(() => shell.Session.RaidCaptors(id));
            }
            else Widgets.DisabledButton(raid, UkrainianText.Get("ui.captives.raid.none", g));
            GUILayout.EndHorizontal();

            if (c.CanRaid)
                GUILayout.Label(UkrainianText.Format("ui.captives.raid.party", g,
                    "party", Names(c.RaidPartyIds, id2 => ScreenText.ResolveCompanionName(id2, g, roster)),
                    "enemies", Names(c.RaidEnemyIds, e => EnemyName(e, g))), AlphaSkin.HintLine);
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
