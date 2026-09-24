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

        /// <summary>
        /// Останній намальований прямокутник панелі HUD, у GUI-просторі (початок
        /// зверху-зліва — те, що використовує <c>GUILayout.BeginArea</c>, НЕ
        /// Unity screen-простір <c>Input.mousePosition</c>, де початок знизу).
        /// Fix-ревью (блокер): <c>BattleArenaController</c> звіряє курсор із цим
        /// прямокутником ДО <c>Physics.Raycast</c> у <c>UpdateHover</c>/
        /// <c>HandleClicks</c> — інакше клік по кнопці HUD (Кінець ходу,
        /// здібність, Дозор, Автобій, скрол логу) одночасно потрапляє променем
        /// у 3D-арену під тією самою ділянкою екрана (FrameCamera кадрує ввесь
        /// грід, а не «решту після HUD») і викликає CombatMove/Attack/Overwatch
        /// тим самим кліком. За замовчуванням (до першого Draw) — Rect.zero,
        /// що не містить жодної реальної точки курсора.
        /// </summary>
        public static Rect PanelRect { get; private set; }

        /// <summary>
        /// Права межа панелі в пікселях — та сама формула ширини, що й
        /// <see cref="Draw"/> нижче (не дублює магічні числа окремо).
        /// <see cref="BattleArenaController.FrameCamera"/> кличе це, щоб
        /// зсунути кадр камери й не ховати перші колонки грида під панеллю
        /// (фікс-ревью, major) — і працює будь-якої миті, ще до першого
        /// <see cref="Draw"/> цього кадру, бо формула залежить лише від
        /// поточного <c>Screen.width</c>, не від стану бою.
        /// </summary>
        public static float PanelRightEdgePixels()
        {
            return Widgets.ScreenPadding() + PanelWidthPixels();
        }

        private static float PanelWidthPixels() => Clamp(Screen.width * 0.34f, 420f, 620f);

        public static void Draw(IBattleHudData controller)
        {
            if (controller == null) return;

            GUI.skin = AlphaSkin.Build();

            if (controller.ResultPending)
            {
                // Widgets.Modal сам засвічує весь екран (Rect(0,0,Screen.width,
                // Screen.height)) — панель результату блокує курсор так само
                // на всій площі; HandleClicks() і так не викликається, поки
                // ResultPending (Update()), але тримаємо PanelRect чесним.
                PanelRect = new Rect(0f, 0f, Screen.width, Screen.height);
                DrawResultPanel(controller);
                return;
            }

            var view = controller.View;
            if (view == null)
            {
                // Крайовий випадок шва E1b/E2: Enter(session) викликаний, поки
                // GetBattleView() ще/вже повертає null (View лишається null,
                // доки перший непустий BattleView не прийде через Refresh() —
                // §BattleArenaController.cs). Порожній екран без жодного
                // виходу порушував би «кожен стан має видимий шлях вперед» —
                // тут мінімум повідомлення й вихід назад, без Combat*-команд
                // (їх викликати нема на чому — бою й немає).
                PanelRect = new Rect(0f, 0f, Screen.width, Screen.height);
                DrawUnavailablePanel(controller);
                return;
            }

            float padding = Widgets.ScreenPadding();
            float width = PanelWidthPixels();
            float height = Screen.height - padding * 2f;
            var area = new Rect(padding, padding, width, height);
            PanelRect = area;

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
                // Полірування (ціль А «HUD», owner: "enemy turn shows one clear
                // «Хід ворога…» line"): єдине місце, де ця репліка малюється —
                // DrawActionButtons нижче більше не дублює її на кожній
                // вимкненій кнопці (раніше рядок повторювався тричі поспіль).
                GUILayout.Label(UkrainianText.Get("ui.battle.enemyturn", false), AlphaSkin.SubHeader);
                return;
            }

            string name = c.ResolveDisplayName(current);
            GUILayout.Label(UkrainianText.Format("ui.battle.current_unit", false, "name", name), AlphaSkin.SubHeader);

            if (!c.IsPlayerTurn)
            {
                // Той самий рядок, тим самим виразним стилем — ім'я ворога вже
                // назване рядком вище, тут лише пояснення, чому кнопок немає.
                GUILayout.Label(UkrainianText.Get("ui.battle.enemyturn", false), AlphaSkin.SubHeader);
                return;
            }

            DrawHpLine(current);
            DrawApBar(current);
            if (current.IsDowned) GUILayout.Label(UkrainianText.Get("ui.battle.unit.downed", false), AlphaSkin.Tooltip);
        }

        /// <summary>
        /// Fix-ревью (major): Hp/HpMax дозволені гравцю (TEST_BUILD.md R17), але
        /// ніде в HUD не показувались — AP-бар був, HP не було ніде. Простий
        /// текстовий рядок, той самий стиль, що <see cref="DrawApBar"/>.
        /// </summary>
        private static void DrawHpLine(BattleUnitView unit)
        {
            GUILayout.Label(UkrainianText.Format("ui.battle.hp", false,
                "current", unit.Hp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "max", unit.HpMax.ToString(System.Globalization.CultureInfo.InvariantCulture)), AlphaSkin.Body);
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

            // Fix-ревью (major, той самий пункт, що DrawHpLine): гравець вирішує
            // «атакувати/відступити» саме за HP цілі — прев'ю шансу без HP цілі
            // поруч примушувало гадати. Ціль може зникнути з view між кадрами
            // (щойно впала) — тоді просто нічого не домальовуємо.
            var target = FindUnit(view, c.HoveredUnitId);
            if (target != null)
                GUILayout.Label(UkrainianText.Format("ui.battle.hp.target", false,
                    "current", target.Hp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "max", target.HpMax.ToString(System.Globalization.CultureInfo.InvariantCulture)), AlphaSkin.Body);
        }

        private static void DrawAbilities(IBattleHudData c)
        {
            if (!c.IsPlayerTurn) return;

            Widgets.Section(UkrainianText.Get("ui.battle.abilities", false), () =>
            {
                // Фікс-ревью (major, знайдено тур-автоплеєм): один суцільний
                // ряд на всі здібності не влазив у панель — найдовший підпис
                // ("Наказ пересунутися") з'їдав майже весь ряд, і "Залп"
                // обрізався по правому краю панелі (жодної рамки/паддінга —
                // просто впирався в межу BeginArea). Дві кнопки на ряд:
                // фіксований вибір, а не виміряний flow-layout, бо каталог тут
                // — короткий сталий масив (BattleArenaView.KnownAbilityIds,
                // §4.2.1 контракту), не список, що росте під час бою.
                const int perRow = 2;
                for (int row = 0; row * perRow < BattleArenaView.KnownAbilityIds.Length; row++)
                {
                    GUILayout.BeginHorizontal();
                    for (int i = row * perRow; i < BattleArenaView.KnownAbilityIds.Length && i < (row + 1) * perRow; i++)
                    {
                        string abilityId = BattleArenaView.KnownAbilityIds[i];
                        bool armed = c.Armed == ArmedAction.Ability && c.ArmedAbilityId == abilityId;
                        string label = UkrainianText.Get(abilityId, false);
                        if (armed) label = "» " + label;

                        if (Widgets.SecondaryButton(label))
                        {
                            // Fix-ревью (major): c.ArmAbility(null) НЕ повертає Armed
                            // у None (контролер ставить _armed=Ability незалежно від
                            // id) — гравець лишався «застряглим» з озброєною
                            // порожньою здібністю (тултип малював [] — сирий маркер
                            // відсутнього ключа). Той самий патерн, що вже коректно
                            // працює для кнопки Дозору нижче.
                            if (armed) c.CancelArmed(); else c.ArmAbility(abilityId);
                        }
                    }
                    GUILayout.EndHorizontal();
                }

                if (c.Armed == ArmedAction.Ability)
                    Widgets.TooltipLine(UkrainianText.Format("ui.battle.armed.ability", false,
                        "ability", UkrainianText.Get(c.ArmedAbilityId ?? string.Empty, false)));
            });
        }

        private static void DrawActionButtons(IBattleHudData c)
        {
            GUILayout.BeginHorizontal();

            // Полірування (ціль А «HUD», owner: "no overlapping disabled
            // buttons"): раніше тут стояли ДВІ Widgets.DisabledButton, кожна зі
            // своїм рядком-поясненням "Хід ворога…" — та сама репліка, що вже
            // намальована один раз у DrawCurrentUnit вище, повторювалась ще
            // двічі поспіль. Кнопок гравця під час ходу ворога немає взагалі
            // (нема чим керувати) — порожній ряд лишає тільки Автобій нижче.
            if (c.IsPlayerTurn)
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
