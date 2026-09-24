using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Запасний IMGUI-грид бою (R18 фолбек, коли <see cref="IBattlePresenter"/>
    /// не знайдено в сцені): клітинки-кнопки, підсвічені досяжні тайли, клік
    /// по юніту ворога — атака, клік по порожній досяжній клітинці — рух.
    /// Читає ЛИШЕ <see cref="BattleView"/> і команди <c>Combat*</c> — жодного
    /// типу <c>Game.Core.Combat</c>, крім <see cref="GridPos"/>, яким і так
    /// параметризовані самі команди.
    /// </summary>
    public sealed class BattleScreen
    {
        private static readonly string[] AbilityIds = { "lunge", "set_trap", "move_order", "volley" };

        private string _selectedTargetId;

        /// <summary>
        /// Фікс-ревью (major): очікуємо клік по сітці як напрямок дозору
        /// (GameSession.CombatEnterOverwatch(GridPos aim)) замість звичайного
        /// руху/атаки — той самий грид, інший сенс кліку, поки прапорець
        /// піднято.
        /// </summary>
        private bool _awaitingOverwatchAim;

        public void Draw(GameShell shell)
        {
            var g = shell.ProtagonistGender;
            var view = shell.Session.GetBattleView();
            if (view == null) return;

            Widgets.Panel(UkrainianText.Get("ui.battle.fallback.title", g), () =>
            {
                GUILayout.Label(UkrainianText.Format("ui.battle.round", g, "round", view.Round.ToString()) +
                                 " · " + OutcomeLabel(view.Outcome, g), AlphaSkin.SubHeader);

                // Ім'я — за тим самим правилом, що й у журналі бою (BattleLogText.UnitName):
                // id юніта бою ("u_maksym") — не id компаньйона, і ResolveCompanionName
                // показував його сирим. Плейсхолдер таблиці — {name}, не {companion}.
                var current = FindUnit(view, view.CurrentUnitId);
                if (current != null)
                    GUILayout.Label(UkrainianText.Format("ui.battle.current_unit", g,
                        "name", BattleLogText.UnitName(view, current.Id, g)), AlphaSkin.Body);

                DrawGrid(shell, view, current, g);
                GUILayout.Space(8f);
                DrawUnitList(shell, view, g);

                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.battle.autoresolve", g)))
                    shell.TryRun(() => shell.Session.CombatAutoResolve());
                if (Widgets.SecondaryButton(UkrainianText.Get("ui.battle.end_turn", g)))
                    shell.TryRun(() => shell.Session.CombatEndTurn());
                if (Widgets.TabButton(UkrainianText.Get("ui.battle.overwatch.button", g), _awaitingOverwatchAim))
                    _awaitingOverwatchAim = !_awaitingOverwatchAim;
                GUILayout.EndHorizontal();

                if (_awaitingOverwatchAim)
                    Widgets.TooltipLine(UkrainianText.Get("ui.battle.overwatch.aim_hint", g));

                DrawAbilities(shell, g);

                DrawLog(view, g);
            }, GUILayout.ExpandWidth(true));
        }

        /// <summary>
        /// Фікс-ревью (major): без цього ряду overwatch (US-3.6, CLAUDE.md) і
        /// вміння (CombatUseAbility) були недоступні гравцю в фолбеку взагалі —
        /// лише рух/атака/автобій/завершити хід. Кнопки — за відомими
        /// ability.&lt;id&gt; з таблиці (§7.20/AddEnemyAndWeaponAndAbilityIds);
        /// GameSession сам відхилить нелегальну (OnCooldown/NotEnoughAp/...) —
        /// це CombatActionResult, не виняток, тому тут не потрібен TryRun-фідбек
        /// понад те, що вже показує BattleView.Log.
        /// </summary>
        private void DrawAbilities(GameShell shell, Game.Core.Characters.Creation.Gender g)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(UkrainianText.Get("ui.battle.abilities.label", g), AlphaSkin.Body, GUILayout.Width(90f));
            foreach (var abilityId in AbilityIds)
            {
                string id = abilityId;
                string key = "ability." + id;
                string label = UkrainianText.Has(key, g) ? UkrainianText.Get(key, g) : id;
                if (Widgets.SecondaryButton(label, GUILayout.Width(140f)))
                    shell.TryRun(() => shell.Session.CombatUseAbility(key, _selectedTargetId, null));
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>BattleView.Outcome — сирий рядок enum'а ("Ongoing"/"Victory"/...), тут переклад за ключем (фікс-ревью, major: раніше показувалось англійською напряму).</summary>
        private static string OutcomeLabel(string rawOutcome, Game.Core.Characters.Creation.Gender g)
        {
            if (string.IsNullOrEmpty(rawOutcome)) return "";
            string key = "ui.battle.outcome." + rawOutcome.ToLowerInvariant();
            return UkrainianText.Has(key, g) ? UkrainianText.Get(key, g) : rawOutcome;
        }

        private void DrawGrid(GameShell shell, BattleView view, BattleUnitView current, Game.Core.Characters.Creation.Gender g)
        {
            if (view.Grid == null) return;

            var byPos = new Dictionary<(int, int), BattleUnitView> ();
            if (view.Units != null)
                foreach (var u in view.Units)
                    byPos[(u.Pos.X, u.Pos.Y)] = u;

            var reachable = new HashSet<(int, int)>();
            if (view.ReachableTiles != null)
                foreach (var p in view.ReachableTiles) reachable.Add((p.X, p.Y));

            for (int y = view.Grid.Height - 1; y >= 0; y--)
            {
                GUILayout.BeginHorizontal();
                for (int x = 0; x < view.Grid.Width; x++)
                {
                    BattleUnitView occupant;
                    byPos.TryGetValue((x, y), out occupant);
                    bool canMoveHere = reachable.Contains((x, y)) && occupant == null;

                    string label = occupant != null ? CellLabel(occupant) : "";
                    var previous = GUI.backgroundColor;
                    GUI.backgroundColor = CellColor(occupant, canMoveHere);

                    if (GUILayout.Button(label, GUILayout.Width(34f), GUILayout.Height(34f)))
                        OnCellClicked(shell, view, current, occupant, x, y, canMoveHere);

                    GUI.backgroundColor = previous;
                }
                GUILayout.EndHorizontal();
            }
        }

        private void OnCellClicked(GameShell shell, BattleView view, BattleUnitView current, BattleUnitView occupant, int x, int y, bool canMoveHere)
        {
            if (_awaitingOverwatchAim)
            {
                _awaitingOverwatchAim = false;
                if (current == null || (current.Pos.X == x && current.Pos.Y == y)) return;
                shell.TryRun(() => shell.Session.CombatEnterOverwatch(new GridPos(x, y)));
                return;
            }

            if (occupant != null && current != null && occupant.Id != current.Id)
            {
                _selectedTargetId = occupant.Id;
                shell.TryRun(() => shell.Session.CombatAttack(occupant.Id));
                return;
            }

            if (canMoveHere)
                shell.TryRun(() => shell.Session.CombatMove(new GridPos(x, y)));
        }

        private static string CellLabel(BattleUnitView u)
        {
            if (string.IsNullOrEmpty(u.Id)) return "?";
            return u.Id.Substring(0, 1).ToUpperInvariant();
        }

        private static Color CellColor(BattleUnitView occupant, bool canMoveHere)
        {
            if (occupant != null)
            {
                if (occupant.IsDowned) return new Color(0.4f, 0.4f, 0.4f);
                switch (occupant.Side)
                {
                    case "Player": return new Color(0.35f, 0.7f, 0.4f);
                    case "FromDefector": return new Color(0.8f, 0.55f, 0.2f);
                    default: return new Color(0.75f, 0.3f, 0.3f);
                }
            }
            return canMoveHere ? new Color(0.55f, 0.65f, 0.85f) : new Color(1f, 1f, 1f);
        }

        private static void DrawUnitList(GameShell shell, BattleView view, Game.Core.Characters.Creation.Gender g)
        {
            if (view.Units == null) return;
            foreach (var u in view.Units)
            {
                string name = BattleLogText.UnitName(view, u.Id, g);
                string hp = UkrainianText.Format("ui.battle.hp", g, "current", u.Hp.ToString(), "max", u.HpMax.ToString());
                string ap = UkrainianText.Format("ui.battle.ap", g, "current", u.Ap.ToString(), "max", u.ApMax.ToString());
                string tail = hp + " · " + ap + (u.IsOverwatching ? " · " + UkrainianText.Get("ui.battle.overwatch.indicator", g) : "");
                Widgets.LabeledRow(name, tail);
            }
        }

        /// <summary>
        /// BattleView.Log — ключі <c>combat.log.*</c> з аргументами (§4.2.1
        /// TEST_BUILD.md), слова — <see cref="BattleLogText"/>. Раніше тут був
        /// фолбек «не ключ — показати як є», і гравець бачив сирий російський
        /// трейс ядра: жоден його рядок ключем таблиці не був.
        /// </summary>
        private static void DrawLog(BattleView view, Game.Core.Characters.Creation.Gender g)
        {
            foreach (var line in BattleLogText.RecentLines(view, 8, g))
                GUILayout.Label(line, AlphaSkin.Tooltip);
        }

        private static BattleUnitView FindUnit(BattleView view, string id)
        {
            if (view.Units == null || string.IsNullOrEmpty(id)) return null;
            foreach (var u in view.Units) if (u.Id == id) return u;
            return null;
        }
    }
}
