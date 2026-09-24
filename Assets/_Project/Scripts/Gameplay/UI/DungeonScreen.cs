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
                // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): "ui.dungeon.unbanked" —
                // ШАБЛОН із плейсхолдерами (Format-ключ), а не готовий підпис;
                // LabeledRow.label раніше кликав Get() на тому самому ключі —
                // гравець бачив буквальний рядок "Незбережено: {materials}
                // матеріалів, {gold} золота" у лівій колонці. Value-колонка
                // (Format) уже рахувала правильно — просто дублювала
                // непотрібний підпис.
                GUILayout.Label(UkrainianText.Format("ui.dungeon.unbanked", g,
                    "materials", view.UnbankedMaterials.ToString(), "gold", view.UnbankedGold.ToString()), AlphaSkin.Body);

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
                        // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): окремий
                        // короткий ключ "ui.dungeon.option_line" замість
                        // спільного "ui.decision.option_line" (звичайна точка
                        // рішення) — той шаблон закінчується
                        // "— {candidate}, очікувана полоса: {band}.", а в
                        // данжі candidate/band не існує (Поправка №1: тихий
                        // обхід — 0 ризику, без кандидата й полоси), тож
                        // гравець бачив би висячі "— , очікувана полоса: .".
                        string quiet = UkrainianText.Format("ui.dungeon.option_line", g,
                            "path", UkrainianText.Get("ui.dungeon.quiet", g),
                            "skill", ScreenText.SkillLabel(room.QuietSkillKey, g),
                            "threshold", room.QuietThreshold.ToString());
                        if (Widgets.PrimaryButton(quiet)) Resolve(shell, IncidentPath.Quiet);
                    }
                    else
                    {
                        Widgets.DisabledButton(UkrainianText.Get("ui.dungeon.quiet", g), UkrainianText.Get("ui.common.none", g));
                    }

                    // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): кроваво в
                    // бойовій кімнаті данжу — завжди бій без перевірки навички
                    // (DungeonRun.ResolveRoom: Bloody одразу EnterAwaitingBattle,
                    // жодного порогу) — GameSession.BuildDungeonRoomView НІКОЛИ
                    // не заповнює BloodySkillKey/BloodyThreshold (лишаються
                    // null/0), тож стара формула "{skill} ≥ {threshold}"
                    // показувала порожній навик і "≥ 0". Без штучного порогу,
                    // яким і так ніхто не керує.
                    if (Widgets.DangerButton(UkrainianText.Get("ui.dungeon.bloody_fight", g)))
                        Resolve(shell, IncidentPath.Bloody);
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
