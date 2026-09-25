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
            DrawAbilities(c, view);
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

        /// <summary>
        /// Фікс-ревью (major, раунд 2, знайдено QA): раніше один суцільний
        /// <c>BeginHorizontal</c> на всю чергу ходу — на п'ятьох+ юнітах
        /// (довгі імена на кшталт "Розвідник орди" двічі) останній(і) бейдж(і)
        /// фізично виїжджав(ли) за праву межу панелі HUD прямо в 3D-арену,
        /// не обрізаний по рамці, а НАМАЛЬОВАНИЙ ПОВЕРХ сцени — IMGUI не
        /// клипає дочірні елементи горизонтальної групи по контейнеру.
        /// Загортаємо в новий ряд, щойно накопичена ширина перевищує вміст
        /// панелі (<see cref="Widgets.BadgeWidth"/> — той самий стиль, що й
        /// сам бейдж, тож оцінка рівно та, що піде на екран).
        /// </summary>
        private static void DrawInitiativeStrip(IBattleHudData c, BattleView view)
        {
            Widgets.Section(UkrainianText.Get("ui.battle.initiative", false), () =>
            {
                float maxWidth = PanelWidthPixels() - Widgets.PanelContentInset();
                float rowWidth = 0f;
                bool rowOpen = false;

                if (view.InitiativeOrder != null)
                    foreach (var id in view.InitiativeOrder)
                    {
                        var unit = FindUnit(view, id);
                        string name = unit != null ? c.ResolveDisplayName(unit) : id;
                        bool current = string.Equals(id, view.CurrentUnitId, System.StringComparison.Ordinal);
                        float badgeWidth = Widgets.BadgeWidth(name);

                        if (rowOpen && rowWidth + badgeWidth > maxWidth)
                        {
                            GUILayout.EndHorizontal();
                            rowOpen = false;
                            rowWidth = 0f;
                        }
                        if (!rowOpen)
                        {
                            GUILayout.BeginHorizontal();
                            rowOpen = true;
                        }

                        Widgets.Badge(name, current ? AlphaSkin.Accent : AlphaSkin.BgRaised);
                        rowWidth += badgeWidth;
                    }

                if (rowOpen) GUILayout.EndHorizontal();
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
            DrawWeaponLine(current);
            DrawApBar(current);
            DrawStatuses(current);
            if (current.IsDowned) GUILayout.Label(UkrainianText.Get("ui.battle.unit.downed", false), AlphaSkin.Tooltip);
        }

        /// <summary>Яка зброя екіпірована (власник, 25.09.2026: «хочу бачити яка зброя экіпірована») — той самий стиль рядка, що HP/AP нижче. Юніт без зброї (безоружний ворог) — рядок просто не малюється.</summary>
        private static void DrawWeaponLine(BattleUnitView unit)
        {
            if (string.IsNullOrEmpty(unit.WeaponId)) return;
            GUILayout.Label(UkrainianText.Format("ui.battle.weapon", false,
                "name", UkrainianText.Get(unit.WeaponId, false)), AlphaSkin.Body);
        }

        /// <summary>
        /// Дебаг §6.1 №32 (24.09.2026): <c>BattleUnitView.Statuses</c> раніше
        /// не малювався НІДЕ в HUD — гравець не бачив накладені стани навіть
        /// коли Core коректно їх рахував. Той самий стиль значка, що дозор
        /// (<see cref="DrawApBar"/>, <c>Widgets.Badge</c>); ключ без мапінгу
        /// (<see cref="BattleArenaView.StatusLabelKey"/> повернув null) —
        /// пропускаємо значок, а не показуємо сирий enum-рядок гравцю.
        /// </summary>
        private static void DrawStatuses(BattleUnitView unit)
        {
            if (unit.Statuses == null || unit.Statuses.Count == 0) return;

            GUILayout.BeginHorizontal();
            foreach (var status in unit.Statuses)
            {
                string key = BattleArenaView.StatusLabelKey(status);
                if (key == null) continue;
                Widgets.Badge(UkrainianText.Get(key, false), AlphaSkin.Danger);
            }
            GUILayout.EndHorizontal();
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

            // Власник, 25.09.2026: «хочу бачити ... скільки урону нанесе
            // атака». Діапазон, не кидок (§BattleArenaController.
            // UpdateHitChancePreview — прев'ю рахується щокадру, поки курсор
            // на цілі, тож рол тут неможливий, лише детермінований min/max).
            // Крит показуємо лише коли він реально відрізняється від max —
            // на True-уроні (без резисту) і без криту різниці нема сенсу
            // повторювати те саме число двічі.
            if (c.HoveredDamageMax > 0 || c.HoveredDamageMin > 0)
            {
                string damageLine = c.HoveredDamageCrit > c.HoveredDamageMax
                    ? UkrainianText.Format("ui.battle.damage.preview.crit", false,
                        "min", c.HoveredDamageMin.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "max", c.HoveredDamageMax.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "crit", c.HoveredDamageCrit.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    : UkrainianText.Format("ui.battle.damage.preview", false,
                        "min", c.HoveredDamageMin.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "max", c.HoveredDamageMax.ToString(System.Globalization.CultureInfo.InvariantCulture));
                GUILayout.Label(damageLine, AlphaSkin.Body);
            }

            // Fix-ревью (major, той самий пункт, що DrawHpLine): гравець вирішує
            // «атакувати/відступити» саме за HP цілі — прев'ю шансу без HP цілі
            // поруч примушувало гадати. Ціль може зникнути з view між кадрами
            // (щойно впала) — тоді просто нічого не домальовуємо.
            var target = FindUnit(view, c.HoveredUnitId);
            if (target != null)
            {
                GUILayout.Label(UkrainianText.Format("ui.battle.hp.target", false,
                    "current", target.Hp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "max", target.HpMax.ToString(System.Globalization.CultureInfo.InvariantCulture)), AlphaSkin.Body);
                DrawStatuses(target);
            }
        }

        /// <summary>
        /// Фікс (відомий розрив звіту пакета E2, §BattleArenaView.KnownAbilityIds):
        /// раніше тут малювався ОДИН фіксований каталог із чотирьох кнопок для
        /// БУДЬ-ЯКОГО юніта, незалежно від того, чи він узагалі знає здібність
        /// (напарник знає її лише за порогом скіла — <c>CombatUnit.Abilities</c>,
        /// AbilityDefinition.RequiredSkillLevel). Клік по здібності, якої юніт
        /// не знає, чи на яку бракує AP/вона на відкаті, беззвучно «відхилявся»
        /// ядром (CombatActionResult != Success) — гравець бачив кнопку, тиснув
        /// і не розумів, чому нічого не відбувається («не можу нормально щось
        /// використовувати»). Тепер кнопки йдуть з РЕАЛЬНОГО списку поточного
        /// юніта (<c>BattleUnitView.Abilities</c>), кожна підписана ціною в AP,
        /// а недоступна зараз (відкат чи нестача AP) — сірою <see
        /// cref="Widgets.DisabledButton"/> з причиною поруч, а не звичайною
        /// кнопкою, що мовчки відмовляє по кліку (Поправка №1, «шлях видно»).
        /// </summary>
        private static void DrawAbilities(IBattleHudData c, BattleView view)
        {
            if (!c.IsPlayerTurn) return;
            var current = FindUnit(view, view.CurrentUnitId);
            if (current?.Abilities == null || current.Abilities.Count == 0) return;

            Widgets.Section(UkrainianText.Get("ui.battle.abilities", false), () =>
            {
                // Фікс-ревью (major, знайдено тур-автоплеєм): один суцільний
                // ряд на всі здібності не влазив у панель — найдовший підпис
                // ("Наказ пересунутися") з'їдав майже весь ряд, і "Залп"
                // обрізався по правому краю панелі (жодної рамки/паддінга —
                // просто впирався в межу BeginArea). Дві кнопки на ряд:
                // фіксований вибір, а не виміряний flow-layout — здібностей
                // на юніта завжди мало (2–4).
                const int perRow = 2;
                var abilities = current.Abilities;
                for (int row = 0; row * perRow < abilities.Count; row++)
                {
                    GUILayout.BeginHorizontal();
                    for (int i = row * perRow; i < abilities.Count && i < (row + 1) * perRow; i++)
                    {
                        var ability = abilities[i];
                        bool armed = c.Armed == ArmedAction.Ability && c.ArmedAbilityId == ability.Id;
                        string apSuffix = " (" + ability.ApCost.ToString(System.Globalization.CultureInfo.InvariantCulture) + " AP)";
                        string label = (armed ? "» " : string.Empty) + UkrainianText.Get(ability.Id, false) + apSuffix;

                        bool onCooldown = ability.CooldownRemaining > 0;
                        bool notEnoughAp = current.Ap < ability.ApCost;
                        if (onCooldown || notEnoughAp)
                        {
                            string reason = onCooldown
                                ? UkrainianText.Format("ui.battle.ability.cooldown", false,
                                    "turns", ability.CooldownRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture))
                                : UkrainianText.Get("ui.battle.ability.not_enough_ap", false);
                            Widgets.DisabledButton(label, reason);
                        }
                        else if (Widgets.SecondaryButton(label))
                        {
                            // Fix-ревью (major): c.ArmAbility(null) НЕ повертає Armed
                            // у None (контролер ставить _armed=Ability незалежно від
                            // id) — гравець лишався «застряглим» з озброєною
                            // порожньою здібністю (тултип малював [] — сирий маркер
                            // відсутнього ключа). Той самий патерн, що вже коректно
                            // працює для кнопки Дозору нижче.
                            if (armed) c.CancelArmed(); else c.ArmAbility(ability.Id);
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

        /// <summary>
        /// Найновіший рядок — зверху (як у стрічці подій хабу і у фолбеку
        /// BattleScreen): журнал бою тепер повний — рух, стани, дозор, смерті
        /// (<see cref="BattleLogText"/>), — і в хронологічному порядку свіжий
        /// рядок ховався під нижнім краєм прокрутки вже на другому ході.
        /// </summary>
        private static void DrawLog(IBattleHudData c)
        {
            Widgets.Section(UkrainianText.Get("ui.battle.log", false), () =>
            {
                _logScroll = Widgets.ScrollListBegin(_logScroll, GUILayout.Height(160f));
                var lines = c.LogLines;
                for (int i = lines.Count - 1; i >= 0; i--) GUILayout.Label(lines[i], AlphaSkin.Body);
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
