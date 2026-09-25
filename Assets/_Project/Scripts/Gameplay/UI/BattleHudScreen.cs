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

            if (c.ResultPending) _confirmAutoResolve = false; // бій скінчився — питання автобою знято

            // Панель результату — лише коли дограно такти фінальної дії: удар, що
            // вирішив бій, спершу видно на арені (рев'ю Бою v2).
            if (c.ResultPending && !c.IsBusy)
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

            float bottomWidth = BottomPanelWidth();
            float bottomHeight = BottomPanelHeight(c, view);
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

            // Підтвердження автобою — на верхньому рівні, не всередині панелі журналу:
            // там модалка обрізалась межами області й була недоступна (рев'ю Бою v2).
            if (_confirmAutoResolve) DrawAutoResolveConfirm(c);
        }

        private static void DrawAutoResolveConfirm(IBattleHudData c)
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
            }, () => _confirmAutoResolve = false);
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
            if (evt == null || evt.type != EventType.KeyDown || c.IsBusy || c.Paused || c.ResultPending) return;

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
            // Аудит знімків п.5: «Правило влучання вгорі праворуч — не
            // курсивом, читабельно» — AlphaSkin.Tooltip курсивний і тьмяний,
            // тут потрібен звичайний світлий HintLine (§AlphaSkin.HintLine).
            string ruleKey = view.IsHitRulePercent ? "ui.title.hitrule.percent" : "ui.title.hitrule.threshold";
            GUILayout.Label(UkrainianText.Get(ruleKey, false), AlphaSkin.HintLine, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
        }

        /// <summary>Ширина зайва понад <see cref="Widgets.BadgeWidth"/> для рамки поточного юніта (§Widgets.BorderedBadge) — так підрахунок переносу рядка (нижче) не недооцінює ширший бейдж.</summary>
        private const float InitiativeBorderPad = 8f;
        /// <summary>Товщина смужки HP під бейджем черги ходу (§3 «під іменем — тонка HP-смужка»).</summary>
        private const float InitiativeHpBarHeight = 4f;

        /// <summary>
        /// Картки ініціативи: тло кольору сторони, ім'я з порядковим номером
        /// (через <see cref="IBattleHudData.ResolveDisplayName"/>), тонка
        /// смужка HP під іменем; звалений — сірий. Поточний юніт —
        /// золоте тло з ТЕМНИМ текстом і рамкою (не світлий текст на
        /// світлому тлі — фікс-ревью, аудит знімків п.2: «жовтий текст на
        /// жовтому, синій на синьому»), решта — приглушений тон сторони
        /// (<see cref="AlphaSkin.BattlePlayerSideMuted"/> тощо) з БІЛИМ
        /// текстом замість насиченого кольору арени. Загортає в новий ряд,
        /// коли не влазить (фікс-ревью старого HUD, той самий клас бага з
        /// довгими іменами-дублікатами).
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
                bool current = string.Equals(id, view.CurrentUnitId, StringComparison.Ordinal) && !unit.IsDowned;
                float w = Widgets.BadgeWidth(name) + (current ? InitiativeBorderPad : 0f);

                if (rowOpen && rowWidth + w > maxWidth) { GUILayout.EndHorizontal(); rowOpen = false; rowWidth = 0f; }
                if (!rowOpen) { GUILayout.BeginHorizontal(); rowOpen = true; }

                GUILayout.BeginVertical(GUILayout.Width(w));
                if (unit.IsDowned)
                    Widgets.Badge(name, AlphaSkin.BgRaised, AlphaSkin.TextDim);
                else if (current)
                    Widgets.BorderedBadge(name, AlphaSkin.BattleCurrentUnit, AlphaSkin.BattleCurrentUnitText, AlphaSkin.AccentActive);
                else
                    Widgets.Badge(name, SideColorMuted(unit.Side), AlphaSkin.TextMain);

                var hpTint = unit.IsDowned ? AlphaSkin.TextDim : SideColor(unit.Side);
                Widgets.FilledBarAt(GUILayoutBarRect(w, InitiativeHpBarHeight), FilledFraction(unit.Hp, unit.HpMax), hpTint);
                GUILayout.EndVertical();

                rowWidth += w;
            }

            if (rowOpen) GUILayout.EndHorizontal();
        }

        // ================= банер ходу (§3) =================

        /// <summary>
        /// Аудит знімків п.3: «помаранчевий текст на напівпрозорому синьому/
        /// червоному — не читається (05 на 1080p майже невидимий)». Тепер
        /// суцільніша підкладка (α≈0.85, було ~0.63) і БІЛИЙ жирний текст із
        /// темною тінню-зсувом замість акцентного кольору тексту, який на тлі
        /// того самого тону сторони губився так само, як бейдж черги ходу.
        /// </summary>
        private static void DrawBanner(IBattleHudData c, Rect topRect)
        {
            var banner = c.Banner;
            if (banner == null || string.IsNullOrEmpty(banner.Text) || banner.Alpha <= 0f) return;

            float width = Clamp(Screen.width * 0.4f, 360f, 720f);
            float height = 44f * Widgets.ScaleForScreen();
            var rect = new Rect((Screen.width - width) * 0.5f, topRect.height + 6f, width, height);

            var tint = banner.PlayerSide ? AlphaSkin.BattlePlayerSide : AlphaSkin.BattleEnemySide;
            float alpha = Clamp01(banner.Alpha);
            var backdrop = new Color32(tint.r, tint.g, tint.b, (byte)(217 * alpha)); // α≈0.85
            Widgets.SolidRect(rect, backdrop);

            var style = new GUIStyle(AlphaSkin.SubHeader) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            var shadowStyle = new GUIStyle(style) { normal = { textColor = new Color(0f, 0f, 0f, 1f) } };
            var mainStyle = new GUIStyle(style) { normal = { textColor = new Color(1f, 1f, 1f, 1f) } };

            var previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 3f, rect.width, rect.height), banner.Text, shadowStyle);
            GUI.Label(rect, banner.Text, mainStyle);
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

            // Раунд 3 (знімки 720p/1080p): колонка кнопок мала явну ширину не мала —
            // кнопки вилазили за праву межу панелі, а «Кінець ходу» виштовхувало
            // з панелі зовсім. Тепер ширина колонки — рівно те, що лишилось після
            // картки, і кожна кнопка знає свою ширину.
            float columnWidth = ButtonsColumnWidth();
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(BottomCardWidth));
            DrawUnitCard(c, current);
            GUILayout.EndVertical();

            GUILayout.Space(Widgets.ScreenPadding());

            GUILayout.BeginVertical(GUILayout.Width(columnWidth));
            DrawAbilities(c, view, current, columnWidth);
            GUILayout.Space(6f);
            DrawActionButtons(c, view, current, columnWidth);

            // Відмова — червоним; інакше, на своєму ході без озброєної дії, —
            // підказка керування. Обидва — всередині колонки: висота панелі їх
            // уже врахувала (EstimateColumnHeight).
            if (!string.IsNullOrEmpty(c.LastRejectionText))
                GUILayout.Label(c.LastRejectionText, AlphaSkin.DangerText, GUILayout.Width(columnWidth));
            else if (c.IsPlayerTurn && c.Armed == ArmedAction.None)
                GUILayout.Label(UkrainianText.Get("ui.battle.hint.controls", false), AlphaSkin.HintLine, GUILayout.Width(columnWidth));
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Картка юніта: ім'я, здоров'я і ОД — кожне ОДНИМ рядком «підпис +
        /// смужка/піпси» (раунд 3: картка нижча, панель не з'їдає арену),
        /// зброя дрібнішим рядком, стани з тривалістю. На ході ворога — та
        /// сама картка лише для читання.
        /// </summary>
        private static void DrawUnitCard(IBattleHudData c, BattleUnitView unit)
        {
            string name = c.ResolveDisplayName(unit);
            GUILayout.Label(UkrainianText.Format("ui.battle.current_unit", false, "name", name), AlphaSkin.SubHeader);

            GUILayout.BeginHorizontal();
            GUILayout.Label(UkrainianText.Format("ui.battle.hp", false,
                "current", I(unit.Hp), "max", I(unit.HpMax)), AlphaSkin.Body, GUILayout.Width(CardLabelWidth));
            GUILayout.BeginVertical();
            GUILayout.Space(11f);
            Widgets.FilledBarAt(GUILayoutBarRect(150f, 10f), FilledFraction(unit.Hp, unit.HpMax), SideColor(unit.Side));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(UkrainianText.Format("ui.battle.ap", false, "current", I(unit.Ap), "max", I(unit.ApMax)),
                AlphaSkin.Body, GUILayout.Width(CardLabelWidth));
            GUILayout.BeginVertical();
            GUILayout.Space(9f);
            Widgets.ProgressPips(unit.Ap, unit.ApMax, 12f);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            if (unit.ApReserved > 0)
                GUILayout.Label(UkrainianText.Format("ui.battle.ap_reserved", false, "reserved", I(unit.ApReserved)), AlphaSkin.HintLine);

            if (!string.IsNullOrEmpty(unit.WeaponId))
            {
                string weaponName = UkrainianText.Get(unit.WeaponId, false);
                string weaponLine = unit.AttackApCost > 0
                    ? UkrainianText.Format("ui.battle.weapon.detail", false,
                        "name", weaponName, "cost", I(unit.AttackApCost), "range", I(unit.WeaponRange))
                    : weaponName;
                GUILayout.Label(UkrainianText.Format("ui.battle.weapon", false, "name", weaponLine), AlphaSkin.HintLine);
            }

            // Ключ статусу — той самий переклад "Bleeding"→"combat.status.bleeding",
            // що дає BattleArenaView.StatusLabelKey: одна мапа на обидва шляхи.
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

        /// <summary>Підпис кнопки здібності: гаряча клавіша, назва, ціна; для недоступної — причина В САМІЙ кнопці.</summary>
        private static string AbilityLabel(IBattleHudData c, BattleUnitView current, BattleAbilityView ability, int index, out bool usable)
        {
            bool armed = c.Armed == ArmedAction.Ability && c.ArmedAbilityId == ability.Id;
            string label = UkrainianText.Format(armed ? "ui.battle.ability.button.armed" : "ui.battle.ability.button", false,
                "hotkey", (index + 1).ToString(CultureInfo.InvariantCulture), "name", UkrainianText.Get(ability.Id, false), "cost", I(ability.ApCost));

            bool enemyTurn = !c.IsPlayerTurn;
            bool onCooldown = ability.CooldownRemaining > 0;
            bool notEnoughAp = current.Ap < ability.ApCost;
            // Поки йде такт — кнопка сіра (клік однаково нічого б не зробив).
            usable = !(enemyTurn || onCooldown || notEnoughAp || c.IsBusy);
            if (usable || enemyTurn || c.IsBusy) return label; // на ході ворога причина одна на всю панель — не дублюємо її в кожній кнопці

            // Раунд 3 (знімок 720p): причина збоку малювалась вузьким
            // стовпчиком по літері й виштовхувала решту кнопок з панелі.
            string reason = onCooldown
                ? UkrainianText.Format("ui.battle.ability.cooldown", false, "turns", UkrainianText.DeclineTurns(ability.CooldownRemaining))
                : UkrainianText.Get("ui.battle.ability.not_enough_ap", false);
            return label + " · " + reason;
        }

        /// <summary>Скільки кнопок здібностей у ряд влазить у колонку (1 або 2) — за найдовшим підписом.</summary>
        private static int AbilitiesPerRow(IBattleHudData c, BattleUnitView current, float columnWidth)
        {
            if (current.Abilities == null || current.Abilities.Count < 2) return 1;
            float longest = 0f;
            for (int i = 0; i < current.Abilities.Count; i++)
            {
                string label = AbilityLabel(c, current, current.Abilities[i], i, out _);
                float w = label.Length * ButtonCharWidth + ButtonTextPadding;
                if (w > longest) longest = w;
            }
            return longest * 2f + 6f <= columnWidth ? 2 : 1;
        }

        /// <summary>Реальний список здібностей ПОТОЧНОГО юніта — [1..9], ціна, відкат/причина в самій кнопці, опис при наведенні.</summary>
        private static void DrawAbilities(IBattleHudData c, BattleView view, BattleUnitView current, float columnWidth)
        {
            if (current.Abilities == null || current.Abilities.Count == 0) return;

            string hoveredDesc = null;
            int perRow = AbilitiesPerRow(c, current, columnWidth);
            float buttonWidth = (columnWidth - 6f * (perRow - 1)) / perRow;
            for (int row = 0; row * perRow < current.Abilities.Count; row++)
            {
                GUILayout.BeginHorizontal();
                for (int i = row * perRow; i < current.Abilities.Count && i < (row + 1) * perRow; i++)
                {
                    var ability = current.Abilities[i];
                    bool armed = c.Armed == ArmedAction.Ability && c.ArmedAbilityId == ability.Id;
                    string label = AbilityLabel(c, current, ability, i, out bool usable);

                    if (!usable)
                        Widgets.DisabledButton(label, null, GUILayout.Width(buttonWidth));
                    else if (Widgets.SecondaryButton(label, GUILayout.Width(buttonWidth)))
                    {
                        if (armed) c.CancelArmed(); else c.ArmAbility(ability.Id);
                    }

                    if (RectContainsMouse(GUILayoutUtility.GetLastRect())) hoveredDesc = ability.Id;
                }
                GUILayout.EndHorizontal();
            }

            // Опис здібності — наведеної або озброєної; рядок зарезервований у
            // висоті панелі завжди, щоб панель не «стрибала» при наведенні.
            string descId = hoveredDesc ?? (c.Armed == ArmedAction.Ability ? c.ArmedAbilityId : null);
            string desc = descId != null && UkrainianText.Has(descId + ".desc", false) ? UkrainianText.Get(descId + ".desc", false) : string.Empty;
            GUILayout.Label(desc, AlphaSkin.HintLine, GUILayout.Width(columnWidth));

            // Важлива інструкція («по чому саме клацнути») — HintLine, не курсив.
            if (c.Armed == ArmedAction.Ability)
                GUILayout.Label(ArmedAbilityHint(c, view, current), AlphaSkin.HintLine, GUILayout.Width(columnWidth));
        }

        /// <summary>Що клацнути для озброєної здібності: для «Наказу пересунутися» — дві фази (союзник, потім клітинка).</summary>
        private static string ArmedAbilityHint(IBattleHudData c, BattleView view, BattleUnitView current)
        {
            var ability = FindAbility(current, c.ArmedAbilityId);
            if (ability != null && ability.NeedsTargetTile && ability.Targeting != "Tile")
            {
                if (string.IsNullOrEmpty(c.ArmedTargetUnitId))
                    return UkrainianText.Get("ui.battle.armed.reposition.pick_unit", false);
                var target = FindUnit(view, c.ArmedTargetUnitId);
                return UkrainianText.Format("ui.battle.armed.reposition.pick_tile", false,
                    "name", target != null ? c.ResolveDisplayName(target) : string.Empty);
            }
            return UkrainianText.Format("ui.battle.armed.ability", false,
                "ability", UkrainianText.Get(c.ArmedAbilityId ?? string.Empty, false));
        }

        private static BattleAbilityView FindAbility(BattleUnitView unit, string abilityId)
        {
            if (unit?.Abilities == null || string.IsNullOrEmpty(abilityId)) return null;
            foreach (var a in unit.Abilities)
                if (a != null && a.Id == abilityId) return a;
            return null;
        }

        private static void DrawActionButtons(IBattleHudData c, BattleView view, BattleUnitView current, float columnWidth)
        {
            GUILayout.BeginHorizontal();

            if (c.IsPlayerTurn)
            {
                var downedAlly = FindAdjacentDownedAlly(view, current);
                int count = downedAlly != null ? 3 : 2;
                float w = (columnWidth - 6f * (count - 1)) / count;

                bool overwatchArmed = c.Armed == ArmedAction.OverwatchAim;
                string owLabel = (overwatchArmed ? "» " : string.Empty) + UkrainianText.Get("ui.battle.hotkey.overwatch", false);
                string stabLabel = UkrainianText.Get("ui.battle.stabilize", false);
                string endLabel = UkrainianText.Get("ui.battle.hotkey.endturn", false);
                if (c.IsBusy)
                {
                    // Поки йде такт — ті самі кнопки, але сірі: клік однаково чекав би кінця анімації.
                    Widgets.DisabledButton(owLabel, null, GUILayout.Width(w));
                    if (downedAlly != null) Widgets.DisabledButton(stabLabel, null, GUILayout.Width(w));
                    Widgets.DisabledButton(endLabel, null, GUILayout.Width(w));
                }
                else
                {
                    if (Widgets.SecondaryButton(owLabel, GUILayout.Width(w)))
                    {
                        if (overwatchArmed) c.CancelArmed(); else c.ArmOverwatchAim();
                    }

                    if (downedAlly != null && Widgets.SecondaryButton(stabLabel, GUILayout.Width(w)))
                        c.RequestStabilize(downedAlly.Id);

                    if (Widgets.PrimaryButton(endLabel, GUILayout.Width(w)))
                        c.RequestEndTurn();
                }
            }
            else
            {
                bool fast = c.FastEnemyTurns;
                if (Widgets.TabButton(UkrainianText.Get("ui.battle.hotkey.fastforward", false), fast, GUILayout.Width(columnWidth)))
                    c.FastEnemyTurns = !fast;
            }

            GUILayout.EndHorizontal();

            if (c.Armed == ArmedAction.OverwatchAim)
                GUILayout.Label(UkrainianText.Get("ui.battle.armed.overwatch_aim", false), AlphaSkin.HintLine, GUILayout.Width(columnWidth));
            if (c.Armed != ArmedAction.None)
                GUILayout.Label(UkrainianText.Get("ui.battle.cancel", false), AlphaSkin.HintLine, GUILayout.Width(columnWidth));
        }

        // ---- висота нижньої панелі — від вмісту (раунд 3) ----

        /// <summary>Висота картки юніта — ті самі рядки, що малює <see cref="DrawUnitCard"/>.</summary>
        private static float EstimateCardHeight(BattleUnitView u)
        {
            float h = 36f + 30f + 30f; // ім'я, здоров'я, ОД
            if (u.ApReserved > 0) h += 24f;
            if (!string.IsNullOrEmpty(u.WeaponId)) h += 26f;
            if ((u.StatusDetails != null && u.StatusDetails.Count > 0) || (u.Statuses != null && u.Statuses.Count > 0)) h += 32f;
            if (u.IsOverwatching) h += 32f;
            if (u.IsDowned) h += 28f;
            return h;
        }

        /// <summary>Висота колонки кнопок — ті самі рядки, що малюють <see cref="DrawAbilities"/> і <see cref="DrawActionButtons"/>.</summary>
        private static float EstimateColumnHeight(IBattleHudData c, BattleUnitView u, float columnWidth)
        {
            float h = 0f;
            int abilities = u.Abilities != null ? u.Abilities.Count : 0;
            if (abilities > 0)
            {
                int perRow = AbilitiesPerRow(c, u, columnWidth);
                h += ((abilities + perRow - 1) / perRow) * 44f;
                h += 26f; // рядок опису
                if (c.Armed == ArmedAction.Ability) h += 26f;
            }
            h += 6f + 44f; // ряд Дозор/Стабілізувати/Кінець ходу або Прискорити
            if (c.Armed == ArmedAction.OverwatchAim) h += 26f;
            if (c.Armed != ArmedAction.None) h += 26f;
            h += 34f; // відмова або підказка керування (з запасом — на 720p/1080p обрізалась знизу)
            return h;
        }

        private static float _lastBottomPanelHeight = 170f;

        private static float BottomPanelHeight(IBattleHudData c, BattleView view)
        {
            var current = FindUnit(view, view.CurrentUnitId);
            float h = current == null
                ? 80f
                : Math.Max(EstimateCardHeight(current), EstimateColumnHeight(c, current, ButtonsColumnWidth())) + 30f;
            _lastBottomPanelHeight = Clamp(h, 90f, Screen.height * 0.4f);
            return _lastBottomPanelHeight;
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
            // Аудит знімків п.6: «Автобій» — невелика другорядна кнопка в
            // шапці поруч зі згортанням, не на всю ширину панелі (раніше
            // окремим рядком під заголовком, розтягнута стилем кнопки).
            GUILayout.BeginHorizontal();
            // Без перенесення слів: на 720p заголовок ламався на «Журна / л» поруч з «Автобоєм».
            GUILayout.Label(UkrainianText.Get("ui.battle.log", false), new GUIStyle(AlphaSkin.SubHeader) { wordWrap = false }, GUILayout.ExpandWidth(true));
            if (Widgets.SecondaryButton(UkrainianText.Get("ui.battle.autoresolve", false), GUILayout.ExpandWidth(false)))
                _confirmAutoResolve = true;
            GUILayout.Space(6f);
            if (Widgets.SecondaryButton(_logCollapsed ? "▸" : "▾", GUILayout.ExpandWidth(false)))
                _logCollapsed = !_logCollapsed;
            GUILayout.EndHorizontal();

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

        // ================= підказка біля курсора (§3, аудит знімків п.4) =================

        /// <summary>Раунд 3: ширша підказка — «Шанс влучення: 25%» не переноситься на два рядки.</summary>
        private static float TooltipWidth => Screen.width >= 1600f ? 380f : 340f;

        /// <summary>
        /// Аудит знімків п.4: підказка прилипала до лівого верхнього кута
        /// (мишача позиція) поверх «Раунд 1» — тепер стоїть ПОРУЧ із ціллю:
        /// біля оверлея наведеного юніта (<see cref="BattleUnitOverlay.
        /// IsHovered"/>) для атаки, біля екранної точки наведеного тайла
        /// (<see cref="IBattleHudData.HoveredTileScreenX"/>/<c>Y</c> — новий
        /// член контракту, раунд 2) для руху. Клемп у вільну область —
        /// чиста функція <see cref="BattleTooltipLayout"/> (headless-тест),
        /// тут лише підстановка меж панелей.
        /// </summary>
        private static void DrawCursorTooltip(IBattleHudData c, BattleView view)
        {
            var attack = c.HoverAttack;
            var path = c.HoverPath;
            if (attack == null && path == null)
            {
                // Озброєна здібність чи дозор над клітинкою — своя підказка, не «Рух» (рев'ю Бою v2).
                if (c.Armed != ArmedAction.None && c.HasHoveredTile && c.IsPlayerTurn) DrawArmedTileTooltip(c, view);
                return;
            }

            float anchorX, anchorY;
            if (attack != null && TryFindHoveredOverlay(c, out var ov))
            {
                anchorX = ov.ScreenX;
                anchorY = ov.ScreenY;
            }
            else if (c.HasHoveredTile)
            {
                anchorX = c.HoveredTileScreenX;
                anchorY = c.HoveredTileScreenY;
            }
            else
            {
                var evt = Event.current;
                anchorX = evt?.mousePosition.x ?? 0f;
                anchorY = evt?.mousePosition.y ?? 0f;
            }

            float scale = Widgets.ScaleForScreen();
            float height = attack != null ? EstimateAttackTooltipHeight(attack) : EstimatePathTooltipHeight(c, view);

            float freeTop = TopBarHeight(scale) + 8f;
            float freeBottom = Screen.height - _lastBottomPanelHeight - Widgets.ScreenPadding() - 8f;
            float freeRight = Screen.width - RightPanelWidth() - 8f;

            var (x, y) = BattleTooltipLayout.PlaceNearAnchor(anchorX, anchorY, TooltipWidth, height,
                8f, freeTop, freeRight, freeBottom);

            var area = new Rect(x, y, TooltipWidth, height);
            GUILayout.BeginArea(area, GUI.skin.box);
            if (attack != null) DrawHoverAttack(c, view, attack);
            else DrawHoverPath(c, view, path);
            GUILayout.EndArea();
        }

        private static void DrawArmedTileTooltip(IBattleHudData c, BattleView view)
        {
            var current = FindUnit(view, view.CurrentUnitId);
            if (current == null) return;

            string title;
            var lines = new List<KeyValuePair<string, GUIStyle>>();
            if (c.Armed == ArmedAction.OverwatchAim)
            {
                title = UkrainianText.Get("ui.battle.armed.overwatch_title", false);
                lines.Add(new KeyValuePair<string, GUIStyle>(UkrainianText.Get("ui.battle.armed.overwatch_tooltip", false), AlphaSkin.HintLine));
            }
            else
            {
                var ability = FindAbility(current, c.ArmedAbilityId);
                title = UkrainianText.Get(c.ArmedAbilityId ?? string.Empty, false);
                if (ability != null)
                {
                    int dist = Math.Max(Math.Abs(current.Pos.X - c.HoveredTileX), Math.Abs(current.Pos.Y - c.HoveredTileY));
                    lines.Add(new KeyValuePair<string, GUIStyle>(UkrainianText.Format("ui.battle.armed.range", false, "range", I(ability.Range)), AlphaSkin.Body));
                    lines.Add(dist <= ability.Range
                        ? new KeyValuePair<string, GUIStyle>(UkrainianText.Get("ui.battle.armed.in_range", false), AlphaSkin.HintLine)
                        : new KeyValuePair<string, GUIStyle>(UkrainianText.Get("ui.battle.armed.out_of_range", false), AlphaSkin.DangerText));
                }
                lines.Add(new KeyValuePair<string, GUIStyle>(ArmedAbilityHint(c, view, current), AlphaSkin.HintLine));
            }

            float scale = Widgets.ScaleForScreen();
            float height = 24f + 34f + lines.Count * 30f;
            float freeTop = TopBarHeight(scale) + 8f;
            float freeBottom = Screen.height - _lastBottomPanelHeight - Widgets.ScreenPadding() - 8f;
            float freeRight = Screen.width - RightPanelWidth() - 8f;
            var (x, y) = BattleTooltipLayout.PlaceNearAnchor(c.HoveredTileScreenX, c.HoveredTileScreenY, TooltipWidth, height,
                8f, freeTop, freeRight, freeBottom);

            GUILayout.BeginArea(new Rect(x, y, TooltipWidth, height), GUI.skin.box);
            GUILayout.Label(title, AlphaSkin.Body);
            foreach (var line in lines) GUILayout.Label(line.Key, line.Value);
            GUILayout.EndArea();
        }

        private static bool TryFindHoveredOverlay(IBattleHudData c, out BattleUnitOverlay overlay)
        {
            overlay = null;
            if (c.Overlays == null) return false;
            foreach (var ov in c.Overlays)
            {
                if (!ov.IsHovered) continue;
                overlay = ov;
                return true;
            }
            return false;
        }

        /// <summary>Грубий підрахунок висоти підказки атаки з реальних рядків, які вона намалює — трохи із запасом, аби ніколи не обрізати вміст (BeginArea мовчки кадрує зайве, а не скролить).</summary>
        private static float EstimateAttackTooltipHeight(AttackPreviewView p)
        {
            // Раунд 3: оцінка була впритул, і «Ціна: N ОД» обрізалась знизу.
            float h = 24f + 34f; // відступи + заголовок
            if (p.HasAttackRoll)
            {
                h += 42f; // велике «Шанс влучення N%»
                if (p.Terms != null)
                    foreach (var term in p.Terms)
                        if (term.ChanceDelta != 0) h += 26f;
                h += 30f; // шкода
            }
            h += 30f; // «Ціна: N ОД»
            h += 30f; // «Здоров'я цілі: N/M»
            if (!p.CoverIgnored && !string.Equals(p.Cover, "None", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(p.Cover))
                h += 28f;
            if (p.Result != "Success") h += 30f;
            return h;
        }

        private static float EstimatePathTooltipHeight(IBattleHudData c, BattleView view)
        {
            float h = 24f + 34f + 30f; // відступи + заголовок + рядок ціни/відмови
            if (view.Grid != null && c.HasHoveredTile) h += 28f; // укриття клітинки
            h += 30f; // «Під ворожим дозором!» — з запасом, навіть коли порожньо
            return h;
        }

        private static void DrawHoverAttack(IBattleHudData c, BattleView view, AttackPreviewView p)
        {
            var target = FindUnit(view, p.TargetId);
            string targetName = target != null ? c.ResolveDisplayName(target) : string.Empty;
            GUILayout.Label(UkrainianText.Format("ui.battle.hover.attack_title", false, "target", targetName), AlphaSkin.Body);

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
                        GUILayout.Label(label + " " + sign + I(term.ChanceDelta), AlphaSkin.HintLine);
                    }

                string damageLine = p.IsDamageDeterministic
                    ? UkrainianText.Format("ui.battle.damage.preview.single", false, "value", I(p.DamageExpected))
                    : (p.DamageCrit > p.DamageMax
                        ? UkrainianText.Format("ui.battle.damage.preview.crit", false, "min", I(p.DamageMin), "max", I(p.DamageMax), "crit", I(p.DamageCrit))
                        : UkrainianText.Format("ui.battle.damage.preview", false, "min", I(p.DamageMin), "max", I(p.DamageMax)));
                GUILayout.Label(damageLine, AlphaSkin.Body);
            }

            GUILayout.Label(UkrainianText.Format("ui.battle.ap_cost", false, "cost", I(p.ApCost)), AlphaSkin.Body);

            if (target != null)
                GUILayout.Label(UkrainianText.Format("ui.battle.hp.target", false,
                    "current", I(target.Hp), "max", I(target.HpMax)), AlphaSkin.Body);

            if (!p.CoverIgnored && !string.Equals(p.Cover, "None", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(p.Cover))
                GUILayout.Label(UkrainianText.Get("ui.battle.cover." + p.Cover.ToLowerInvariant(), false), AlphaSkin.HintLine);

            if (p.Result != "Success")
                GUILayout.Label(UkrainianText.Get(RejectionKey(p.Result), false), AlphaSkin.DangerText);
        }

        private static void DrawHoverPath(IBattleHudData c, BattleView view, MovePathView path)
        {
            GUILayout.Label(UkrainianText.Get("ui.battle.hover.move_title", false), AlphaSkin.Body);

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
                        GUILayout.Label(UkrainianText.Get("ui.battle.cover." + cover.ToLowerInvariant(), false), AlphaSkin.HintLine);
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

            // Раунд 3 (знімки): імена сусідніх бійців налазили одне на одне
            // («ПровідниЗастрільник орди»). Спершу розсуваємо блоки по
            // вертикалі (чиста функція з тестом), тоді малюємо.
            var visible = new List<BattleUnitOverlay>();
            var units = new List<BattleUnitView>();
            foreach (var ov in c.Overlays)
            {
                if (!ov.OnScreen) continue;
                var unit = FindUnit(view, ov.UnitId);
                if (unit == null) continue;
                visible.Add(ov);
                units.Add(unit);
            }

            int n = visible.Count;
            var centerX = new float[n];
            var top = new float[n];
            var width = new float[n];
            for (int i = 0; i < n; i++)
            {
                centerX[i] = visible[i].ScreenX;
                top[i] = visible[i].ScreenY;
                width[i] = OverlayNameWidth(c.ResolveDisplayName(units[i]));
            }
            float blockHeight = AlphaSkin.OverlayNameFontSize + 6f + 6f + 18f; // ім'я + HP + один значок
            var resolved = BattleTooltipLayout.ResolveVerticalOverlaps(centerX, top, width, blockHeight, 2f);

            for (int i = 0; i < n; i++)
                DrawUnitOverlay(c, units[i], visible[i], resolved[i]);
        }

        private static float OverlayNameWidth(string name)
            => Clamp(name.Length * AlphaSkin.OverlayNameFontSize * 0.62f + 12f, 60f, 220f);

        private static void DrawUnitOverlay(IBattleHudData c, BattleUnitView unit, BattleUnitOverlay ov, float topY)
        {
            string name = c.ResolveDisplayName(unit);
            float nameWidth = OverlayNameWidth(name);
            var nameRect = new Rect(ov.ScreenX - nameWidth * 0.5f, topY, nameWidth, AlphaSkin.OverlayNameFontSize + 6f);
            Widgets.SolidRect(nameRect, new Color32(12, 10, 8, 190));

            // Ворог під курсором, якого ЗАРАЗ можна атакувати, — яскрава рамка
            // (презентер рахує IsTargetable за прев'ю атаки). Гравець бачить
            // «клік сюди = удар» ще до кліку, як у референсах.
            if (ov.IsTargetable)
            {
                const float b = 2f;
                var frame = AlphaSkin.BattleCurrentUnit;
                Widgets.SolidRect(new Rect(nameRect.x - b, nameRect.y - b, nameRect.width + 2f * b, b), frame);
                Widgets.SolidRect(new Rect(nameRect.x - b, nameRect.y + nameRect.height, nameRect.width + 2f * b, b), frame);
                Widgets.SolidRect(new Rect(nameRect.x - b, nameRect.y, b, nameRect.height), frame);
                Widgets.SolidRect(new Rect(nameRect.x + nameRect.width, nameRect.y, b, nameRect.height), frame);
            }

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
                // Темна тінь під написом — читається і на світлій траві, і на тайлах.
                var shadow = new GUIStyle(style) { normal = { textColor = new Color(0f, 0f, 0f, 0.85f) } };
                GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), f.Text, shadow);
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

        /// <summary>Приглушений тон тла бейджа черги ходу (§DrawInitiativeStrip) — не той самий насичений колір, що арена/оверлеї.</summary>
        private static Color32 SideColorMuted(string side)
        {
            switch (side)
            {
                case "Player": return AlphaSkin.BattlePlayerSideMuted;
                case "FromDefector": return AlphaSkin.BattleDefectorSideMuted;
                default: return AlphaSkin.BattleEnemySideMuted;
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

        /// <summary>Ширина картки поточного юніта в нижній панелі (§DrawActionPanel) — фіксоване число зі специфікації, не частка від ширини панелі.</summary>
        private const float BottomCardWidth = 360f;
        /// <summary>Ширина підпису «Здоров'я: 14/14» / «Очки дій: 9/9» у картці — смужка й піпси праворуч.</summary>
        private const float CardLabelWidth = 172f;

        /// <summary>Оцінка ширини символу й полів кнопки (IMGUI однопрохідний — вимірювати наперед нема чим).</summary>
        private const float ButtonCharWidth = 11f;
        private const float ButtonTextPadding = 26f;

        /// <summary>Колонка кнопок — усе, що лишилось у панелі після картки.</summary>
        private static float ButtonsColumnWidth()
            => Math.Max(260f, BottomPanelWidth() - BottomCardWidth - Widgets.ScreenPadding() * 3f - 16f);

        private static float TopBarHeight(float scale) => Clamp(44f * scale, 40f, 56f);


        private static float RightPanelWidth() => Screen.width >= 1600f ? 360f : 300f;

        /// <summary>
        /// Аудит знімків п.1: «ширина — за вмістом (картка юніта ~360 px +
        /// кнопки), без порожнього простору» — панель раніше розтягувалась
        /// на всю вільну ширину екрана між лівим краєм і журналом, лишаючи
        /// порожнє поле праворуч від кнопок. Природна ширина — картка +
        /// оцінка колонки кнопок; клемп зверху лише щоб не виїхати під
        /// журнал на вузькому екрані (720p), знизу — щоб лишитись читабельною.
        /// </summary>
        private static float BottomPanelWidth()
        {
            // Раунд 3: панель — до 1180 px, але не ширша за вільне місце лівіше
            // журналу; кнопки всередині самі діляться на ряди (AbilitiesPerRow).
            float available = Screen.width - RightPanelWidth() - Widgets.ScreenPadding() * 4f;
            return Clamp(available, 560f, 1180f);
        }

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
