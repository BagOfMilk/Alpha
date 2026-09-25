using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using UnityEngine;
// ArmedAction/BattleLogKind/... живуть у Game.Gameplay (батьківський
// неймспейс тут НЕ підключається неявно — C# не трактує "Game.Gameplay.UI"
// як вкладений у "Game.Gameplay" для пошуку імен).
using Game.Gameplay;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Бій v2 (docs/COMBAT_V2.md §3, доручення власника 25.09.2026 —
    /// «Боевка полный пиздец»): HUD бою переписаний з нуля під контракт
    /// <see cref="IBattleHudData"/>. Малює ЛИШЕ з цього інтерфейсу — жодного
    /// виклику <c>GameSession</c> тут немає; презентер (частина «3D»,
    /// <c>BattleArenaController</c>) сам вирішує, який <c>Combat*</c>-виклик
    /// означає клік/гарячу клавішу.
    ///
    /// Розкладка (§3): верхня смуга (раунд + стрічка ініціативи + індикатор
    /// правила влучання) → банер ходу сторони під нею → нижня панель дій по
    /// центру (картка юніта, здібності, кнопки) → журнал праворуч
    /// (згортається) → підказка біля курсора → оверлеї/спливаючі написи над
    /// юнітами на арені. <see cref="IBattleHudData.SetHudRects"/> кличеться
    /// щокадру з прямокутниками панелей, що блокують клік по арені (банер,
    /// оверлеї, підказка і спливаючі написи — НЕ блокують: вони прозорі для
    /// кліку, §3 «Не блокує кліки»).
    ///
    /// ЛІНТ: компілюється звичайним лінтом Gameplay (не виключено) — тут
    /// лише IMGUI + текстова таблиця, жодного Physics/Renderer/Camera.
    /// </summary>
    public static class BattleHudScreen
    {
        private static Vector2 _logScroll;
        private static bool _logCollapsed;
        private static bool _confirmAutoResolve;
        private static int _lastLogCount = -1;

        public static void Draw(IBattleHudData c)
        {
            if (c == null) return;
            GUI.skin = AlphaSkin.Build();

            var blockingRects = new List<Rect>();

            if (c.ResultPending)
            {
                blockingRects.Add(FullScreenRect());
                c.SetHudRects(blockingRects);
                DrawResultPanel(c);
                return;
            }

            var view = c.View;
            if (view == null)
            {
                // Крайовий випадок шва E1b/E2: Enter(session) викликаний, поки
                // GetBattleView() ще/вже повертає null. Порожній екран без
                // жодного виходу порушував би «кожен стан має видимий шлях
                // вперед» (Поправка №1) — мінімум повідомлення й вихід назад.
                blockingRects.Add(FullScreenRect());
                c.SetHudRects(blockingRects);
                DrawUnavailablePanel(c);
                return;
            }

            HandleHotkeys(c, view);

            float scale = Widgets.ScaleForScreen();
            float pad = Widgets.ScreenPadding();

            var topRect = new Rect(0f, 0f, Screen.width, TopBarHeight(scale));
            GUILayout.BeginArea(topRect, GUI.skin.box);
            DrawTopBar(c, view);
            GUILayout.EndArea();
            blockingRects.Add(topRect);

            float rightWidth = RightPanelWidth();
            var rightRect = new Rect(Screen.width - rightWidth - pad, topRect.height + pad,
                rightWidth, Screen.height - topRect.height - pad * 2f);
            GUILayout.BeginArea(rightRect, GUI.skin.box);
            DrawLogPanel(c);
            GUILayout.EndArea();
            blockingRects.Add(rightRect);

            float bottomWidth = Clamp(Screen.width - rightWidth - pad * 3f, 480f, 1180f);
            float bottomHeight = BottomPanelHeight(scale);
            var bottomRect = new Rect((Screen.width - rightWidth - pad - bottomWidth) * 0.5f,
                Screen.height - bottomHeight - pad, bottomWidth, bottomHeight);
            GUILayout.BeginArea(bottomRect, GUI.skin.box);
            DrawActionPanel(c, view);
            GUILayout.EndArea();
            blockingRects.Add(bottomRect);

            c.SetHudRects(blockingRects);

            // Те, що йде нижче, малюється ПОВЕРХ уже намальованих панелей і
            // навмисно НЕ входить у blockingRects — банер/оверлеї/спливаючі
            // написи/підказка не мають ловити клік, призначений арені (§3).
            DrawBanner(c, topRect);
            DrawOverlays(c, view);
            DrawFloatingTexts(c);
            DrawCursorTooltip(c, view);
        }

        // ================= гарячі клавіші (§3) =================

        /// <summary>
        /// [1..9] — здібності поточного юніта (той самий порядок, що кнопки
        /// нижче), [O] — Дозор, [Пробіл] — Кінець ходу (свій хід) або
        /// Прискорити (хід ворога). Нічого не приймає, поки йдуть такти
        /// (<see cref="IBattleInput.IsBusy"/>) — інакше клавіша, натиснута під
        /// час анімації, озброїла б дію, що гравець уже не бачив натисненою.
        /// </summary>
        private static void HandleHotkeys(IBattleHudData c, BattleView view)
        {
            var evt = Event.current;
            if (evt == null || evt.type != EventType.KeyDown || c.IsBusy) return;

            if (c.IsPlayerTurn)
            {
                var current = FindUnit(view, view.CurrentUnitId);
                if (current?.Abilities != null)
                {
                    for (int i = 0; i < current.Abilities.Count && i < 9; i++)
                    {
                        if (evt.keyCode != (KeyCode)((int)KeyCode.Alpha1 + i)) continue;
                        var ability = current.Abilities[i];
                        bool armed = c.Armed == ArmedAction.Ability && c.ArmedAbilityId == ability.Id;
                        if (armed) c.CancelArmed();
                        else if (AbilityUsable(current, ability)) c.ArmAbility(ability.Id);
                        evt.Use();
                        return;
                    }
                }

                if (evt.keyCode == KeyCode.O)
                {
                    if (c.Armed == ArmedAction.OverwatchAim) c.CancelArmed();
                    else c.ArmOverwatchAim();
                    evt.Use();
                    return;
                }

                if (evt.keyCode == KeyCode.Space)
                {
                    c.RequestEndTurn();
                    evt.Use();
                }
            }
            else if (evt.keyCode == KeyCode.Space)
            {
                c.FastEnemyTurns = !c.FastEnemyTurns;
                evt.Use();
            }
        }

        private static bool AbilityUsable(BattleUnitView unit, BattleAbilityView ability)
            => ability.CooldownRemaining <= 0 && unit.Ap >= ability.ApCost;

        // ================= верхня смуга (§3) =================

        private static void DrawTopBar(IBattleHudData c, BattleView view)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(UkrainianText.Format("ui.battle.round", false, "round", view.Round.ToString(CultureInfo.InvariantCulture)),
                AlphaSkin.SubHeader, GUILayout.ExpandWidth(false));
            GUILayout.Space(16f);
            DrawInitiativeStrip(c, view);
            GUILayout.FlexibleSpace();
            string ruleKey = view.IsHitRulePercent ? "ui.title.hitrule.percent" : "ui.title.hitrule.threshold";
            GUILayout.Label(UkrainianText.Get(ruleKey, false), AlphaSkin.Tooltip, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Картки ініціативи: смужка кольору сторони, ім'я з порядковим
        /// номером (через <see cref="IBattleHudData.ResolveDisplayName"/>),
        /// тонка смужка HP; поточний — акцентний колір, звалений — сірий.
        /// Загортає в новий ряд, коли не влазить (фікс-ревью старого HUD,
        /// той самий клас бага з довгими іменами-дублікатами).
        /// </summary>
        private static void DrawInitiativeStrip(IBattleHudData c, BattleView view)
        {
            if (view.InitiativeOrder == null) return;
            float maxWidth = Screen.width * 0.55f;
            float rowWidth = 0f;
            bool rowOpen = false;

            foreach (var id in view.InitiativeOrder)
            {
                var unit = FindUnit(view, id);
                if (unit == null) continue;
                string name = c.ResolveDisplayName(unit);
                bool current = string.Equals(id, view.CurrentUnitId, StringComparison.Ordinal);
                float w = Widgets.BadgeWidth(name);

                if (rowOpen && rowWidth + w > maxWidth) { GUILayout.EndHorizontal(); rowOpen = false; rowWidth = 0f; }
                if (!rowOpen) { GUILayout.BeginHorizontal(); rowOpen = true; }

                var tint = unit.IsDowned ? AlphaSkin.BgRaised : (current ? AlphaSkin.BattleCurrentUnit : SideColor(unit.Side));
                Widgets.Badge(name, tint);
                rowWidth += w;
            }

            if (rowOpen) GUILayout.EndHorizontal();
        }

        // ================= банер ходу (§3) =================

        private static void DrawBanner(IBattleHudData c, Rect topRect)
        {
            var banner = c.Banner;
            if (banner == null || string.IsNullOrEmpty(banner.Text) || banner.Alpha <= 0f) return;

            float width = Clamp(Screen.width * 0.4f, 360f, 720f);
            float height = 40f * Widgets.ScaleForScreen();
            var rect = new Rect((Screen.width - width) * 0.5f, topRect.height + 6f, width, height);

            var tint = banner.PlayerSide ? AlphaSkin.BattlePlayerSide : AlphaSkin.BattleEnemySide;
            var backdrop = new Color32(tint.r, tint.g, tint.b, (byte)(160 * Clamp01(banner.Alpha)));
            Widgets.SolidRect(rect, backdrop);

            var style = new GUIStyle(AlphaSkin.SubHeader) { alignment = TextAnchor.MiddleCenter };
            var previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Clamp01(banner.Alpha));
            GUI.Label(rect, banner.Text, style);
            GUI.color = previous;
        }

        // ================= нижня панель дій (§3) =================

        private static void DrawActionPanel(IBattleHudData c, BattleView view)
        {
            var current = FindUnit(view, view.CurrentUnitId);
            if (current == null)
            {
                GUILayout.Label(UkrainianText.Get("ui.battle.enemyturn", false), AlphaSkin.SubHeader);
                return;
            }

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(BottomPanelWidth() * 0.34f));
            DrawUnitCard(c, current);
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            DrawAbilities(c, view, current);
            GUILayout.Space(6f);
            DrawActionButtons(c, view, current);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(c.LastRejectionText))
                GUILayout.Label(c.LastRejectionText, AlphaSkin.DangerText);
        }

        /// <summary>
        /// Картка юніта: ім'я, HP числом і смужкою, ОД піпсами і «ОД 7/9»,
        /// зброя «назва · удар N ОД · дальність N», стани з тривалістю. На
        /// ході ворога — та сама картка, лише для читання (жодних кнопок
        /// поруч — ті малює <see cref="DrawActionButtons"/>, вимкнені).
        /// </summary>
        private static void DrawUnitCard(IBattleHudData c, BattleUnitView unit)
        {
            string name = c.ResolveDisplayName(unit);
            GUILayout.Label(UkrainianText.Format("ui.battle.current_unit", false, "name", name), AlphaSkin.SubHeader);

            GUILayout.Label(UkrainianText.Format("ui.battle.hp", false,
                "current", I(unit.Hp), "max", I(unit.HpMax)), AlphaSkin.Body);
            Widgets.FilledBarAt(GUILayoutBarRect(220f, 10f), FilledFraction(unit.Hp, unit.HpMax), SideColor(unit.Side));

            GUILayout.Space(4f);
            GUILayout.Label(UkrainianText.Format("ui.battle.ap", false, "current", I(unit.Ap), "max", I(unit.ApMax)), AlphaSkin.Body);
            Widgets.ProgressPips(unit.Ap, unit.ApMax, 14f);
            if (unit.ApReserved > 0)
                Widgets.TooltipLine(UkrainianText.Format("ui.battle.ap_reserved", false, "reserved", I(unit.ApReserved)));

            if (!string.IsNullOrEmpty(unit.WeaponId))
            {
                string weaponName = UkrainianText.Get(unit.WeaponId, false);
                string weaponLine = unit.AttackApCost > 0
                    ? UkrainianText.Format("ui.battle.weapon.detail", false,
                        "name", weaponName, "cost", I(unit.AttackApCost), "range", I(unit.WeaponRange))
                    : weaponName;
                GUILayout.Label(UkrainianText.Format("ui.battle.weapon", false, "name", weaponLine), AlphaSkin.Body);
            }

            // Ключ статусу — той самий переклад "Bleeding"→"combat.status.bleeding",
            // що вже дає BattleArenaView.StatusLabelKey (частина «3D») для
            // старого HUD: одна мапа сирого імені на ключ таблиці, не дублюємо
            // її тут другим списком case'ів.
            if (unit.StatusDetails != null && unit.StatusDetails.Count > 0)
            {
                GUILayout.BeginHorizontal();
                foreach (var status in unit.StatusDetails)
                {
                    string key = BattleArenaView.StatusLabelKey(status.Type);
                    if (key == null) continue;
                    string label = UkrainianText.Get(key, false) + " (" + UkrainianText.DeclineTurns(status.RemainingTurns) + ")";
                    Widgets.Badge(label, AlphaSkin.BattleStatus);
                }
                GUILayout.EndHorizontal();
            }
            else if (unit.Statuses != null && unit.Statuses.Count > 0)
            {
                GUILayout.BeginHorizontal();
                foreach (var s in unit.Statuses)
                {
                    string key = BattleArenaView.StatusLabelKey(s);
                    if (key == null) continue;
                    Widgets.Badge(UkrainianText.Get(key, false), AlphaSkin.BattleStatus);
                }
                GUILayout.EndHorizontal();
            }

            if (unit.IsOverwatching)
                Widgets.Badge(UkrainianText.Get("ui.battle.overwatch.indicator", false), AlphaSkin.BattleOverwatch);

            if (unit.IsDowned)
                GUILayout.Label(UkrainianText.Format("ui.battle.downed.window", false, "turns", UkrainianText.DeclineTurns(unit.DownWindowRemaining)),
                    AlphaSkin.DangerText);
        }

        /// <summary>Реальний список здібностей ПОТОЧНОГО юніта (не фіксований каталог) — [1..9], ціна, відкат, опис при наведенні, сіре з причиною.</summary>
        private static void DrawAbilities(IBattleHudData c, BattleView view, BattleUnitView current)
        {
            if (current.Abilities == null || current.Abilities.Count == 0) return;

            string hoveredDesc = null;
            const int perRow = 2;
            for (int row = 0; row * perRow < current.Abilities.Count; row++)
            {
                GUILayout.BeginHorizontal();
                for (int i = row * perRow; i < current.Abilities.Count && i < (row + 1) * perRow; i++)
                {
                    var ability = current.Abilities[i];
                    bool armed = c.Armed == ArmedAction.Ability && c.ArmedAbilityId == ability.Id;
                    string hotkey = (i + 1).ToString(CultureInfo.InvariantCulture);
                    string label = UkrainianText.Format(armed ? "ui.battle.ability.button.armed" : "ui.battle.ability.button", false,
                        "hotkey", hotkey, "name", UkrainianText.Get(ability.Id, false), "cost", I(ability.ApCost));

                    bool enemyTurn = !c.IsPlayerTurn;
                    bool onCooldown = ability.CooldownRemaining > 0;
                    bool notEnoughAp = current.Ap < ability.ApCost;

                    if (enemyTurn || onCooldown || notEnoughAp)
                    {
                        string reason = enemyTurn
                            ? UkrainianText.Get("ui.battle.enemyturn", false)
                            : (onCooldown
                                ? UkrainianText.Format("ui.battle.ability.cooldown", false, "turns", UkrainianText.DeclineTurns(ability.CooldownRemaining))
                                : UkrainianText.Get("ui.battle.ability.not_enough_ap", false));
                        Widgets.DisabledButton(label, reason);
                    }
                    else if (Widgets.SecondaryButton(label))
                    {
                        if (armed) c.CancelArmed(); else c.ArmAbility(ability.Id);
                    }

                    if (RectContainsMouse(GUILayoutUtility.GetLastRect())) hoveredDesc = ability.Id;
                }
                GUILayout.EndHorizontal();
            }

            string descKey = (hoveredDesc ?? (c.Armed == ArmedAction.Ability ? c.ArmedAbilityId : null)) + ".desc";
            if (hoveredDesc != null || c.Armed == ArmedAction.Ability)
                Widgets.TooltipLine(UkrainianText.Has(descKey, false) ? UkrainianText.Get(descKey, false) : string.Empty);

            if (c.Armed == ArmedAction.Ability)
                Widgets.TooltipLine(UkrainianText.Format("ui.battle.armed.ability", false,
                    "ability", UkrainianText.Get(c.ArmedAbilityId ?? string.Empty, false)));
        }

        private static void DrawActionButtons(IBattleHudData c, BattleView view, BattleUnitView current)
        {
            GUILayout.BeginHorizontal();

            if (c.IsPlayerTurn)
            {
                bool overwatchArmed = c.Armed == ArmedAction.OverwatchAim;
                string owLabel = (overwatchArmed ? "» " : string.Empty) + UkrainianText.Get("ui.battle.hotkey.overwatch", false);
                if (Widgets.SecondaryButton(owLabel))
                {
                    if (overwatchArmed) c.CancelArmed(); else c.ArmOverwatchAim();
                }
                if (overwatchArmed) Widgets.TooltipLine(UkrainianText.Get("ui.battle.armed.overwatch_aim", false));

                var downedAlly = FindAdjacentDownedAlly(view, current);
                if (downedAlly != null)
                {
                    if (Widgets.SecondaryButton(UkrainianText.Get("ui.battle.stabilize", false)))
                        c.RequestStabilize(downedAlly.Id);
                }

                if (Widgets.PrimaryButton(UkrainianText.Get("ui.battle.hotkey.endturn", false)))
                    c.RequestEndTurn();
            }
            else
            {
                bool fast = c.FastEnemyTurns;
                if (Widgets.TabButton(UkrainianText.Get("ui.battle.hotkey.fastforward", false), fast))
                    c.FastEnemyTurns = !fast;
            }

            GUILayout.EndHorizontal();

            if (c.Armed != ArmedAction.None)
                Widgets.TooltipLine(UkrainianText.Get("ui.battle.cancel", false));
        }

        /// <summary>Чебишовська відстань 1 від поточного юніта до звааленого союзника — «Стабілізувати» видно лише коли є кого (§3).</summary>
        private static BattleUnitView FindAdjacentDownedAlly(BattleView view, BattleUnitView current)
        {
            if (view.Units == null) return null;
            foreach (var u in view.Units)
            {
                if (!u.IsDowned || string.Equals(u.Id, current.Id, StringComparison.Ordinal)) continue;
                if (!string.Equals(u.Side, current.Side, StringComparison.Ordinal)) continue;
                if (Chebyshev(current.Pos, u.Pos) <= 1) return u;
            }
            return null;
        }

        // ================= журнал (§3) =================

        private static void DrawLogPanel(IBattleHudData c)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(UkrainianText.Get("ui.battle.log", false), AlphaSkin.SubHeader, GUILayout.ExpandWidth(true));
            if (Widgets.SecondaryButton(_logCollapsed ? "▸" : "▾", GUILayout.ExpandWidth(false)))
                _logCollapsed = !_logCollapsed;
            GUILayout.EndHorizontal();

            if (Widgets.SecondaryButton(UkrainianText.Get("ui.battle.autoresolve", false)))
                _confirmAutoResolve = true;

            if (_confirmAutoResolve)
            {
                Widgets.Modal(UkrainianText.Get("ui.battle.autoresolve.confirm.title", false), () =>
                {
                    GUILayout.Label(UkrainianText.Get("ui.battle.autoresolve.confirm.body", false), AlphaSkin.Body);
                    GUILayout.Space(10f);
                    GUILayout.BeginHorizontal();
                    if (Widgets.PrimaryButton(UkrainianText.Get("ui.common.confirm", false)))
                    {
                        _confirmAutoResolve = false;
                        c.RequestAutoResolve();
                    }
                    if (Widgets.SecondaryButton(UkrainianText.Get("ui.common.cancel", false)))
                        _confirmAutoResolve = false;
                    GUILayout.EndHorizontal();
                });
            }

            if (_logCollapsed) return;

            var entries = c.LogEntries;
            if (entries.Count != _lastLogCount)
            {
                _lastLogCount = entries.Count;
                _logScroll.y = 1_000_000f; // клемпиться самим скролвʼю до низу — нові рядки знизу (§3)
            }

            _logScroll = Widgets.ScrollListBegin(_logScroll, GUILayout.ExpandHeight(true));
            int lastRound = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Kind == BattleLogKind.Round && e.Round != lastRound && i > 0)
                    GUILayout.Box(string.Empty, GUILayout.Height(1f), GUILayout.ExpandWidth(true));
                lastRound = e.Round;
                GUILayout.Label(e.Text, StyleForLogKind(e.Kind));
            }
            Widgets.ScrollListEnd();
        }

        // ================= підказка біля курсора (§3) =================

        private static void DrawCursorTooltip(IBattleHudData c, BattleView view)
        {
            var evt = Event.current;
            if (evt == null) return;
            float mx = evt.mousePosition.x, my = evt.mousePosition.y;

            var attack = c.HoverAttack;
            var path = c.HoverPath;
            if (attack == null && path == null) return;

            float width = 260f;
            var area = new Rect(Clamp(mx + 18f, 0f, Screen.width - width), Clamp(my + 18f, 0f, Screen.height - 160f), width, 150f);

            GUILayout.BeginArea(area, GUI.skin.box);
            if (attack != null) DrawHoverAttack(attack);
            else DrawHoverPath(c, view, path);
            GUILayout.EndArea();
        }

        private static void DrawHoverAttack(AttackPreviewView p)
        {
            if (p.HasAttackRoll)
            {
                string chanceKey = p.IsPercent ? "ui.battle.hitchance.percent" : "ui.battle.hitchance.threshold";
                GUILayout.Label(UkrainianText.Format(chanceKey, false, "value", I(p.Chance)), AlphaSkin.SubHeader);

                if (p.Terms != null)
                    foreach (var term in p.Terms)
                    {
                        if (term.ChanceDelta == 0) continue;
                        string label = UkrainianText.Has("ui.battle.term." + term.Key, false)
                            ? UkrainianText.Get("ui.battle.term." + term.Key, false) : term.Key;
                        string sign = term.ChanceDelta > 0 ? "+" : string.Empty;
                        GUILayout.Label(label + " " + sign + I(term.ChanceDelta), AlphaSkin.Tooltip);
                    }

                string damageLine = p.IsDamageDeterministic
                    ? I(p.DamageExpected)
                    : (p.DamageCrit > p.DamageMax
                        ? UkrainianText.Format("ui.battle.damage.preview.crit", false, "min", I(p.DamageMin), "max", I(p.DamageMax), "crit", I(p.DamageCrit))
                        : UkrainianText.Format("ui.battle.damage.preview", false, "min", I(p.DamageMin), "max", I(p.DamageMax)));
                GUILayout.Label(damageLine, AlphaSkin.Body);
            }

            GUILayout.Label(UkrainianText.Format("ui.battle.ap_cost", false, "cost", I(p.ApCost)), AlphaSkin.Body);

            if (!p.CoverIgnored && !string.Equals(p.Cover, "None", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(p.Cover))
                GUILayout.Label(UkrainianText.Get("ui.battle.cover." + p.Cover.ToLowerInvariant(), false), AlphaSkin.Tooltip);

            if (p.Result != "Success")
                GUILayout.Label(UkrainianText.Get(RejectionKey(p.Result), false), AlphaSkin.DangerText);
        }

        private static void DrawHoverPath(IBattleHudData c, BattleView view, MovePathView path)
        {
            if (path.Result == "Success")
                GUILayout.Label(UkrainianText.Format("ui.battle.move.cost", false, "cost", I(path.ApCost)), AlphaSkin.Body);
            else if (path.Result == "NotReachable")
                GUILayout.Label(UkrainianText.Get("ui.battle.move.unreachable", false), AlphaSkin.DangerText);
            else
                GUILayout.Label(UkrainianText.Get(RejectionKey(path.Result), false), AlphaSkin.DangerText);

            if (view.Grid != null && c.HasHoveredTile)
            {
                int idx = c.HoveredTileX + c.HoveredTileY * view.Grid.Width;
                if (view.Grid.TileCover != null && idx >= 0 && idx < view.Grid.TileCover.Count)
                {
                    string cover = view.Grid.TileCover[idx];
                    if (!string.IsNullOrEmpty(cover) && !string.Equals(cover, "None", StringComparison.OrdinalIgnoreCase))
                        GUILayout.Label(UkrainianText.Get("ui.battle.cover." + cover.ToLowerInvariant(), false), AlphaSkin.Tooltip);
                }
            }

            if (path.OverwatchThreatTiles != null && c.HasHoveredTile)
                foreach (var t in path.OverwatchThreatTiles)
                    if (t.X == c.HoveredTileX && t.Y == c.HoveredTileY)
                    {
                        GUILayout.Label(UkrainianText.Get("ui.battle.move.threatened", false), AlphaSkin.DangerText);
                        break;
                    }
        }

        /// <summary>
        /// <see cref="AttackPreviewView.Result"/>/<see cref="MovePathView.Result"/> —
        /// та сама назва, що <c>CombatActionResult</c> (NotEnoughAp, OutOfRange, …);
        /// приведена до нижнього регістру вона ЗБІГАЄТЬСЯ з хвостом наявних
        /// ключів <c>ui.battle.action.rejected.*</c> (notenoughap, outofrange, …) —
        /// один рядок замість перемикача на кожне значення.
        /// </summary>
        private static string RejectionKey(string result)
        {
            if (string.IsNullOrEmpty(result)) return "ui.battle.action.rejected";
            string key = "ui.battle.action.rejected." + result.ToLowerInvariant();
            return UkrainianText.Has(key, false) ? key : "ui.battle.action.rejected";
        }

        // ================= оверлеї й спливаючі написи над арeною (§3, §6) =================

        private static void DrawOverlays(IBattleHudData c, BattleView view)
        {
            if (c.Overlays == null) return;
            foreach (var ov in c.Overlays)
            {
                if (!ov.OnScreen) continue;
                var unit = FindUnit(view, ov.UnitId);
                if (unit == null) continue;
                DrawUnitOverlay(c, unit, ov);
            }
        }

        private static void DrawUnitOverlay(IBattleHudData c, BattleUnitView unit, BattleUnitOverlay ov)
        {
            string name = c.ResolveDisplayName(unit);
            float nameWidth = Clamp(name.Length * AlphaSkin.OverlayNameFontSize * 0.62f + 12f, 60f, 220f);
            var nameRect = new Rect(ov.ScreenX - nameWidth * 0.5f, ov.ScreenY, nameWidth, AlphaSkin.OverlayNameFontSize + 6f);
            Widgets.SolidRect(nameRect, new Color32(12, 10, 8, 190));

            var style = new GUIStyle(AlphaSkin.OverlayName);
            var tint = unit.IsDowned ? AlphaSkin.BgRaised : (ov.IsCurrent ? AlphaSkin.BattleCurrentUnit : SideColor(unit.Side));
            style.normal.textColor = tint;
            GUI.Label(nameRect, name, style);

            float hpY = nameRect.y + nameRect.height + 2f;
            var hpRect = new Rect(nameRect.x, hpY, nameRect.width, 4f);
            Widgets.FilledBarAt(hpRect, FilledFraction(unit.Hp, unit.HpMax), tint);

            float badgeY = hpY + 4f + 2f;
            if (unit.IsOverwatching)
            {
                var badge = new Rect(nameRect.x, badgeY, nameRect.width, 16f);
                Widgets.SolidRect(badge, AlphaSkin.BattleOverwatch);
                GUI.Label(badge, UkrainianText.Get("ui.battle.overlay.overwatch", false), new GUIStyle(AlphaSkin.OverlayName) { normal = { textColor = AlphaSkin.BgDark } });
                badgeY += 18f;
            }

            if (unit.IsDowned)
            {
                var badge = new Rect(nameRect.x, badgeY, nameRect.width, 16f);
                Widgets.SolidRect(badge, AlphaSkin.BattleEnemySide);
                GUI.Label(badge, UkrainianText.Format("ui.battle.overlay.downed", false, "turns", I(unit.DownWindowRemaining)),
                    new GUIStyle(AlphaSkin.OverlayName) { normal = { textColor = AlphaSkin.TextMain } });
            }
        }

        private static void DrawFloatingTexts(IBattleHudData c)
        {
            if (c.FloatingTexts == null) return;
            foreach (var f in c.FloatingTexts)
            {
                var style = f.Big ? AlphaSkin.CritText : new GUIStyle(AlphaSkin.OverlayName) { fontSize = AlphaSkin.BodyFontSize };
                if (!f.Big) style.normal.textColor = AlphaSkin.BattleLogColor(f.Kind);

                var previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Clamp01(f.Alpha));
                var rect = new Rect(f.ScreenX - 60f, f.ScreenY - 24f, 120f, 32f);
                GUI.Label(rect, f.Text, style);
                GUI.color = previous;
            }
        }

        // ================= бою немає (крайовий випадок шва) =================

        private static void DrawUnavailablePanel(IBattleHudData c)
        {
            Widgets.Modal(UkrainianText.Get("ui.battle.title", false), () =>
            {
                GUILayout.Label(UkrainianText.Get("ui.battle.unavailable", false), AlphaSkin.Body);
                GUILayout.Space(14f);
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.battle.unavailable.exit", false)))
                    c.AcknowledgeResult();
            });
        }

        // ================= результат бою (новий стиль) =================

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
                    foreach (var line in c.ResultCasualtyLines) GUILayout.Label(line, AlphaSkin.DangerText);

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

        // ================= кольори (§2) =================

        private static Color32 SideColor(string side)
        {
            switch (side)
            {
                case "Player": return AlphaSkin.BattlePlayerSide;
                case "FromDefector": return AlphaSkin.BattleDefectorSide;
                default: return AlphaSkin.BattleEnemySide;
            }
        }

        private static GUIStyle StyleForLogKind(BattleLogKind kind)
        {
            var style = new GUIStyle(AlphaSkin.Body) { fontStyle = FontStyle.Normal };
            style.normal.textColor = AlphaSkin.BattleLogColor(kind);
            if (kind == BattleLogKind.Crit) style.fontStyle = FontStyle.Bold;
            return style;
        }

        // ================= розкладка (§3: 1920×1080/1280×720) =================

        private static float TopBarHeight(float scale) => Clamp(44f * scale, 40f, 56f);
        private static float BottomPanelHeight(float scale) => Clamp(210f * scale, 200f, 260f);
        private static float RightPanelWidth() => Screen.width >= 1600f ? 360f : 300f;
        private static float BottomPanelWidth() => Clamp(Screen.width - RightPanelWidth() - Widgets.ScreenPadding() * 3f, 480f, 1180f);
        private static Rect FullScreenRect() => new Rect(0f, 0f, Screen.width, Screen.height);

        /// <summary>Прямокутник під смужку HP картки юніта — той самий трюк, що GUILayoutUtility.GetLastRect() у Widgets, але з фіксованою шириною.</summary>
        private static Rect GUILayoutBarRect(float width, float height)
        {
            GUILayout.Box(string.Empty, GUILayout.Width(width), GUILayout.Height(height));
            return GUILayoutUtility.GetLastRect();
        }

        // ================= допоміжне =================

        private static BattleUnitView FindUnit(BattleView view, string id)
        {
            if (string.IsNullOrEmpty(id) || view.Units == null) return null;
            foreach (var u in view.Units)
                if (string.Equals(u.Id, id, StringComparison.Ordinal)) return u;
            return null;
        }

        private static int Chebyshev(GridPosView a, GridPosView b)
            => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        private static bool RectContainsMouse(Rect r)
        {
            var evt = Event.current;
            if (evt == null) return false;
            float mx = evt.mousePosition.x, my = evt.mousePosition.y;
            return mx >= r.x && mx <= r.x + r.width && my >= r.y && my <= r.y + r.height;
        }

        private static float FilledFraction(int value, int max) => max <= 0 ? 0f : Clamp01((float)value / max);

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

        // Немає Mathf у заглушці лінту (Widgets.cs — той самий прийом): свій Clamp.
        private static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
        private static float Clamp01(float value) => Clamp(value, 0f, 1f);
    }
}
