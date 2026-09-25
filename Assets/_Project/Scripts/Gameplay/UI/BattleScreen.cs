using System;
using System.Collections.Generic;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Запасний IMGUI-грид бою (R18 фолбек, коли <see cref="IBattlePresenter"/>
    /// не знайдено в сцені або кинув виняток): клітинки-кнопки, підсвічені
    /// досяжні тайли, клік по юніту ворога — атака, клік по порожній
    /// досяжній клітинці — рух. Читає ЛИШЕ <see cref="BattleView"/> і команди
    /// <c>Combat*</c> — жодного типу <c>Game.Core.Combat</c>, крім
    /// <see cref="GridPos"/>, яким і так параметризовані самі команди.
    ///
    /// Бій v2 (docs/COMBAT_V2.md §9, аудит HUD п.6 — «фолбек досі має ТОЙ
    /// САМИЙ фіксований каталог здібностей і мовчазну відмову, той самий баг,
    /// який власник уже критикував»): реальний список здібностей ПОТОЧНОГО
    /// юніта, видима причина відмови (<see cref="GameShell.TryRunReported{T}"/>,
    /// не <see cref="GameShell.TryRun"/> — інакше <c>CombatActionResult</c>
    /// відкидається без сліду), кольоровий журнал (<see cref="AlphaSkin.
    /// BattleLogColor"/> — той самий, що в HUD), і хід ШІ, який фолбек веде
    /// САМ таймером (§5: «фолбек-екран веде хід ворога так само, таймером,
    /// без тактів») — без цього ворог мовчки не робив нічого, щойно наставав
    /// його хід, і гра «зависала».
    /// </summary>
    public sealed class BattleScreen
    {
        private string _selectedTargetId;

        /// <summary>
        /// Фікс-ревью (major): очікуємо клік по сітці як напрямок дозору
        /// (GameSession.CombatEnterOverwatch(GridPos aim)) замість звичайного
        /// руху/атаки — той самий грид, інший сенс кліку, поки прапорець
        /// піднято.
        /// </summary>
        private bool _awaitingOverwatchAim;

        // ---- хід ШІ таймером (§5): CombatAiStepOneAction раз на ~0.5с, try/catch, запобіжник кроків ----
        private const float AiStepIntervalSeconds = 0.5f;
        private const int AiStepGuardLimit = 40; // той самий порядок, що §5 «> 40 кроків» для 3D-презентера
        private float _aiStepTimer;
        private string _aiGuardUnitId;
        private int _aiGuardCount;

        public void Draw(GameShell shell)
        {
            var g = shell.ProtagonistGender;
            var view = shell.Session.GetBattleView();
            if (view == null) { ResetAiGuard(); return; }

            DriveAiTurnIfNeeded(shell, view);

            Widgets.Panel(UkrainianText.Get("ui.battle.fallback.title", g), () =>
            {
                GUILayout.Label(UkrainianText.Format("ui.battle.round", g, "round", view.Round.ToString()) +
                                 " · " + OutcomeLabel(view.Outcome, g), AlphaSkin.SubHeader);

                var current = FindUnit(view, view.CurrentUnitId);
                if (current != null)
                    GUILayout.Label(UkrainianText.Format("ui.battle.current_unit", g,
                        "name", BattleLogText.UnitName(view, current.Id, g)), AlphaSkin.Body);
                else
                    GUILayout.Label(UkrainianText.Get("ui.battle.enemyturn", g), AlphaSkin.SubHeader);

                DrawGrid(shell, view, current, g);
                GUILayout.Space(8f);
                DrawUnitList(shell, view, g);

                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.battle.autoresolve", g)))
                    shell.TryRun(() => shell.Session.CombatAutoResolve());
                if (view.CurrentUnitId != null && current != null)
                {
                    if (Widgets.SecondaryButton(UkrainianText.Get("ui.battle.end_turn", g)))
                        shell.TryRunReported(() => shell.Session.CombatEndTurn(), r => RejectionText(r, g));
                    if (Widgets.TabButton(UkrainianText.Get("ui.battle.overwatch.button", g), _awaitingOverwatchAim))
                        _awaitingOverwatchAim = !_awaitingOverwatchAim;
                }
                GUILayout.EndHorizontal();

                if (_awaitingOverwatchAim)
                    Widgets.TooltipLine(UkrainianText.Get("ui.battle.overwatch.aim_hint", g));

                if (current != null) DrawAbilities(shell, current, g);

                // Фікс-ревью (major, той самий баг, що вже закритий у HUD):
                // GameShell.LastMessage (де TryRun/TryRunReported кладуть
                // причину відмови) тут ніде не показувався — фолбек малює
                // власну повноекранну панель (DrawFullScreen), не спільну
                // шапку хаба, що показує LastMessage деінде.
                if (!string.IsNullOrEmpty(shell.LastMessage))
                    GUILayout.Label(shell.LastMessage, AlphaSkin.DangerText);

                DrawLog(view, g);
            }, GUILayout.ExpandWidth(true));
        }

        /// <summary>
        /// §5: «Фолбек-екран веде хід ворога так само, таймером, без тактів» —
        /// без 3D презентера ніхто інший <c>GameSession.CombatAiStepOneAction</c>
        /// не кличе (автотур кличе лише «Кінець ходу» за ворога — коментар
        /// §0 п.1 аудиту), і гра застигала на ході ШІ так само, як була
        /// критика власника про основний HUD. Раз на ~0.5с — один крок ІІ;
        /// запобіжник (той самий порядок, що §5 3D-режисера — «> 40 кроків»)
        /// б'є по тому самому юніту примусовим <c>CombatEndTurn</c>, якщо він
        /// сам не звільняє хід.
        /// </summary>
        private void DriveAiTurnIfNeeded(GameShell shell, BattleView view)
        {
            if (!view.IsAiTurn) { ResetAiGuard(); return; }

            _aiStepTimer += Time.deltaTime;
            if (_aiStepTimer < AiStepIntervalSeconds) return;
            _aiStepTimer = 0f;

            if (string.Equals(view.CurrentUnitId, _aiGuardUnitId, StringComparison.Ordinal)) _aiGuardCount++;
            else { _aiGuardUnitId = view.CurrentUnitId; _aiGuardCount = 0; }

            try
            {
                if (_aiGuardCount > AiStepGuardLimit) shell.Session.CombatEndTurn();
                else shell.Session.CombatAiStepOneAction();
            }
            catch (InvalidOperationException)
            {
                // Бій завершився САМЕ цим кроком (одна дія ІІ добила
                // останнього юніта) — наступний Draw() побачить
                // GetBattleView() == null або новий Outcome, тут просто не
                // падаємо (§CombatAudit CORE п.1: AI-фасад кидає виняток,
                // якщо покликати його ще раз одразу після завершення бою).
            }
        }

        private void ResetAiGuard()
        {
            _aiStepTimer = 0f;
            _aiGuardUnitId = null;
            _aiGuardCount = 0;
        }

        /// <summary>
        /// Фікс-ревью (major, аудит HUD п.6): реальний список здібностей
        /// ПОТОЧНОГО юніта (<c>BattleUnitView.Abilities</c>), не фіксований
        /// каталог для будь-кого — і видима причина відмови/відкату/нестачі
        /// ОД замість мовчазного <c>shell.TryRun</c>.
        /// </summary>
        private void DrawAbilities(GameShell shell, BattleUnitView current, Gender g)
        {
            if (current.Abilities == null || current.Abilities.Count == 0) return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(UkrainianText.Get("ui.battle.abilities.label", g), AlphaSkin.Body, GUILayout.Width(90f));
            foreach (var ability in current.Abilities)
            {
                string label = UkrainianText.Format("ui.battle.ability.button.plain", g,
                    "name", UkrainianText.Get(ability.Id, g), "cost", ability.ApCost.ToString());

                bool onCooldown = ability.CooldownRemaining > 0;
                bool notEnoughAp = current.Ap < ability.ApCost;
                if (onCooldown || notEnoughAp)
                {
                    string reason = onCooldown
                        ? UkrainianText.Format("ui.battle.ability.cooldown", g, "turns", UkrainianText.DeclineTurns(ability.CooldownRemaining))
                        : UkrainianText.Get("ui.battle.ability.not_enough_ap", g);
                    Widgets.DisabledButton(label, reason, GUILayout.Width(220f));
                }
                else if (Widgets.SecondaryButton(label, GUILayout.Width(220f)))
                {
                    string abilityId = ability.Id;
                    shell.TryRunReported(() => shell.Session.CombatUseAbility(abilityId, _selectedTargetId, null),
                        r => RejectionText(r, g));
                }
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>BattleView.Outcome — сирий рядок enum'а ("Ongoing"/"Victory"/...), тут переклад за ключем.</summary>
        private static string OutcomeLabel(string rawOutcome, Gender g)
        {
            if (string.IsNullOrEmpty(rawOutcome)) return "";
            string key = "ui.battle.outcome." + rawOutcome.ToLowerInvariant();
            return UkrainianText.Has(key, g) ? UkrainianText.Get(key, g) : rawOutcome;
        }

        /// <summary>
        /// <c>CombatActionResult</c> → рядок причини (Поправка №1, «шлях
        /// завжди видно»): та сама лишень таблиця <c>ui.battle.action.
        /// rejected.*</c>, що вже показує HUD (<see cref="BattleHudScreen"/>).
        /// null — дія пройшла, TryRunReported тоді не чіпає LastMessage.
        /// </summary>
        private static string RejectionText(CombatActionResult result, Gender g)
        {
            if (result == CombatActionResult.Success) return null;
            string key = "ui.battle.action.rejected." + result.ToString().ToLowerInvariant();
            return UkrainianText.Has(key, g) ? UkrainianText.Get(key, g) : UkrainianText.Get("ui.battle.action.rejected", g);
        }

        private void DrawGrid(GameShell shell, BattleView view, BattleUnitView current, Gender g)
        {
            if (view.Grid == null) return;

            var byPos = new Dictionary<(int, int), BattleUnitView>();
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
                        OnCellClicked(shell, view, current, occupant, x, y, canMoveHere, g);

                    GUI.backgroundColor = previous;
                }
                GUILayout.EndHorizontal();
            }
        }

        private void OnCellClicked(GameShell shell, BattleView view, BattleUnitView current, BattleUnitView occupant,
                                    int x, int y, bool canMoveHere, Gender g)
        {
            if (_awaitingOverwatchAim)
            {
                _awaitingOverwatchAim = false;
                if (current == null || (current.Pos.X == x && current.Pos.Y == y)) return;
                shell.TryRunReported(() => shell.Session.CombatEnterOverwatch(new GridPos(x, y)), r => RejectionText(r, g));
                return;
            }

            if (occupant != null && current != null && occupant.Id != current.Id)
            {
                _selectedTargetId = occupant.Id;
                shell.TryRunReported(() => shell.Session.CombatAttack(occupant.Id), r => RejectionText(r, g));
                return;
            }

            if (canMoveHere)
                shell.TryRunReported(() => shell.Session.CombatMove(new GridPos(x, y)), r => RejectionText(r, g));
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
                    case "Player": return new Color(0.30f, 0.60f, 1.00f);
                    case "FromDefector": return new Color(0.70f, 0.40f, 0.95f);
                    default: return new Color(0.92f, 0.28f, 0.22f);
                }
            }
            return canMoveHere ? new Color(0.55f, 0.65f, 0.85f) : new Color(1f, 1f, 1f);
        }

        private static void DrawUnitList(GameShell shell, BattleView view, Gender g)
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
        /// Кольоровий журнал (аудит HUD, inventory «Кольоровий... журнал бою»):
        /// той самий переклад і той самий колір, що в HUD арени —
        /// <see cref="BattleLogText.Entry"/> + <see cref="AlphaSkin.BattleLogColor"/>,
        /// а не суцільний тьмяний курсив, яким фолбек малював журнал раніше.
        /// </summary>
        private static void DrawLog(BattleView view, Gender g)
        {
            if (view.Log == null) return;
            int start = Math.Max(0, view.Log.Count - 8);
            for (int i = start; i < view.Log.Count; i++)
            {
                var entry = BattleLogText.Entry(view.Log[i], view, g);
                if (entry == null || string.IsNullOrEmpty(entry.Text)) continue;
                var style = new GUIStyle(AlphaSkin.Body) { fontSize = AlphaSkin.BodyFontSize - 2 };
                style.normal.textColor = AlphaSkin.BattleLogColor(entry.Kind);
                GUILayout.Label(entry.Text, style);
            }
        }

        private static BattleUnitView FindUnit(BattleView view, string id)
        {
            if (view.Units == null || string.IsNullOrEmpty(id)) return null;
            foreach (var u in view.Units) if (u.Id == id) return u;
            return null;
        }
    }
}
