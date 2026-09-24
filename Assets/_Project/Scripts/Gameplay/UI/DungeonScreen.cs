using Game.Core.Loop;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Данж (§18 таблиці «одна гра»): картка кімнати (тип, текст, тихий обхід
    /// з ефективним порогом проти кровавого бою), незбережена здобич, полоса
    /// загрози, push deeper / extract / abandon.
    /// </summary>
    public sealed class DungeonScreen
    {
        public void Draw(GameShell shell)
        {
            var g = shell.ProtagonistGender;
            var view = shell.Session.GetDungeonView();
            if (view == null) return;

            Widgets.Panel(UkrainianText.Get("ui.dungeon.title", g), () =>
            {
                Widgets.LabeledRow(UkrainianText.Get("ui.dungeon.depth", g), view.Depth.ToString());
                Widgets.LabeledRow(UkrainianText.Get("ui.dungeon.threat", g), ScreenText.ThreatChip(view.ThreatBand, g));
                Widgets.LabeledRow(UkrainianText.Get("ui.dungeon.unbanked", g),
                    UkrainianText.Format("ui.dungeon.unbanked", g,
                        "materials", view.UnbankedMaterials.ToString(), "gold", view.UnbankedGold.ToString()));

                GUILayout.Space(10f);

                if (view.CurrentRoom != null)
                    DrawRoom(shell, view.CurrentRoom, g);
                else
                    DrawFinished(shell, view, g);
            }, GUILayout.ExpandWidth(true));
        }

        private static void DrawRoom(GameShell shell, DungeonRoomView room, Game.Core.Characters.Creation.Gender g)
        {
            GUILayout.Label(UkrainianText.Has(room.DisplayName, g) ? UkrainianText.Get(room.DisplayName, g) : room.DisplayName, AlphaSkin.SubHeader);

            switch (room.Type)
            {
                case "Combat":
                    if (room.HasQuietBypass)
                    {
                        string quiet = UkrainianText.Format("ui.decision.option_line", g,
                            "path", UkrainianText.Get("ui.dungeon.quiet", g),
                            "skill", ScreenText.SkillLabel(room.QuietSkillKey, g),
                            "threshold", room.QuietThreshold.ToString(),
                            "candidate", "", "band", "");
                        if (Widgets.PrimaryButton(quiet)) Resolve(shell, IncidentPath.Quiet);
                    }
                    else
                    {
                        Widgets.DisabledButton(UkrainianText.Get("ui.dungeon.quiet", g), UkrainianText.Get("ui.common.none", g));
                    }

                    string bloody = UkrainianText.Format("ui.decision.option_line", g,
                        "path", UkrainianText.Get("ui.dungeon.bloody", g),
                        "skill", ScreenText.SkillLabel(room.BloodySkillKey, g),
                        "threshold", room.BloodyThreshold.ToString(),
                        "candidate", "", "band", "");
                    if (Widgets.DangerButton(bloody)) Resolve(shell, IncidentPath.Bloody);
                    break;

                case "Treasure":
                    if (Widgets.PrimaryButton(UkrainianText.Get("ui.common.confirm", g)))
                        Resolve(shell, IncidentPath.Quiet);
                    break;

                case "Event":
                    if (room.EventOptionKeys != null)
                        for (int i = 0; i < room.EventOptionKeys.Count; i++)
                        {
                            int index = i;
                            string key = room.EventOptionKeys[i];
                            if (Widgets.SecondaryButton(UkrainianText.Has(key, g) ? UkrainianText.Get(key, g) : key))
                                shell.TryRun(() => shell.Session.ResolveDungeonEvent(index));
                        }
                    break;
            }
        }

        private static void Resolve(GameShell shell, IncidentPath path)
            => shell.TryRun(() => shell.Session.ResolveDungeonRoom(path));

        private static void DrawFinished(GameShell shell, DungeonView view, Game.Core.Characters.Creation.Gender g)
        {
            GUILayout.BeginHorizontal();
            if (Widgets.PrimaryButton(UkrainianText.Get("ui.dungeon.push", g)))
                shell.TryRun(() => shell.Session.PushDeeper());
            if (Widgets.SecondaryButton(UkrainianText.Get("ui.dungeon.extract", g)))
                shell.TryRun(() => shell.Session.ExtractDungeon());
            if (Widgets.DangerButton(UkrainianText.Get("ui.dungeon.abandon", g)))
                shell.TryRun(() => shell.Session.AbandonDungeon());
            GUILayout.EndHorizontal();
        }
    }
}
