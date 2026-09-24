using Game.Core.Session.Views;
using Game.Gameplay.Text;
using UnityEngine;
// ArmedAction/BattleArenaController/BattleArenaView живуть у Game.Gameplay
// (батьківський неймспейс тут НЕ підключається неявно — C# не трактує
// "Game.Gameplay.UI" як вкладений у "Game.Gameplay" для пошуку імен).
using Game.Gameplay;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// IMGUI бойового HUD (пакет E2), малюється з <see cref="BattleArenaController.DrawHud"/>.
    /// Читає лише публічний зріз контролера (<c>View</c>/<c>ResultPending</c>/
    /// <c>LogLines</c>/…) — жодної команди <c>GameSession</c> тут немає:
    /// кнопки викликають <c>Request*</c>/<c>Arm*</c> контролера, а той уже
    /// вирішує, який <c>Combat*</c>-виклик це означає (та ж межа, що і в
    /// інших *Screen.cs — Widgets нічого не знає про GameSession, екран
    /// нічого не знає про Game.Core.Combat).
    ///
    /// ЛІНТ: компілюється звичайним лінтом Gameplay (не виключено) — тут
    /// лише IMGUI + текстова таблиця, жодного Physics/Renderer/Camera.
    /// </summary>
    public static class BattleHudScreen
    {
        private static Vector2 _logScroll;

        public static void Draw(IBattleHudData controller)
        {
            if (controller == null) return;

            GUI.skin = AlphaSkin.Build();

            if (controller.ResultPending)
            {
                DrawResultPanel(controller);
                return;
            }

            var view = controller.View;
            if (view == null) return;

            float padding = Widgets.ScreenPadding();
            float width = Clamp(Screen.width * 0.34f, 420f, 620f);
            float height = Screen.height - padding * 2f;
            var area = new Rect(padding, padding, width, height);

            GUILayout.BeginArea(area);
            Widgets.Panel(UkrainianText.Get("ui.battle.title", false), () => DrawBody(controller, view));
            GUILayout.EndArea();
        }

        // ================= тіло HUD =================

        private static void DrawBody(IBattleHudData c, BattleView view)
        {
            DrawHitRuleIndicator(view);
            GUILayout.Space(6f);
            DrawInitiativeStrip(c, view);
            GUILayout.Space(6f);
            DrawCurrentUnit(c, view);
            GUILayout.Space(6f);
            DrawHitChancePreview(c, view);
            GUILayout.Space(10f);
            DrawAbilities(c);
            GUILayout.Space(10f);
            DrawActionButtons(c);
            GUILayout.Space(10f);
            DrawLog(c);
        }

        private static void DrawHitRuleIndicator(BattleView view)
        {
            string key = view.IsHitRulePercent ? "ui.title.hitrule.percent" : "ui.title.hitrule.threshold";
            GUILayout.Label(UkrainianText.Get(key, false), AlphaSkin.Tooltip);
        }

        private static void DrawInitiativeStrip(IBattleHudData c, BattleView view)
        {
            Widgets.Section(UkrainianText.Get("ui.battle.initiative", false), () =>
            {
                GUILayout.BeginHorizontal();
                if (view.InitiativeOrder != null)
                    foreach (var id in view.InitiativeOrder)
                    {
                        var unit = FindUnit(view, id);
                        string name = unit != null ? c.ResolveDisplayName(unit) : id;
                        bool current = string.Equals(id, view.CurrentUnitId, System.StringComparison.Ordinal);
                        Widgets.Badge(name, current ? AlphaSkin.Accent : AlphaSkin.BgRaised);
                    }
                GUILayout.EndHorizontal();
            });
        }

        private static void DrawCurrentUnit(IBattleHudData c, BattleView view)
        {
            var current = FindUnit(view, view.CurrentUnitId);
            if (current == null)
            {
                GUILayout.Label(UkrainianText.Get("ui.battle.enemyturn", false), AlphaSkin.Body);
                return;
            }

            string name = c.ResolveDisplayName(current);
            GUILayout.Label(UkrainianText.Format("ui.battle.current_unit", false, "name", name), AlphaSkin.SubHeader);

            if (!c.IsPlayerTurn)
            {
                GUILayout.Label(UkrainianText.Get("ui.battle.enemyturn", false), AlphaSkin.Tooltip);
                return;
            }

            DrawApBar(current);
            if (current.IsDowned) GUILayout.Label(UkrainianText.Get("ui.battle.unit.downed", false), AlphaSkin.Tooltip);
        }

        private static void DrawApBar(BattleUnitView unit)
        {
            GUILayout.Label(UkrainianText.Format("ui.battle.ap", false,
                "current", unit.Ap.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "max", unit.ApMax.ToString(System.Globalization.CultureInfo.InvariantCulture)), AlphaSkin.Body);

            float filled = BattleArenaView.FilledFraction(unit.Ap, unit.ApMax);
            float reserved = BattleArenaView.ReservedFraction(unit.ApMax, unit.ApReserved);
            DrawFractionBar(filled, reserved);

            if (unit.ApReserved > 0)
                Widgets.TooltipLine(UkrainianText.Format("ui.battle.ap_reserved", false,
                    "reserved", unit.ApReserved.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            if (unit.IsOverwatching)
                Widgets.Badge(UkrainianText.Get("ui.battle.overwatch.indicator", false), AlphaSkin.Accent);
        }

        /// <summary>
        /// Смужка AP: акцентний бар — заповнена частка; тонша смужка під ним,
        /// іншим (тривожним) кольором і коротша на частку <paramref name="reservedFraction"/> —
        /// скільки з максимуму «заморожено» резервом дозору (§BattleArenaView.
        /// ReservedFraction). Два бари, не один накладений, — простіше й чесно
        /// показує IMGUI без власного шейдера маски.
        /// </summary>
        private static void DrawFractionBar(float filledFraction, float reservedFraction)
        {
            const float barWidth = 220f;
            var previous = GUI.backgroundColor;

            GUI.backgroundColor = AlphaSkin.Accent;
            GUILayout.Box(string.Empty, GUILayout.Height(14f), GUILayout.Width(Clamp01(filledFraction) * barWidth));
            GUI.backgroundColor = previous;

            if (reservedFraction > 0f)
            {
                GUI.backgroundColor = AlphaSkin.Danger;
                GUILayout.Box(string.Empty, GUILayout.Height(5f), GUILayout.Width(Clamp01(reservedFraction) * barWidth));
                GUI.backgroundColor = previous;
            }
        }

        private static void DrawHitChancePreview(IBattleHudData c, BattleView view)
        {
            if (string.IsNullOrEmpty(c.HoveredUnitId)) return;
            string key = view.IsHitRulePercent ? "ui.battle.hitchance.percent" : "ui.battle.hitchance.threshold";
            GUILayout.Label(UkrainianText.Format(key, false, "value",
                c.HoveredHitChance.ToString(System.Globalization.CultureInfo.InvariantCulture)), AlphaSkin.Body);
        }

        private static void DrawAbilities(IBattleHudData c)
        {
            if (!c.IsPlayerTurn) return;

            Widgets.Section(UkrainianText.Get("ui.battle.abilities", false), () =>
            {
                GUILayout.BeginHorizontal();
                foreach (var abilityId in BattleArenaView.KnownAbilityIds)
                {
                    bool armed = c.Armed == ArmedAction.Ability && c.ArmedAbilityId == abilityId;
                    string label = UkrainianText.Get(abilityId, false);
                    if (armed) label = "» " + label;

                    if (Widgets.SecondaryButton(label))
                        c.ArmAbility(armed ? null : abilityId);
                }
                GUILayout.EndHorizontal();

                if (c.Armed == ArmedAction.Ability)
                    Widgets.TooltipLine(UkrainianText.Format("ui.battle.armed.ability", false,
                        "ability", UkrainianText.Get(c.ArmedAbilityId ?? string.Empty, false)));
            });
        }

        private static void DrawActionButtons(IBattleHudData c)
        {
            GUILayout.BeginHorizontal();

            if (!c.IsPlayerTurn)
            {
                Widgets.DisabledButton(UkrainianText.Get("ui.battle.overwatch.button", false),
                    UkrainianText.Get("ui.battle.enemyturn", false));
                Widgets.DisabledButton(UkrainianText.Get("ui.battle.endturn", false),
                    UkrainianText.Get("ui.battle.enemyturn", false));
            }
            else
            {
                bool overwatchArmed = c.Armed == ArmedAction.OverwatchAim;
                if (Widgets.SecondaryButton((overwatchArmed ? "» " : string.Empty) + UkrainianText.Get("ui.battle.overwatch.button", false)))
                {
                    if (overwatchArmed) c.CancelArmed(); else c.ArmOverwatchAim();
                }

                if (Widgets.PrimaryButton(UkrainianText.Get("ui.battle.endturn", false)))
                    c.RequestEndTurn();
            }

            if (Widgets.SecondaryButton(UkrainianText.Get("ui.battle.autoresolve", false)))
                c.RequestAutoResolve();

            GUILayout.EndHorizontal();

            if (c.Armed != ArmedAction.None)
                Widgets.TooltipLine(UkrainianText.Get("ui.battle.cancel", false));
        }

        private static void DrawLog(IBattleHudData c)
        {
            Widgets.Section(UkrainianText.Get("ui.battle.log", false), () =>
            {
                _logScroll = Widgets.ScrollListBegin(_logScroll, GUILayout.Height(160f));
                foreach (var line in c.LogLines) GUILayout.Label(line, AlphaSkin.Body);
                Widgets.ScrollListEnd();
            });
        }

        // ================= результат бою =================

        private static void DrawResultPanel(IBattleHudData c)
        {
            string title = UkrainianText.Get(OutcomeTitleKey(c.ResultOutcomeKey), false);

            Widgets.Modal(title, () =>
            {
                if (!string.IsNullOrEmpty(c.ResultRounds))
                    GUILayout.Label(UkrainianText.Format("ui.battle.result.rounds", false, "rounds", c.ResultRounds), AlphaSkin.Body);

                GUILayout.Space(8f);
                GUILayout.Label(UkrainianText.Get("ui.battle.result.casualties.title", false), AlphaSkin.SubHeader);

                if (c.ResultCasualtyLines.Count == 0)
                    GUILayout.Label(UkrainianText.Get("ui.battle.result.casualties.none", false), AlphaSkin.Body);
                else
                    foreach (var line in c.ResultCasualtyLines) GUILayout.Label(line, AlphaSkin.Body);

                GUILayout.Space(14f);
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.battle.result.next", false)))
                    c.AcknowledgeResult();
            });
        }

        private static string OutcomeTitleKey(string outcome)
        {
            switch (outcome)
            {
                case "Victory": return "ui.battle.victory";
                case "Defeat": return "ui.battle.defeat";
                case "Retreat": return "ui.battle.outcome.retreat";
                case "Draw": return "ui.battle.outcome.draw";
                default: return "ui.battle.title";
            }
        }

        // ================= допоміжне =================

        private static BattleUnitView FindUnit(BattleView view, string id)
        {
            if (string.IsNullOrEmpty(id) || view.Units == null) return null;
            foreach (var u in view.Units)
                if (string.Equals(u.Id, id, System.StringComparison.Ordinal)) return u;
            return null;
        }

        // Немає Mathf у заглушці лінту (Widgets.cs — той самий прийом): свій Clamp.
        private static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
        private static float Clamp01(float value) => Clamp(value, 0f, 1f);
    }
}
