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
    /// Розкладка (§3): верхня смуга (раунд + індикатор правила влучання) →
    /// колесо черги ходів у лівому верхньому куті (Поправка №14.5) → банер
    /// ходу сторони під смугою → нижня панель дій по
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
        private static bool _confirmRetreat;
        private static int _lastLogCount = -1;

        public static void Draw(IBattleHudData c)
        {
            if (c == null) return;
            GUI.skin = AlphaSkin.Build();

            var blockingRects = new List<Rect>();

            if (c.ResultPending) { _confirmAutoResolve = false; _confirmRetreat = false; } // бій скінчився — питання знято

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

            var topRect = new Rect(0f, 0f, UiScale.Width, TopBarHeight(scale));
            GUILayout.BeginArea(topRect, GUI.skin.box);
            DrawTopBar(c, view);
            GUILayout.EndArea();
            blockingRects.Add(topRect);

            // Колесо черги ходів у лівому верхньому куті (Поправка №14.5) —
            // замість стрічки у верхній смузі. Клік блокують лише бейджі й
            // центр: між ними арена клікається (рецензія C1 — інакше колесо
            // з'їдало 9–15% поля на 720p).
            bool compactWheel = UiScale.Height <= 760;
            var wheel = TurnWheelModel.Build(view, compactWheel ? WheelCompactSlots : TurnWheelModel.DefaultMaxSlots);
            var wheelRect = TurnWheelRect(topRect, WheelScale(scale, compactWheel), pad);
            DrawTurnWheel(c, view, wheel, wheelRect, WheelScale(scale, compactWheel), blockingRects);

            float rightWidth = RightPanelWidth();
            var rightRect = new Rect(UiScale.Width - rightWidth - pad, topRect.height + pad,
                rightWidth, UiScale.Height - topRect.height - pad * 2f);
            GUILayout.BeginArea(rightRect, GUI.skin.box);
            DrawLogPanel(c);
            GUILayout.EndArea();
            blockingRects.Add(rightRect);

            float bottomWidth = BottomPanelWidth();
            float bottomHeight = BottomPanelHeight(c, view);
            var bottomRect = new Rect((UiScale.Width - rightWidth - pad - bottomWidth) * 0.5f,
                UiScale.Height - bottomHeight - pad, bottomWidth, bottomHeight);
            GUILayout.BeginArea(bottomRect, GUI.skin.box);
            DrawActionPanel(c, view);
            GUILayout.EndArea();
            blockingRects.Add(bottomRect);

            // Портрети загону, як у BG3 (власник, 08.10.2026): стовпець під колесом черги, до нижньої панелі.
            float portraitsTop = wheelRect.y + wheelRect.height + pad;
            float portraitsBottom = bottomRect.x < pad * 2f + PortraitWidth(scale) ? bottomRect.y - pad : UiScale.Height - pad;
            DrawPartyPortraits(c, view, pad, portraitsTop, portraitsBottom, scale, blockingRects);

            c.SetHudRects(blockingRects);

            // Те, що йде нижче, малюється ПОВЕРХ уже намальованих панелей і
            // навмисно НЕ входить у blockingRects — банер/оверлеї/спливаючі
            // написи/підказка не мають ловити клік, призначений арені (§3).
            DrawBanner(c, topRect);
            DrawCoverMarkers(c);
            DrawOverlays(c, view);
            DrawFloatingTexts(c);
            DrawCursorTooltip(c, view);

            // Підтвердження автобою — на верхньому рівні, не всередині панелі журналу:
            // там модалка обрізалась межами області й була недоступна (рев'ю Бою v2).
            if (_confirmAutoResolve) DrawAutoResolveConfirm(c);
            if (_confirmRetreat) DrawRetreatConfirm(c, view);
        }

        /// <summary>
        /// Підтвердження відступу (Поправка №14.7): що саме буде — до кліку
        /// (Статут UI-02), текст наслідку — за тим, хто просив бій.
        /// </summary>
        private static void DrawRetreatConfirm(IBattleHudData c, BattleView view)
        {
            Widgets.Modal(UkrainianText.Get("ui.battle.retreat.confirm.title", false), () =>
            {
                GUILayout.Label(UkrainianText.Get("ui.battle.retreat.confirm.body", false), AlphaSkin.Body);
                if (!string.IsNullOrEmpty(view.RetreatConsequenceKey))
                    GUILayout.Label(UkrainianText.Get(view.RetreatConsequenceKey, false), AlphaSkin.DangerText);
                GUILayout.Space(10f);
                GUILayout.BeginHorizontal();
                if (Widgets.DangerButton(UkrainianText.Get("ui.battle.retreat", false)))
                {
                    _confirmRetreat = false;
                    c.RequestRetreat();
                }
                if (Widgets.SecondaryButton(UkrainianText.Get("ui.common.cancel", false)))
                    _confirmRetreat = false;
                GUILayout.EndHorizontal();
            }, () => _confirmRetreat = false);
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
            if (evt == null || evt.type != EventType.KeyDown || c.Paused || PlaytestLog.NoteOpen) return;

            // Подача бою П8: показати/сховати журнал — будь-коли, і під час тактів теж (нічого не озброює).
            if (evt.keyCode == KeyCode.L)
            {
                _logCollapsed = !_logCollapsed;
                evt.Use();
                return;
            }

            if (c.IsBusy || c.ResultPending) return;

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
            // «Раунд N» — у центрі колеса черги (Поправка №14.5), тут не дублюється.
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            // Підкріплення ворога з відліком (Поправка №14.4): видно заздалегідь, а не з'являється раптом.
            if (view.ReinforcementRound > 0)
            {
                GUILayout.Label(UkrainianText.Format("ui.battle.reinforcements.countdown", false,
                    "round", I(view.ReinforcementRound), "count", I(view.ReinforcementCount)), AlphaSkin.DangerText, GUILayout.ExpandWidth(false));
                GUILayout.Space(16f);
            }
            // Аудит знімків п.5: «Правило влучання вгорі праворуч — не
            // курсивом, читабельно» — AlphaSkin.Tooltip курсивний і тьмяний,
            // тут потрібен звичайний світлий HintLine (§AlphaSkin.HintLine).
            string ruleKey = view.IsHitRulePercent ? "ui.title.hitrule.percent" : "ui.title.hitrule.threshold";
            GUILayout.Label(UkrainianText.Get(ruleKey, false), AlphaSkin.HintLine, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
        }

        // ================= портрети загону, як у BG3 (08.10.2026) =================

        /// <summary>
        /// Хто дає обличчя: оболонка ставить <c>GameShell.PortraitProvider</c> (живий рендер набору) перед кожним
        /// кадром бою; null чи ще не зрендерено — картка з ініціалами кольором персонажа.
        /// </summary>
        public static IPortraitProvider Portraits { get; set; }

        /// <summary>Портрет під мишею — мітка бійця на арені підсвічується, як від колеса черги.</summary>
        private static string _portraitHoverUnitId;
        private static readonly Dictionary<string, Texture2D> _portraitCards = new Dictionary<string, Texture2D>();
        private static Texture2D _portraitShade;

        private static float PortraitWidth(float scale) => Clamp(78f * scale, 56f, 104f);

        /// <summary>
        /// Стовпець портретів загону (власник, 08.10.2026: «іконки персонажів такі самі на рушії гри»; «У бою»).
        /// ЩО і ДЕ рахує <see cref="PartyPortraitsModel"/>; тут лише малювання: обличчя з рушія, золота рамка в
        /// того, чий хід, упалий — притемнений, під обличчям — здоров'я й очки дії, при наведенні — підказка;
        /// клік — камера до бійця (як бейдж колеса).
        /// </summary>
        private static void DrawPartyPortraits(IBattleHudData c, BattleView view, float x, float top, float bottom, float scale, List<Rect> blocking)
        {
            _portraitHoverUnitId = null;
            var slots = PartyPortraitsModel.Build(view, Game.Core.Session.GameSession.ProtagonistId);
            var rects = PartyPortraitsModel.Layout(slots.Count, x, top, bottom, PortraitWidth(scale), 8f);
            PartyPortraitSlot hoveredSlot = null;
            Rect hoveredCard = default(Rect);
            for (int i = 0; i < rects.Count; i++)
            {
                var slot = slots[i];
                var pr = rects[i];
                var card = new Rect(pr.X, pr.Y, pr.Width, pr.Height);
                var face = new Rect(pr.X, pr.Y, pr.Width, pr.Width * PartyPortraitsModel.CardAspect);
                var unit = FindUnit(view, slot.UnitId);
                string name = unit != null ? c.ResolveDisplayName(unit) : slot.CharacterId;

                bool hovered = RectContainsMouse(card);
                bool hoveredOnMap = string.Equals(c.HoveredUnitId, slot.UnitId, StringComparison.Ordinal);
                if (hovered)
                {
                    _portraitHoverUnitId = slot.UnitId;
                    hoveredSlot = slot;
                    hoveredCard = card;
                    var evt = Event.current;
                    if (evt != null && evt.type == EventType.MouseDown && evt.button == 0)
                    {
                        c.FocusCamera(slot.UnitId);
                        evt.Use();
                    }
                }

                // Рамка: чий хід — товста золота; наведення (тут чи на арені) — світла; решта — тонка.
                Color32 frame = slot.IsCurrent ? AlphaSkin.BattleCurrentUnit : (hovered || hoveredOnMap ? AlphaSkin.AccentHover : AlphaSkin.Border);
                float b = slot.IsCurrent ? 3f : 1.5f;
                Widgets.SolidRect(new Rect(face.x - b, face.y - b, face.width + 2f * b, face.height + 2f * b), frame);

                var tex = Portraits != null ? Portraits.GetPortrait(slot.CharacterId) : null;
                if (tex != null) GUI.DrawTexture(face, tex, ScaleMode.ScaleAndCrop);
                else DrawPortraitCard(face, slot.CharacterId, name);
                if (slot.IsDowned)
                {
                    if (_portraitShade == null) _portraitShade = AlphaSkin.SolidTexture(new Color32(10, 8, 6, 165));
                    GUI.DrawTexture(face, _portraitShade, ScaleMode.StretchToFill);
                }

                ShadowedLabel(new Rect(face.x + 3f, face.y + face.height - 18f, face.width - 6f, 18f),
                    TruncateName(name, face.width), slot.IsDowned ? AlphaSkin.TextDim : AlphaSkin.TextMain);
                if (slot.Statuses.Count > 0)
                    ShadowedLabel(new Rect(face.x + 3f, face.y + 2f, face.width - 6f, 18f),
                        slot.Statuses.Count == 1 ? "◆" : "◆" + I(slot.Statuses.Count), AlphaSkin.BattleStatus);

                float by = face.y + face.height + 2f;
                Widgets.FilledBarAt(new Rect(face.x, by, face.width, 4f), slot.HpFraction, slot.IsDowned ? AlphaSkin.TextDim : SideColor("Player"));
                Widgets.FilledBarAt(new Rect(face.x, by + 5f, face.width, 3f), slot.ApFraction, AlphaSkin.AccentHover);
                blocking.Add(card);
            }

            if (hoveredSlot != null) DrawPortraitTip(c, view, hoveredSlot, hoveredCard);
        }

        /// <summary>Підказка біля портрета: ім'я, здоров'я й очки дії числом, стани з тривалістю — те саме, що картка бійця.</summary>
        private static void DrawPortraitTip(IBattleHudData c, BattleView view, PartyPortraitSlot slot, Rect card)
        {
            var unit = FindUnit(view, slot.UnitId);
            if (unit == null) return;
            var lines = new List<string>
            {
                c.ResolveDisplayName(unit),
                UkrainianText.Format("ui.battle.portrait.stats", false,
                    "hp", I(slot.Hp), "hpMax", I(slot.HpMax), "ap", I(slot.Ap), "apMax", I(slot.ApMax))
            };
            if (slot.IsDowned) lines.Add(UkrainianText.Get("ui.battle.portrait.downed", false));
            if (unit.StatusDetails != null)
                foreach (var status in unit.StatusDetails)
                {
                    string key = BattleArenaView.StatusLabelKey(status.Type);
                    if (key != null) lines.Add(UkrainianText.Get(key, false) + " (" + UkrainianText.DeclineTurns(status.RemainingTurns) + ")");
                }
            float w = 220f, lineH = 20f;
            var tip = new Rect(card.x + card.width + 8f, card.y, w, lines.Count * lineH + 10f);
            Widgets.SolidRect(tip, new Color32(12, 10, 8, 225));
            for (int i = 0; i < lines.Count; i++)
                GUI.Label(new Rect(tip.x + 8f, tip.y + 5f + i * lineH, w - 16f, lineH), lines[i],
                    WheelText(i == 0 ? AlphaSkin.AccentHover : AlphaSkin.TextMain));
        }

        /// <summary>Поки живого рендера немає (перший кадр запиту): картка кольором персонажа з ініціалами, а не порожнє місце.</summary>
        private static void DrawPortraitCard(Rect rect, string characterId, string name)
        {
            string id = characterId ?? string.Empty;
            if (!_portraitCards.TryGetValue(id, out var tex) || tex == null)
            {
                var palette = BattleArenaView.CharacterTint("u_" + id, "Player", id);
                tex = AlphaSkin.SolidTexture(new Color32((byte)(palette.R * 255f), (byte)(palette.G * 255f), (byte)(palette.B * 255f), 255));
                _portraitCards[id] = tex;
            }
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill);
            string initials = "?";
            if (!string.IsNullOrEmpty(name))
            {
                var words = name.Split(' ');
                initials = words.Length >= 2 && words[0].Length > 0 && words[1].Length > 0
                    ? (char.ToUpperInvariant(words[0][0]).ToString() + char.ToUpperInvariant(words[1][0]))
                    : name.Substring(0, Math.Min(2, name.Length)).ToUpperInvariant();
            }
            GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.3f, rect.width, 24f), initials, WheelText(AlphaSkin.BgDark));
        }

        // ================= колесо черги ходів (Поправка №14.5) =================

        /// <summary>Бієць, на чий бейдж на колесі наведена миша, — його мітка над головою підсвічується (зв'язок колесо → мапа).</summary>
        private static string _wheelHoverUnitId;
        private static string _wheelLastCurrentId;
        private static float _wheelTurnStartedAt = -10f;

        /// <summary>Скільки триває поворот колеса на крок після зміни ходу (сек).</summary>
        private const float WheelTurnSeconds = 0.3f;

        /// <summary>На екранах до 760 px заввишки (720p) колесо менше і тримає 6 ходів, а не 8 (рецензія C1).</summary>
        private const int WheelCompactSlots = 6;
        private static float WheelScale(float scale, bool compact) => compact ? scale * 0.85f : scale;

        private static float WheelBadgeWidth(float scale) => Clamp(112f * scale, 92f, 150f);
        private static float WheelBadgeHeight(float scale) => Clamp(24f * scale, 20f, 30f);
        private static float WheelRadiusX(float scale) => Clamp(122f * scale, 96f, 160f);
        // Вертикальна піввісь ≥ висота бейджа / (1 − cos 45°): сусіди вгорі й унизу не налазять одне на одного при 8 слотах.
        private static float WheelRadiusY(float scale) => Clamp(100f * scale, 80f, 130f);
        private static float WheelTitleHeight(float scale) => Clamp(22f * scale, 18f, 28f);
        private const float WheelHpBarHeight = 3f;
        private const float WheelSkipLabelHeight = 16f;

        /// <summary>Спільний стиль тексту колеса: один екземпляр на весь HUD, колір підставляється перед кожним підписом (рецензія C1 — без нового GUIStyle щокадру).</summary>
        private static GUIStyle _wheelText;
        private static GUIStyle _wheelTextSource;

        private static GUIStyle WheelText(Color32 color)
        {
            var source = AlphaSkin.OverlayName;
            if (_wheelText == null || !ReferenceEquals(_wheelTextSource, source))
            {
                _wheelText = new GUIStyle(source);
                _wheelTextSource = source;
            }
            _wheelText.normal.textColor = color;
            return _wheelText;
        }

        private static Rect TurnWheelRect(Rect topRect, float scale, float pad)
        {
            float w = WheelRadiusX(scale) * 2f + WheelBadgeWidth(scale) + pad * 2f;
            float h = WheelTitleHeight(scale) + WheelRadiusY(scale) * 2f + WheelBadgeHeight(scale)
                + WheelHpBarHeight + WheelSkipLabelHeight + pad * 2f;
            return new Rect(pad, topRect.height + pad, w, h);
        }

        /// <summary>Підпис поверх арени без суцільного тла: темна тінь-зсув робить його читабельним і на траві, і на тайлах.</summary>
        private static void ShadowedLabel(Rect rect, string text, Color32 color)
        {
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, WheelText(AlphaSkin.BgDark));
            GUI.Label(rect, text, WheelText(color));
        }

        /// <summary>
        /// Колесо черги ходів (власник, 29.09.2026: «колесо чи щось таке»,
        /// обрано «Колесо в кутку»). ЩО показувати рахує
        /// <see cref="TurnWheelModel"/>; тут лише малювання:
        /// <list type="bullet">
        /// <item>поточний бієць угорі — золоте тло, темний текст, рамка;</item>
        /// <item>далі за годинниковою стрілкою; межа раунду — риска з «Р{n}»;</item>
        /// <item>свої — повна рамка, чужі — лише верхня і нижня (форма, а не лише колір, Статут UI-01);</item>
        /// <item>упалий — сірий і перекреслений; оглушений — «пропускає» під бейджем;</item>
        /// <item>наведення на бейдж підсвічує мітку бійця на арені, наведення на бійця — бейдж; клік — камера до бійця;</item>
        /// <item>після зміни ходу колесо обертається на крок (<see cref="WheelTurnSeconds"/>).</item>
        /// </list>
        /// Кольори — лише з <see cref="AlphaSkin"/>. Суцільного тла немає: арена
        /// під колесом видна, а клік блокують лише бейджі й центр — їхні
        /// прямокутники йдуть у <paramref name="blocking"/> (рецензія C1).
        /// </summary>
        private static Texture2D _wheelBacking;

        private static void DrawTurnWheel(IBattleHudData c, BattleView view, TurnWheelModel wheel, Rect area, float scale, List<Rect> blocking)
        {
            _wheelHoverUnitId = null;

            // Напівпрозора підкладка: без неї бейджі колеса змішувались із підписами бійців на арені — «Оксана» й
            // «Максим» стояли поруч двічі (тур 07.10.2026, чеклист G5). Арену під колесом видно, кліки й далі
            // блокують лише бейджі (рецензія C1).
            if (_wheelBacking == null)
                _wheelBacking = AlphaSkin.SolidTexture(new Color32(AlphaSkin.BgDark.r, AlphaSkin.BgDark.g, AlphaSkin.BgDark.b, 150));
            GUI.DrawTexture(area, _wheelBacking, ScaleMode.StretchToFill);

            float titleH = WheelTitleHeight(scale);
            ShadowedLabel(new Rect(area.x, area.y, area.width, titleH), UkrainianText.Get("ui.battle.wheel.title", false), AlphaSkin.TextDim);

            int count = wheel.Slots.Count;
            if (count == 0) return;

            float bw = WheelBadgeWidth(scale), bh = WheelBadgeHeight(scale);
            float rx = WheelRadiusX(scale), ry = WheelRadiusY(scale);
            float cx = area.x + area.width * 0.5f;
            float cy = area.y + titleH + (area.height - titleH - WheelHpBarHeight - WheelSkipLabelHeight) * 0.5f;

            // Поворот на крок: новий поточний приїжджає з позиції «наступний».
            string currentId = count > 0 ? wheel.Slots[0].UnitId : null;
            if (!string.Equals(currentId, _wheelLastCurrentId, StringComparison.Ordinal))
            {
                if (_wheelLastCurrentId != null) _wheelTurnStartedAt = Time.realtimeSinceStartup;
                _wheelLastCurrentId = currentId;
            }
            float t = (Time.realtimeSinceStartup - _wheelTurnStartedAt) / WheelTurnSeconds;
            float offset = t < 1f ? (360f / count) * (1f - Clamp01(t)) : 0f;

            // Обід колеса — пунктир, щоб бейджі читались як одне коло.
            for (int d = 0; d < 48; d++)
            {
                double a = d * Math.PI * 2.0 / 48.0;
                Widgets.SolidRect(new Rect(cx + rx * (float)Math.Cos(a) - 1.5f, cy + ry * (float)Math.Sin(a) - 1.5f, 3f, 3f), AlphaSkin.BgRaised);
            }

            var centerRect = new Rect(cx - 60f, cy - 12f, 120f, 24f);
            ShadowedLabel(centerRect, UkrainianText.Format("ui.battle.round", false, "round", I(wheel.CurrentRound)), AlphaSkin.TextMain);
            blocking.Add(centerRect);

            if (wheel.NextRoundStartsAt > 0)
                DrawWheelRoundMark(cx, cy, rx, ry,
                    TurnWheelModel.SlotAngleDegrees(wheel.NextRoundStartsAt, count) - 180f / count + offset,
                    wheel.Slots[wheel.NextRoundStartsAt].Round);

            for (int i = 0; i < count; i++)
            {
                var slot = wheel.Slots[i];
                var unit = FindUnit(view, slot.UnitId);
                if (unit == null) continue;

                double angle = (TurnWheelModel.SlotAngleDegrees(i, count) + offset) * Math.PI / 180.0;
                var rect = new Rect(cx + rx * (float)Math.Cos(angle) - bw * 0.5f, cy + ry * (float)Math.Sin(angle) - bh * 0.5f, bw, bh);

                bool hoveredOnMap = string.Equals(c.HoveredUnitId, slot.UnitId, StringComparison.Ordinal);
                bool hoveredHere = RectContainsMouse(rect);
                if (hoveredHere)
                {
                    _wheelHoverUnitId = slot.UnitId;
                    var evt = Event.current;
                    if (evt != null && evt.type == EventType.MouseDown && evt.button == 0)
                    {
                        c.FocusCamera(slot.UnitId);
                        evt.Use();
                    }
                }

                DrawWheelBadge(rect, TruncateName(c.ResolveDisplayName(unit), bw), slot, hoveredOnMap || hoveredHere);

                // Тонка смужка HP під бейджем — хто ледь живий, видно й з черги (рецензія C1).
                var hpRect = new Rect(rect.x, rect.y + rect.height + 1f, rect.width, WheelHpBarHeight);
                Widgets.FilledBarAt(hpRect, FilledFraction(unit.Hp, unit.HpMax), slot.IsDowned ? AlphaSkin.TextDim : SideColor(slot.Side));
                blocking.Add(new Rect(rect.x, rect.y, rect.width, rect.height + 1f + WheelHpBarHeight));

                if (slot.SkipsTurn)
                    ShadowedLabel(new Rect(rect.x, hpRect.y + hpRect.height, rect.width, WheelSkipLabelHeight),
                        UkrainianText.Get("ui.battle.wheel.skips", false), AlphaSkin.BattleStatus);
            }
        }

        private static void DrawWheelBadge(Rect rect, string name, TurnWheelSlot slot, bool highlighted)
        {
            Color32 bg, fg, frame;
            if (slot.IsDowned) { bg = AlphaSkin.BgRaised; fg = AlphaSkin.TextDim; frame = AlphaSkin.TextDim; }
            else if (slot.IsCurrent) { bg = AlphaSkin.BattleCurrentUnit; fg = AlphaSkin.BattleCurrentUnitText; frame = AlphaSkin.AccentActive; }
            else { bg = SideColorMuted(slot.Side); fg = AlphaSkin.TextMain; frame = SideColor(slot.Side); }
            if (highlighted && !slot.IsCurrent) frame = AlphaSkin.AccentHover;

            Widgets.SolidRect(rect, bg);

            float b = highlighted || slot.IsCurrent ? 3f : 2f;
            // Верх і низ — у всіх; боки — лише у своїх: форма рамки розрізняє сторони і без кольору.
            Widgets.SolidRect(new Rect(rect.x, rect.y, rect.width, b), frame);
            Widgets.SolidRect(new Rect(rect.x, rect.y + rect.height - b, rect.width, b), frame);
            if (slot.Side == "Player")
            {
                Widgets.SolidRect(new Rect(rect.x, rect.y, b, rect.height), frame);
                Widgets.SolidRect(new Rect(rect.x + rect.width - b, rect.y, b, rect.height), frame);
            }

            GUI.Label(rect, name, WheelText(fg));

            if (slot.IsDowned)
                Widgets.SolidRect(new Rect(rect.x + 4f, rect.y + rect.height * 0.5f - 1f, rect.width - 8f, 2f), AlphaSkin.TextDim);
        }

        /// <summary>Межа раунду на ободі: риска від центру назовні і «Р{n}» — з якого місця починається наступний раунд.</summary>
        private static void DrawWheelRoundMark(float cx, float cy, float rx, float ry, float angleDegrees, int round)
        {
            double a = angleDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            for (float k = 0.45f; k <= 1.12f; k += 0.04f)
                Widgets.SolidRect(new Rect(cx + rx * k * cos - 1.5f, cy + ry * k * sin - 1.5f, 3f, 3f), AlphaSkin.Accent);

            ShadowedLabel(new Rect(cx + rx * 0.62f * cos - 22f, cy + ry * 0.62f * sin - 10f, 44f, 20f),
                UkrainianText.Format("ui.battle.wheel.round_mark", false, "round", I(round)), AlphaSkin.Accent);
        }

        /// <summary>Ім'я, що не влазить у бейдж, обрізається з «…» (повне — у мітці над бійцем).</summary>
        private static string TruncateName(string name, float badgeWidth)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int maxChars = Math.Max(4, (int)((badgeWidth - 10f) / (AlphaSkin.OverlayNameFontSize * 0.58f)));
            return name.Length <= maxChars ? name : name.Substring(0, maxChars - 1) + "…";
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

            float width = Clamp(UiScale.Width * 0.4f, 360f, 720f);
            float height = 44f * Widgets.ScaleForScreen();
            var rect = new Rect((UiScale.Width - width) * 0.5f, topRect.height + 6f, width, height);

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

            // Зв'язки в бою (№14.8): хто з побратимів поруч прикриє — видно заздалегідь.
            if (unit.BondUnitIds != null)
                foreach (var partnerId in unit.BondUnitIds)
                {
                    var partner = FindUnit(c.View, partnerId);
                    if (partner == null || partner.IsOutOfBattle) continue;
                    bool near = Math.Max(Math.Abs(partner.Pos.X - unit.Pos.X), Math.Abs(partner.Pos.Y - unit.Pos.Y)) <= 1;
                    GUILayout.Label(UkrainianText.Format(near ? "ui.battle.bond.near" : "ui.battle.bond.far", false,
                        "name", c.ResolveDisplayName(partner)), AlphaSkin.HintLine);
                }

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
            _lastBottomPanelHeight = Clamp(h, 90f, UiScale.Height * 0.4f);
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
            if (Widgets.SecondaryButton(_logCollapsed ? "▸" : "▾", GUILayout.ExpandWidth(false)))
                _logCollapsed = !_logCollapsed;
            GUILayout.EndHorizontal();

            // «Автобій» і «Відступити» (Поправка №14.7, ROADMAP B13) — окремим рядком навпіл:
            // обидві про бій цілком, а не про хід бійця. В одному рядку з «Журналом»
            // «Відступити» обрізалось на всіх роздільностях (знімки «Щ», 29.09.2026:
            // 1280 — «Від», 1920 — «Відступи»). Не свій хід — причина рядком нижче (UI-04),
            // а не праворуч від кнопки, де вона виштовхувала кнопку за край панелі.
            GUILayout.BeginHorizontal();
            if (Widgets.SecondaryButton(UkrainianText.Get("ui.battle.autoresolve", false), GUILayout.ExpandWidth(true)))
                _confirmAutoResolve = true;
            GUILayout.Space(6f);
            string retreatLabel = UkrainianText.Get("ui.battle.retreat", false);
            bool canRetreat = c.IsPlayerTurn && !c.IsBusy;
            if (canRetreat)
            {
                if (Widgets.SecondaryButton(retreatLabel, GUILayout.ExpandWidth(true)))
                    _confirmRetreat = true;
            }
            else
                Widgets.DisabledButton(retreatLabel, null, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            if (!canRetreat)
                GUILayout.Label(UkrainianText.Get("ui.battle.retreat.only_own_turn", false), AlphaSkin.HintLine);

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

            // Об'єкт поля під курсором (Поправка №14.4): що це, що зробить і скільки коштує вдарити.
            if (attack == null && c.HasHoveredTile && c.Armed == ArmedAction.None)
            {
                var obj = ObjectAt(view, c.HoveredTileX, c.HoveredTileY);
                if (obj != null) { DrawObjectTooltip(c, view, obj); return; }
            }

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
            float height = attack != null ? EstimateAttackTooltipHeight(attack, FindUnit(view, attack.TargetId), view) : EstimatePathTooltipHeight(c, view);

            float freeTop = TopBarHeight(scale) + 8f;
            float freeBottom = UiScale.Height - _lastBottomPanelHeight - Widgets.ScreenPadding() - 8f;
            float freeRight = UiScale.Width - RightPanelWidth() - 8f;

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
            float freeBottom = UiScale.Height - _lastBottomPanelHeight - Widgets.ScreenPadding() - 8f;
            float freeRight = UiScale.Width - RightPanelWidth() - 8f;
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
        private static float EstimateAttackTooltipHeight(AttackPreviewView p, BattleUnitView target, BattleView view)
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
            if (p.IsFlanked) h += 28f;
            // Рядки треку бою (здача, перевірка здібності, прогноз, досьє) бувають довгими й
            // переносяться: міряємо їх за шириною підказки, а не сталою висотою — інакше
            // BeginArea мовчки обрізала низ (знімок «Щ» без кубика на 1600: «Здасться…» і
            // «Роль…» зникли під краєм підказки).
            h += WrappedHeight(SurrenderLine(target), AlphaSkin.HintLine);
            h += WrappedHeight(CheckLine(p), AlphaSkin.HintLine);
            h += WrappedHeight(PredictLine(p), AlphaSkin.Body);
            foreach (var line in DossierLines(target, view)) h += WrappedHeight(line, AlphaSkin.HintLine);
            if (p.Result != "Success") h += 30f;
            return h;
        }

        private static BattleTrapView TrapAt(BattleView view, int x, int y)
        {
            if (view?.Traps == null) return null;
            foreach (var t in view.Traps)
                if (t != null && t.Pos.X == x && t.Pos.Y == y) return t;
            return null;
        }

        // ================= поле бою: об'єкти, вогонь, укриття по боках (Поправка №14.4) =================

        private static BattleObjectView ObjectAt(BattleView view, int x, int y)
        {
            if (view?.Objects == null) return null;
            foreach (var o in view.Objects)
                if (o != null && o.Pos.X == x && o.Pos.Y == y) return o;
            return null;
        }

        private static BattleFireView FireAt(BattleView view, int x, int y)
        {
            if (view?.Fires == null) return null;
            foreach (var f in view.Fires)
                if (f != null && Math.Max(Math.Abs(f.Center.X - x), Math.Abs(f.Center.Y - y)) <= f.Radius) return f;
            return null;
        }

        private static readonly string[] SideKeys = { "north", "east", "south", "west" };

        /// <summary>«з півночі — повне, зі сходу — ½» для клітинки; null — укриття немає з жодного боку.</summary>
        private static string CoverSidesText(BattleView view, int x, int y)
        {
            var grid = view?.Grid;
            if (grid?.TileCoverSides == null) return null;
            int idx = x + y * grid.Width;
            if (idx < 0 || idx >= grid.TileCoverSides.Count) return null;
            var parts = grid.TileCoverSides[idx]?.Split('|');
            if (parts == null || parts.Length != 4) return null;

            var list = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                if (parts[i] == "None" || string.IsNullOrEmpty(parts[i])) continue;
                list.Add(UkrainianText.Format("ui.battle.cover.side", false,
                    "side", UkrainianText.Get("ui.battle.dir." + SideKeys[i], false),
                    "level", UkrainianText.Get("ui.battle.cover.level." + parts[i].ToLowerInvariant(), false)));
            }
            return list.Count == 0 ? null : string.Join(", ", list);
        }

        /// <summary>Підказка над об'єктом поля: назва, що зробить, і ціна удару для поточного бійця (UI-02).</summary>
        private static void DrawObjectTooltip(IBattleHudData c, BattleView view, BattleObjectView obj)
        {
            var lines = new List<KeyValuePair<string, GUIStyle>>();
            switch (obj.Kind)
            {
                case "PowderKeg":
                    lines.Add(new KeyValuePair<string, GUIStyle>(UkrainianText.Format("ui.battle.object.keg.effect", false,
                        "damage", I(obj.EffectDamage), "radius", I(obj.EffectRadius)), AlphaSkin.Body));
                    break;
                case "Haystack":
                    lines.Add(new KeyValuePair<string, GUIStyle>(UkrainianText.Format("ui.battle.object.hay.effect", false,
                        "rounds", I(obj.EffectRounds), "radius", I(obj.EffectRadius)), AlphaSkin.Body));
                    break;
                case "HighCover":
                    lines.Add(new KeyValuePair<string, GUIStyle>(UkrainianText.Get("ui.battle.object.high.effect", false), AlphaSkin.Body));
                    break;
                default:
                    lines.Add(new KeyValuePair<string, GUIStyle>(UkrainianText.Get("ui.battle.object.low.effect", false), AlphaSkin.Body));
                    break;
            }

            var current = FindUnit(view, view.CurrentUnitId);
            if (obj.IsTargetable)
            {
                if (c.IsPlayerTurn && current != null && current.AttackApCost > 0)
                    lines.Add(new KeyValuePair<string, GUIStyle>(UkrainianText.Format("ui.battle.object.hit_cost", false,
                        "cost", I(current.AttackApCost)), AlphaSkin.HintLine));
                else
                    lines.Add(new KeyValuePair<string, GUIStyle>(UkrainianText.Get("ui.battle.object.hit_own_turn", false), AlphaSkin.HintLine));
            }

            float scale = Widgets.ScaleForScreen();
            float height = 24f + 34f + lines.Count * 30f;
            float freeTop = TopBarHeight(scale) + 8f;
            float freeBottom = UiScale.Height - _lastBottomPanelHeight - Widgets.ScreenPadding() - 8f;
            float freeRight = UiScale.Width - RightPanelWidth() - 8f;
            var (x, y) = BattleTooltipLayout.PlaceNearAnchor(c.HoveredTileScreenX, c.HoveredTileScreenY, TooltipWidth, height,
                8f, freeTop, freeRight, freeBottom);

            GUILayout.BeginArea(new Rect(x, y, TooltipWidth, height), GUI.skin.box);
            GUILayout.Label(UkrainianText.Get("ui.battle.object.title." + obj.Kind, false), AlphaSkin.Body);
            foreach (var line in lines) GUILayout.Label(line.Key, line.Value);
            GUILayout.EndArea();
        }

        private static float EstimatePathTooltipHeight(IBattleHudData c, BattleView view)
        {
            float h = 24f + 34f + 30f; // відступи + заголовок + рядок ціни/відмови
            if (view.Grid != null && c.HasHoveredTile) h += 28f; // укриття клітинки
            if (c.HasHoveredTile && TrapAt(view, c.HoveredTileX, c.HoveredTileY) != null) h += 28f; // своя пастка
            if (c.HasHoveredTile && FireAt(view, c.HoveredTileX, c.HoveredTileY) != null) h += 28f; // вогонь (№14.4)
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

                // Правило без кубика: чим скінчиться саме цей удар — видно наперед.
                string predict = PredictLine(p);
                if (predict != null)
                    GUILayout.Label(predict, p.PredictedHits > 0 ? AlphaSkin.Body : PredictMissStyle);

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
                // Досьє (№14.6): опори невивченого ворога невідомі — число з «?».
                if (p.DamageUncertain) damageLine += " " + UkrainianText.Get("ui.battle.damage.uncertain", false);
                GUILayout.Label(damageLine, AlphaSkin.Body);
            }

            GUILayout.Label(UkrainianText.Format("ui.battle.ap_cost", false, "cost", I(p.ApCost)), AlphaSkin.Body);
            if (p.Result == "Success") DrawApForecast(view, p.ApCost, p.AbilityId);

            // Перевірка здібності (docs/ABILITIES.md): що з чим порівнюється — до кліку (інваріант 8).
            string checkLine = CheckLine(p);
            if (checkLine != null)
                GUILayout.Label(checkLine, p.CheckPasses ? AlphaSkin.HintLine : CheckFailStyle);

            if (target != null)
                GUILayout.Label(UkrainianText.Format("ui.battle.hp.target", false,
                    "current", I(target.Hp), "max", I(target.HpMax)), AlphaSkin.Body);

            if (!p.CoverIgnored && !string.Equals(p.Cover, "None", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(p.Cover))
                GUILayout.Label(UkrainianText.Get("ui.battle.cover." + p.Cover.ToLowerInvariant(), false), AlphaSkin.HintLine);

            // Фланг (Поправка №14.4): у цілі є укриття, але з цього боку воно не діє.
            if (p.IsFlanked)
                GUILayout.Label(UkrainianText.Get("ui.battle.flanked", false), AlphaSkin.HintLine);

            // Здача (Поправка №14.2): поріг видно до удару; бос — ніколи.
            string surrender = SurrenderLine(target);
            if (surrender != null) GUILayout.Label(surrender, AlphaSkin.HintLine);

            // Досьє (Поправка №14.6): роль — з контакту; решта — після розвідки чи бою.
            foreach (var line in DossierLines(target, view)) GUILayout.Label(line, AlphaSkin.HintLine);

            if (p.Result != "Success")
                GUILayout.Label(UkrainianText.Get(RejectionKey(p.Result), false), AlphaSkin.DangerText);
        }

        /// <summary>
        /// Висота рядка з переносом (0 — рядка немає). Ширина — та, що реально дістається
        /// мітці: ширина підказки мінус поля рамки (GUI.skin.box) і бічні відступи стилю;
        /// до висоти — вертикальні відступи стилю. Перша версія міряла ширше, ніж є, і
        /// останній перенос («…з / укриття.») обрізався (перезнімок «Щ» на 8266b49).
        /// </summary>
        private static float WrappedHeight(string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var box = GUI.skin.box;
            float width = TooltipWidth - box.padding.left - box.padding.right - style.margin.left - style.margin.right;
            return style.CalcHeight(new GUIContent(text), Math.Max(40f, width)) + style.margin.top + style.margin.bottom + 2f;
        }

        private static GUIStyle _predictMissStyle, _checkFailStyle;

        /// <summary>
        /// «Мимо» в прогнозі — не помилка, а прогноз: колір промаху з палітри бою
        /// (<see cref="AlphaSkin.BattleMiss"/>). Темно-червоний DangerText на темному тлі
        /// читався погано (знімок «Щ», 29.09.2026).
        /// </summary>
        private static GUIStyle PredictMissStyle =>
            _predictMissStyle ?? (_predictMissStyle = new GUIStyle(AlphaSkin.Body) { normal = { textColor = AlphaSkin.BattleMiss } });

        /// <summary>Перевірка здібності не вийде (ОД згорять) — яскраво-червоний з палітри бою, читається на темному тлі.</summary>
        private static GUIStyle CheckFailStyle =>
            _checkFailStyle ?? (_checkFailStyle = new GUIStyle(AlphaSkin.HintLine) { normal = { textColor = AlphaSkin.BattleEnemySide } });

        /// <summary>«Цей удар влучить» / «мимо» / «Влучить 1 з 2» — правило без кубика; null — правило з кубиком.</summary>
        private static string PredictLine(AttackPreviewView p)
        {
            if (p == null || p.PredictedShots <= 0) return null;
            if (p.PredictedShots > 1)
                return UkrainianText.Format("ui.battle.predict.multi", false, "hits", I(p.PredictedHits), "shots", I(p.PredictedShots));
            return UkrainianText.Get(p.PredictedHits > 0 ? "ui.battle.predict.hit" : "ui.battle.predict.miss", false);
        }

        /// <summary>Рядок перевірки здібності: «Залякування 3 проти Волі 2 — вийде», «Імунітет», «Броня ще ціла»; null — перевірки немає.</summary>
        private static string CheckLine(AttackPreviewView p)
        {
            if (p == null || string.IsNullOrEmpty(p.CheckKind)) return null;
            string vals = p.CheckSkill == "shred" ? "ui.battle.check.shred" : "ui.battle.check.contest";
            if (!string.IsNullOrEmpty(p.CheckBlockKey))
                return UkrainianText.Format("ui.battle.check.block." + p.CheckBlockKey, false,
                    "value", I(p.CheckValue), "threshold", I(p.CheckThreshold));
            if (string.IsNullOrEmpty(p.CheckSkill)) return null;
            string verdict = UkrainianText.Get(p.CheckPasses ? "ui.battle.check.pass" : "ui.battle.check.fail", false);
            return UkrainianText.Format(vals, false, "value", I(p.CheckValue), "threshold", I(p.CheckThreshold), "verdict", verdict);
        }

        private static void DrawHoverPath(IBattleHudData c, BattleView view, MovePathView path)
        {
            GUILayout.Label(UkrainianText.Get("ui.battle.hover.move_title", false), AlphaSkin.Body);

            if (path.Result == "Success")
            {
                GUILayout.Label(UkrainianText.Format("ui.battle.move.cost", false, "cost", I(path.ApCost)), AlphaSkin.Body);
                DrawApForecast(view, path.ApCost, null);
            }
            else if (path.Result == "NotReachable")
                GUILayout.Label(UkrainianText.Get("ui.battle.move.unreachable", false), AlphaSkin.DangerText);
            else
                GUILayout.Label(UkrainianText.Get(RejectionKey(path.Result), false), AlphaSkin.DangerText);

            if (view.Grid != null && c.HasHoveredTile)
            {
                // Укриття видно по боках (Поправка №14.4): з якого боку тут захищає і як.
                string sides = CoverSidesText(view, c.HoveredTileX, c.HoveredTileY);
                if (sides != null)
                    GUILayout.Label(UkrainianText.Format("ui.battle.cover.sides", false, "sides", sides), AlphaSkin.HintLine);

                var fire = FireAt(view, c.HoveredTileX, c.HoveredTileY);
                if (fire != null)
                    GUILayout.Label(UkrainianText.Format("ui.battle.fire.here", false, "rounds", I(fire.RoundsLeft)), AlphaSkin.DangerText);
            }

            var trapHere = TrapAt(view, c.HoveredTileX, c.HoveredTileY);
            if (trapHere != null && c.HasHoveredTile)
                GUILayout.Label(UkrainianText.Format("ui.battle.trap.here", false, "damage", I(trapHere.TrapDamage)), AlphaSkin.HintLine);

            if (path.OverwatchThreatTiles != null && c.HasHoveredTile)
                foreach (var t in path.OverwatchThreatTiles)
                    if (t.X == c.HoveredTileX && t.Y == c.HoveredTileY)
                    {
                        GUILayout.Label(UkrainianText.Get("ui.battle.move.threatened", false), AlphaSkin.DangerText);
                        break;
                    }
        }

        /// <summary>
        /// Подача бою П9: скільки ОД лишиться після наведеної дії і на які здібності, доступні зараз, після неї
        /// не вистачить (<see cref="ApForecast"/>). Лише в хід гравця — у підказці шляху й атаки.
        /// </summary>
        private static void DrawApForecast(BattleView view, int cost, string exceptAbilityId)
        {
            var current = FindUnit(view, view?.CurrentUnitId);
            if (current == null) return;
            int left = ApForecast.Left(current.Ap, cost);
            GUILayout.Label(UkrainianText.Format("ui.battle.ap_left", false, "left", I(left)), AlphaSkin.HintLine);
            var lost = ApForecast.NewlyUnaffordable(current.Abilities, current.Ap, left, exceptAbilityId);
            if (lost.Count == 0) return;
            var names = new List<string>(lost.Count);
            foreach (var id in lost) names.Add(UkrainianText.Get(id, false));
            GUILayout.Label(UkrainianText.Format("ui.battle.ap_lost", false, "abilities", string.Join(", ", names)), AlphaSkin.DangerText);
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
            // Раунд 3 (знімки): імена сусідніх бійців налазили одне на одне
            // («ПровідниЗастрільник орди»). Спершу розсуваємо блоки по
            // вертикалі (чиста функція з тестом), тоді малюємо. Мітки своїх
            // пасток — у тому ж розсуванні: інакше значок «Дозор» сусіда їх ховав.
            var visible = new List<BattleUnitOverlay>();
            var units = new List<BattleUnitView>();
            if (c.Overlays != null)
                foreach (var ov in c.Overlays)
                {
                    if (!ov.OnScreen) continue;
                    var unit = FindUnit(view, ov.UnitId);
                    if (unit == null) continue;
                    visible.Add(ov);
                    units.Add(unit);
                }

            var traps = new List<BattleTrapOverlay>();
            if (c.TrapOverlays != null)
                foreach (var trap in c.TrapOverlays)
                    if (trap != null && trap.OnScreen) traps.Add(trap);

            int n = visible.Count;
            int total = n + traps.Count;
            var centerX = new float[total];
            var top = new float[total];
            var width = new float[total];
            var heights = new float[total];
            float blockHeight = AlphaSkin.OverlayNameFontSize + 6f + 6f + 18f; // ім'я + HP + один значок
            for (int i = 0; i < n; i++)
            {
                centerX[i] = visible[i].ScreenX;
                top[i] = visible[i].ScreenY;
                width[i] = OverlayNameWidth(c.ResolveDisplayName(units[i]));
                heights[i] = blockHeight;
            }
            string trapText = UkrainianText.Get("ui.battle.overlay.trap", false);
            for (int k = 0; k < traps.Count; k++)
            {
                centerX[n + k] = traps[k].ScreenX;
                top[n + k] = traps[k].ScreenY - TrapLabelHeight * 0.5f;
                width[n + k] = OverlayNameWidth(trapText);
                heights[n + k] = TrapLabelHeight;
            }
            var resolved = BattleTooltipLayout.ResolveVerticalOverlaps(centerX, top, width, heights, 2f);

            // Пастки — першими: вони на землі, імена бійців над головами.
            for (int k = 0; k < traps.Count; k++)
                DrawTrapLabel(trapText, traps[k].ScreenX, resolved[n + k], width[n + k]);
            for (int i = 0; i < n; i++)
                DrawUnitOverlay(c, units[i], visible[i], resolved[i]);
        }

        private const float TrapLabelHeight = 18f;

        /// <summary>Бурштинова мітка своєї пастки — на клітинці, після розсування з підписами бійців.</summary>
        private static void DrawTrapLabel(string text, float centerX, float topY, float width)
        {
            var rect = new Rect(centerX - width * 0.5f, topY, width, TrapLabelHeight);
            Widgets.SolidRect(rect, AlphaSkin.BattleTrap);
            GUI.Label(rect, text, new GUIStyle(AlphaSkin.OverlayName) { normal = { textColor = AlphaSkin.BgDark } });
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
            // Наведення на бейдж у колесі черги — світла рамка на мітці цього
            // бійця (зв'язок колесо → мапа, Поправка №14.5). Інший колір, ніж
            // «можна вдарити»: одна барва — один сенс (Статут UI-01).
            bool wheelHovered = string.Equals(_wheelHoverUnitId, unit.Id, StringComparison.Ordinal)
                || string.Equals(_portraitHoverUnitId, unit.Id, StringComparison.Ordinal);
            if (wheelHovered && !ov.IsTargetable)
            {
                const float b = 2f;
                var frame = AlphaSkin.AccentHover;
                Widgets.SolidRect(new Rect(nameRect.x - b, nameRect.y - b, nameRect.width + 2f * b, b), frame);
                Widgets.SolidRect(new Rect(nameRect.x - b, nameRect.y + nameRect.height, nameRect.width + 2f * b, b), frame);
                Widgets.SolidRect(new Rect(nameRect.x - b, nameRect.y, b, nameRect.height), frame);
                Widgets.SolidRect(new Rect(nameRect.x + nameRect.width, nameRect.y, b, nameRect.height), frame);
            }

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

            // Подача П4: боєць в укритті — щит ліворуч від імені (пів-щит / повний).
            if (ov.CoverBest == "Half" || ov.CoverBest == "Full")
            {
                float size = nameRect.height;
                DrawShield(new Rect(nameRect.x - size - 2f, nameRect.y, size, size), ov.CoverBest == "Full");
            }

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

            if (unit.IsSurrendered)
            {
                var badge = new Rect(nameRect.x, badgeY, nameRect.width, 16f);
                Widgets.SolidRect(badge, AlphaSkin.BgRaised);
                GUI.Label(badge, UkrainianText.Get("ui.battle.overlay.surrendered", false),
                    new GUIStyle(AlphaSkin.OverlayName) { normal = { textColor = AlphaSkin.TextMain } });
                badgeY += 18f;
            }

            if (unit.IsDowned)
            {
                var badge = new Rect(nameRect.x, badgeY, nameRect.width, 16f);
                Widgets.SolidRect(badge, AlphaSkin.BattleEnemySide);
                GUI.Label(badge, UkrainianText.Format("ui.battle.overlay.downed", false, "turns", I(unit.DownWindowRemaining)),
                    new GUIStyle(AlphaSkin.OverlayName) { normal = { textColor = AlphaSkin.TextMain } });
                badgeY += 18f;
            }

            // Подача П3: шанс по цьому ворогу з клітинки, куди веде голограма руху (той самий, що покаже прев'ю
            // атаки, коли боєць там стоятиме — інваріант 8).
            if (ov.GhostHitChance >= 0)
            {
                var badge = new Rect(nameRect.x, badgeY, nameRect.width, 16f);
                Widgets.SolidRect(badge, new Color32(20, 40, 52, 220));
                GUI.Label(badge, UkrainianText.Format("ui.battle.overlay.ghost_chance", false, "chance", I(ov.GhostHitChance)),
                    new GUIStyle(AlphaSkin.OverlayName) { normal = { textColor = new Color(0.62f, 0.88f, 1f) } });
            }
        }

        // ================= подача П4: щити укриття (docs/research/RT_COMBAT_PRESENTATION.md) =================

        private const int ShieldTextureSize = 32;
        private static Texture2D _shieldHalf, _shieldFull;

        private static void DrawCoverMarkers(IBattleHudData c)
        {
            if (c.CoverMarkers == null) return;
            const float size = 22f;
            foreach (var m in c.CoverMarkers)
                if (m != null && m.OnScreen)
                    DrawShield(new Rect(m.ScreenX - size * 0.5f, m.ScreenY - size * 0.5f, size, size), m.Full);
        }

        private static void DrawShield(Rect rect, bool full)
        {
            var tex = full ? (_shieldFull != null ? _shieldFull : (_shieldFull = BuildShield(true)))
                           : (_shieldHalf != null ? _shieldHalf : (_shieldHalf = BuildShield(false)));
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
        }

        /// <summary>Текстура щита з маски <see cref="ShieldIcon"/>: контур темний, заливка світла (читається на траві й камені).</summary>
        private static Texture2D BuildShield(bool full)
        {
            int n = ShieldTextureSize;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var outline = new Color(0.08f, 0.07f, 0.06f, 0.95f);
            var fill = new Color(0.86f, 0.90f, 0.95f, 0.95f);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int cell = ShieldIcon.Cell(x, y, n, full);
                    // SetPixel рахує y знизу, маска — згори.
                    tex.SetPixel(x, n - 1 - y, cell == ShieldIcon.Outline ? outline : cell == ShieldIcon.Fill ? fill : clear);
                }
            tex.Apply();
            return tex;
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

                DrawSurrenderDecisions(c);

                GUILayout.Space(14f);
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.battle.result.next", false)))
                    c.AcknowledgeResult();
            });
        }

        /// <summary>
        /// Хто здався (Поправка №14.2): для кожного — відпустити, у полон, добити.
        /// Наслідок кожної кнопки підписано до кліку (Статут UI-02); кого не вирішили —
        /// відпустять (сказано прямо, щоб «Далі» не ховало вибору).
        /// </summary>
        private static void DrawSurrenderDecisions(IBattleHudData c)
        {
            var pending = c.PendingSurrenders;
            if (pending == null || pending.Count == 0) return;

            GUILayout.Space(8f);
            GUILayout.Label(UkrainianText.Get("ui.battle.surrender.title", false), AlphaSkin.SubHeader);
            GUILayout.Label(UkrainianText.Get("ui.battle.surrender.hint", false), AlphaSkin.HintLine);
            foreach (var s in pending)
            {
                string name = UkrainianText.Has("enemy." + s.DisplayNameKey, false)
                    ? UkrainianText.Get("enemy." + s.DisplayNameKey, false) : s.DisplayNameKey;
                string unitId = s.UnitId;
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(name, AlphaSkin.Body, GUILayout.ExpandWidth(true));
                if (Widgets.SecondaryButton(UkrainianText.Get("ui.battle.surrender.release", false)))
                    c.DecideSurrender(unitId, Game.Core.Combat.SurrenderFate.Release);
                if (Widgets.PrimaryButton(UkrainianText.Get(s.CanRecruitLater ? "ui.battle.surrender.capture" : "ui.battle.surrender.capture_no_recruit", false)))
                    c.DecideSurrender(unitId, Game.Core.Combat.SurrenderFate.Capture);
                if (Widgets.DangerButton(UkrainianText.Get("ui.battle.surrender.execute", false)))
                    c.DecideSurrender(unitId, Game.Core.Combat.SurrenderFate.Execute);
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// Рядки досьє ворога (Поправка №14.6): роль; для невивченого — що відкриє розвідка
        /// (пороги видно заздалегідь); для вивченого — опори і прийоми. Порожньо для своїх.
        /// </summary>
        private static List<string> DossierLines(BattleUnitView target, BattleView view = null)
        {
            var lines = new List<string>();
            if (target == null || string.IsNullOrEmpty(target.Dossier)) return lines;

            if (!string.IsNullOrEmpty(target.Role))
                lines.Add(UkrainianText.Format("ui.battle.dossier.role", false,
                    "role", UkrainianText.Get("ui.battle.role." + target.Role, false)));

            if (target.Dossier != "Studied")
            {
                lines.Add(UkrainianText.Format("ui.battle.dossier.partial", false,
                    "survival", I(view?.DossierScoutSurvival ?? 0), "wits", I(view?.DossierScoutWits ?? 0)));
                return lines;
            }

            if (target.ResistNotes != null && target.ResistNotes.Count > 0)
            {
                var parts = new List<string>();
                foreach (var note in target.ResistNotes)
                {
                    int colon = note.IndexOf(':');
                    if (colon <= 0) continue;
                    parts.Add(UkrainianText.Format("ui.battle.dossier.resist." + note.Substring(colon + 1), false,
                        "type", UkrainianText.Get("combat.damage_type." + note.Substring(0, colon).ToLowerInvariant(), false)));
                }
                lines.Add(UkrainianText.Format("ui.battle.dossier.resists", false, "list", string.Join(", ", parts)));
            }

            if (target.Abilities != null && target.Abilities.Count > 0)
            {
                var names = new List<string>();
                foreach (var a in target.Abilities) names.Add(UkrainianText.Get(a.Id, false));
                lines.Add(UkrainianText.Format("ui.battle.dossier.abilities", false, "list", string.Join(", ", names)));
            }
            return lines;
        }

        /// <summary>«Здасться при ≤30% здоров'я» / «Не здається» / «Бос — не здається ніколи» для ворога; null для своїх.</summary>
        private static string SurrenderLine(BattleUnitView target)
        {
            if (target == null || string.IsNullOrEmpty(target.Rank)) return null;
            // Досьє (№14.6): умову здачі відкриває розвідка чи бій.
            if (!string.IsNullOrEmpty(target.Dossier) && target.Dossier != "Studied")
                return UkrainianText.Get("ui.battle.surrender.unknown", false);
            if (target.CanSurrender)
                return UkrainianText.Format("ui.battle.surrender.at", false, "percent", I(target.SurrenderAtHpPercent));
            return UkrainianText.Get(target.Rank == "Boss" ? "ui.battle.surrender.boss" : "ui.battle.surrender.never", false);
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

        /// <summary>Приглушений тон тла бейджа на колесі черги ходів (§DrawTurnWheel) — не той самий насичений колір, що арена/оверлеї.</summary>
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
            float available = UiScale.Width - RightPanelWidth() - Widgets.ScreenPadding() * 4f;
            return Clamp(available, 560f, 1180f);
        }

        private static Rect FullScreenRect() => new Rect(0f, 0f, UiScale.Width, UiScale.Height);

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
