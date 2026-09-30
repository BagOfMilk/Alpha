using Game.Core.Characters.Creation;
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

                // Полірування (ціль 6 «Рішення», owner: "dungeon room card
                // shows the party"): хто пішов у цей данж — раніше картка
                // цього не показувала взагалі.
                if (view.PartyIds != null && view.PartyIds.Count > 0)
                {
                    var roster = shell.Session.GetRosterView();
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var id in view.PartyIds) names.Add(ScreenText.ResolveCompanionName(id, g, roster));
                    Widgets.LabeledRow(UkrainianText.Get("ui.dungeon.party", g), string.Join(", ", names));
                }
                // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): "ui.dungeon.unbanked" —
                // ШАБЛОН із плейсхолдерами (Format-ключ), а не готовий підпис;
                // LabeledRow.label раніше кликав Get() на тому самому ключі —
                // гравець бачив буквальний рядок "Незбережено: {build} ..."
                // у лівій колонці. Value-колонка
                // (Format) уже рахувала правильно — просто дублювала
                // непотрібний підпис.
                GUILayout.Label(UkrainianText.Format("ui.dungeon.unbanked", g,
                    "build", view.UnbankedBuildComponent.ToString(), "craft", view.UnbankedCraftComponent.ToString(),
                    "gold", view.UnbankedGold.ToString()), AlphaSkin.Body);

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
                        // данжі band не існує (Поправка №1: тихий обхід —
                        // 0 ризику, без полоси); {candidate} додано ціллю 6
                        // «Рішення» (owner: "the quiet candidate").
                        string candidate = room.QuietHasCandidate
                            ? UkrainianText.Format("ui.decision.candidate", g, "name", ScreenText.ResolveCompanionName(room.QuietBestActorId, g, shell.Session.GetRosterView()))
                            : UkrainianText.Get("ui.decision.no_candidate", g);
                        string quiet = UkrainianText.Format("ui.dungeon.option_line", g,
                            "path", UkrainianText.Get("ui.dungeon.quiet", g),
                            "skill", ScreenText.SkillLabel(room.QuietSkillKey, g),
                            "threshold", room.QuietThreshold.ToString(),
                            "candidate", candidate);
                        if (Widgets.PrimaryButton(quiet)) Resolve(shell, IncidentPath.Quiet);
                    }
                    else
                    {
                        Widgets.DisabledButton(UkrainianText.Get("ui.dungeon.quiet", g), UkrainianText.Get("ui.common.none", g));
                    }

                    // Полірування (ціль 6 «Рішення», owner: "тактичний бій:
                    // N ворогів"): кроваво в бойовій кімнаті данжу — завжди
                    // бій без перевірки навички (DungeonRun.ResolveRoom:
                    // Bloody одразу EnterAwaitingBattle) — раніше кнопка
                    // казала лише "Битися", без кількості ворогів.
                    string bloody = UkrainianText.Format("ui.dungeon.bloody_fight", g, "count", room.EnemyCount.ToString(), "enemies", ScreenText.EnemiesCount(room.EnemyCount));
                    if (Widgets.DangerButton(bloody))
                        Resolve(shell, IncidentPath.Bloody);

                    // UX_DESIGN §5.8: прогноз старту бою до вибору шляху (UI-02) — перенести в картку варіанта
                    if (!string.IsNullOrEmpty(room.BloodyOpening))
                    {
                        string quietFail = string.IsNullOrEmpty(room.QuietFailOpening)
                            ? UkrainianText.Get("ui.common.none", g)
                            : UkrainianText.Get("ui.battle.opening." + room.QuietFailOpening, g);
                        GUILayout.Label(UkrainianText.Format("ui.dungeon.opening.preview", g,
                            "bloody", UkrainianText.Get("ui.battle.opening." + room.BloodyOpening, g),
                            "quiet", quietFail), AlphaSkin.HintLine);
                    }

                    // Розмова перед боєм (docs/ABILITIES.md §4.6; власник: «ок»): поріг, ціна
                    // і хто відгукнеться — до кліку (інваріант 8, UI-02). UX_DESIGN §5.8 —
                    // перенести в картки варіантів разом із рядком прогнозу вище.
                    if (room.Parley != null && room.Parley.Count > 0)
                    {
                        GUILayout.Label(UkrainianText.Get("ui.dungeon.parley.title", g), AlphaSkin.Body);
                        foreach (var p in room.Parley) DrawParley(shell, g, p);
                    }
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

        /// <summary>Одна форма розмови: «Слово миру: Переконання 3 / 2 — вийде · підуть 1, битися з 1».</summary>
        private static void DrawParley(GameShell shell, Gender g, Game.Core.Session.Views.ParleyView p)
        {
            string verdict = UkrainianText.Get(p.Passes ? "ui.dungeon.parley.pass" : "ui.dungeon.parley.fail." + p.Form, g);
            string label = UkrainianText.Format("ui.dungeon.parley.line", g,
                "form", UkrainianText.Get("ui.dungeon.parley." + p.Form, g),
                "skill", ScreenText.SkillLabel(p.SkillKey, g),
                "value", p.ParleyValue.ToString(), "threshold", p.ParleyThreshold.ToString(),
                "verdict", verdict);
            if (p.Form == "bribe" && p.GoldCost > 0)
                label += " · " + UkrainianText.Format("ui.dungeon.parley.gold", g, "gold", p.GoldCost.ToString());
            if (p.LeavingCount > 0)
                label += " · " + UkrainianText.Format("ui.dungeon.parley.effect." + p.Form, g,
                    "leaving", p.LeavingCount.ToString(), "remaining", p.RemainingCount.ToString());

            if (p.BlockKey != null)
                Widgets.DisabledButton(label, UkrainianText.Get("ui.dungeon.parley.block." + p.BlockKey, g));
            else if (Widgets.SecondaryButton(label))
            {
                string form = p.Form;
                shell.TryRun(() => shell.Session.ResolveDungeonParley(form));
            }
        }

        private static void DrawFinished(GameShell shell, DungeonView view, Game.Core.Characters.Creation.Gender g)
        {
            GUILayout.BeginHorizontal();
            if (Widgets.PrimaryButton(UkrainianText.Get("ui.dungeon.push", g)))
                shell.TryRun(() => shell.Session.PushDeeper());
            if (Widgets.SecondaryButton(UkrainianText.Get("ui.dungeon.extract", g)))
                shell.TryRun(() => shell.Session.ExtractDungeon());
            // Незворотне — лише з підтвердженням (UX-12, UX_DESIGN §5.15).
            if (Widgets.DangerButton(UkrainianText.Get("ui.dungeon.abandon", g)))
                shell.AskConfirm(new UxConfirm(UkrainianText.Get("ux.dungeon.abandon.question", g), UkrainianText.Get("ux.dungeon.abandon.verb", g),
                        new[] { UkrainianText.Get("ux.dungeon.abandon.loss", g) }),
                    () => shell.TryRun(() => shell.Session.AbandonDungeon()));
            GUILayout.EndHorizontal();
        }
    }
}
