using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>Меню паузи (Esc): зберегти/завантажити/вийти. Save доступний лише коли GameSession сам дозволяє (Morning/FreePlay) — інакше кнопка вимкнена з причиною.</summary>
    public sealed class EscapeMenuScreen
    {
        private bool _confirmQuit;
        private bool _confirmTitle;

        public void Draw(GameShell shell)
        {
            var g = shell.ProtagonistGender;
            Widgets.Modal(UkrainianText.Get("ui.escape.title", g), () =>
            {
                var state = shell.Session.State;
                bool canSave = state == SessionState.Morning || state == SessionState.FreePlay;
                // Бій v2 (docs/COMBAT_V2.md §3.5): «EscapeMenuScreen у бою —
                // без дій, що ламають бій (збереження посеред бою не
                // пропонувати)» — не просто вимкнена кнопка з причиною (як
                // для решти станів нижче), а її взагалі немає в бою: сейв
                // посеред бою — не та дія, яку варто навіть НАЗИВАТИ гравцю
                // як можливу.
                bool inBattle = state == SessionState.Battle;

                if (Widgets.PrimaryButton(UkrainianText.Get("ui.escape.resume", g)))
                    shell.SetEscapeOpen(false);

                GUILayout.Space(8f);

                // Збереження й завантаження — картки слотів (UX_DESIGN §5.14):
                // у будь-який слот, а не лише в автослот, як раніше; перезапис і
                // завантаження поверх гри — з підтвердженням. Зберегти можна
                // вранці й у вільній грі — причина видна на самій картці.
                if (!inBattle && Widgets.SecondaryButton(UkrainianText.Get("ux.escape.saves", g)))
                {
                    shell.SetEscapeOpen(false);
                    shell.OpenPanel(UxPanelId.Save, null);
                }
                if (!canSave && !inBattle)
                    Widgets.TooltipLine(UkrainianText.Format("ux.escape.save_when", g, "state", StateLabel(state, g)));

                GUILayout.Space(8f);

                // Налаштування: правило влучання (власник, 29.09.2026: «в настройках його
                // можна змінить»). Діє з наступного бою.
                bool percent = shell.Session.HitRule == HitRuleKind.Percent;
                GUILayout.Label(UkrainianText.Get("ui.title.hitrule.section", g), AlphaSkin.Body);
                GUILayout.BeginHorizontal();
                if (Widgets.TabButton(UkrainianText.Get("ui.title.hitrule.threshold", g), !percent) && percent)
                    shell.TryRun(() => shell.Session.SetHitRule(HitRuleKind.Threshold));
                if (Widgets.TabButton(UkrainianText.Get("ui.title.hitrule.percent", g), percent) && !percent)
                    shell.TryRun(() => shell.Session.SetHitRule(HitRuleKind.Percent));
                GUILayout.EndHorizontal();
                if (inBattle) Widgets.TooltipLine(UkrainianText.Get("ui.escape.hitrule.next_battle", g));

                GUILayout.Space(8f);

                // Швидкість бою (подача П1, docs/research/RT_COMBAT_PRESENTATION.md): анімації, рух і хід ворога
                // разом; діє одразу, зберігається між запусками. На розрахунок бою не впливає.
                GUILayout.Label(UkrainianText.Get("ui.battle_speed.title", g), AlphaSkin.Body);
                GUILayout.BeginHorizontal();
                foreach (float option in BattleTactTiming.SpeedOptions)
                    if (Widgets.TabButton(BattleTactTiming.SpeedLabel(option), System.Math.Abs(BattleSpeed.Multiplier - option) < 0.01f))
                        BattleSpeed.Multiplier = option;
                GUILayout.EndHorizontal();
                Widgets.TooltipLine(UkrainianText.Get("ui.battle_speed.hint", g));

                // Уповільнення на вбивстві (подача П6): коротке, не стакається, вимикається тут.
                bool slowMo = BattleSlowMoSetting.Enabled;
                GUILayout.BeginHorizontal();
                GUILayout.Label(UkrainianText.Get("ui.battle_slowmo.title", g), AlphaSkin.Body, GUILayout.Width(220f));
                bool rare = BattleSlowMoSetting.Rare;
                if (Widgets.TabButton(UkrainianText.Get("ui.battle_slowmo.always", g), slowMo && !rare)) { BattleSlowMoSetting.Enabled = true; BattleSlowMoSetting.Rare = false; }
                if (Widgets.TabButton(UkrainianText.Get("ui.battle_slowmo.rare", g), slowMo && rare)) { BattleSlowMoSetting.Enabled = true; BattleSlowMoSetting.Rare = true; }
                if (Widgets.TabButton(UkrainianText.Get("ui.battle_slowmo.off", g), !slowMo) && slowMo) BattleSlowMoSetting.Enabled = false;
                GUILayout.EndHorizontal();

                // Екшн-кадри бою (подача П12): через плече того, хто б'є, на кожен ~третій удар; на Низькій — немає.
                bool actionCam = BattleActionCameraSetting.Enabled;
                GUILayout.BeginHorizontal();
                GUILayout.Label(UkrainianText.Get("ui.battle_action_camera.title", g), AlphaSkin.Body, GUILayout.Width(220f));
                if (Widgets.TabButton(UkrainianText.Get("ui.battle_action_camera.on", g), actionCam) && !actionCam) BattleActionCameraSetting.Enabled = true;
                if (Widgets.TabButton(UkrainianText.Get("ui.battle_action_camera.off", g), !actionCam) && actionCam) BattleActionCameraSetting.Enabled = false;
                GUILayout.EndHorizontal();

                // Камера діалогу (розбір BG3, 08.10.2026: крупні плани в розмові вимикаються в налаштуваннях).
                bool closeUps = DialogueCameraSetting.CloseUps;
                GUILayout.BeginHorizontal();
                GUILayout.Label(UkrainianText.Get("ui.dialogue_camera.title", g), AlphaSkin.Body, GUILayout.Width(220f));
                if (Widgets.TabButton(UkrainianText.Get("ui.dialogue_camera.close", g), closeUps) && !closeUps) DialogueCameraSetting.CloseUps = true;
                if (Widgets.TabButton(UkrainianText.Get("ui.dialogue_camera.village", g), !closeUps) && closeUps) DialogueCameraSetting.CloseUps = false;
                GUILayout.EndHorizontal();

                GUILayout.Space(8f);

                // Графіка (Поправка №21.1): три рівні, діє одразу, зберігається між запусками.
                GUILayout.Label(UkrainianText.Get("ui.gfx.title", g), AlphaSkin.Body);
                DrawGraphicsLevels(shell, g);
                Widgets.TooltipLine(UkrainianText.Get("ui.gfx.hint", g));

                // Розмір інтерфейсу (власник 07.10.2026: «Абсолютно увесь UI завеликий, звенши усе вдвічі»).
                GUILayout.Label(UkrainianText.Get("ui.uiscale.title", g), AlphaSkin.Body);
                DrawUiScale(g);
                Widgets.TooltipLine(UkrainianText.Get("ui.uiscale.hint", g));

                GUILayout.Space(8f);

                // Гучність (віха M1.19): п'ять шарів, крок 10 %; зберігається між запусками.
                GUILayout.Label(UkrainianText.Get("ui.sound.section", g), AlphaSkin.Body);
                foreach (SoundBus bus in System.Enum.GetValues(typeof(SoundBus)))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(UkrainianText.Get(SoundSettings.TextKey(bus), g), AlphaSkin.Body, GUILayout.Width(220f));
                    if (Widgets.SecondaryButton("−", GUILayout.Width(44f))) SoundSettings.Nudge(bus, -1);
                    GUILayout.Label(SoundSettings.Percent(bus) + " %", AlphaSkin.Body, GUILayout.Width(70f));
                    if (Widgets.SecondaryButton("+", GUILayout.Width(44f))) SoundSettings.Nudge(bus, 1);
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(8f);

                // «У головне меню» — лише з підтвердженням (UX-12): незбережене пропаде.
                // У бою — немає (як і збереження: посеред бою ця дія ламає бій).
                if (!inBattle)
                {
                    if (!_confirmTitle)
                    {
                        if (Widgets.SecondaryButton(UkrainianText.Get("ux.escape.to_title", g))) _confirmTitle = true;
                    }
                    else
                    {
                        GUILayout.Label(UkrainianText.Get("ux.escape.to_title.question", g), AlphaSkin.Body);
                        GUILayout.BeginHorizontal();
                        if (Widgets.PrimaryButton(UkrainianText.Get("ux.common.cancel", g))) _confirmTitle = false;
                        if (Widgets.DangerButton(UkrainianText.Get("ux.escape.to_title.verb", g)))
                        {
                            _confirmTitle = false;
                            shell.ReturnToTitle();
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.Space(8f);
                }

                // Вихід — лише з підтвердженням (UX-12): незбережене пропаде.
                if (!_confirmQuit)
                {
                    if (Widgets.DangerButton(UkrainianText.Get("ui.escape.quit", g))) _confirmQuit = true;
                }
                else
                {
                    GUILayout.Label(UkrainianText.Get("ux.escape.quit.question", g), AlphaSkin.Body);
                    GUILayout.BeginHorizontal();
                    if (Widgets.PrimaryButton(UkrainianText.Get("ux.common.cancel", g))) _confirmQuit = false;
                    if (Widgets.DangerButton(UkrainianText.Get("ux.escape.quit.verb", g)))
                        shell.RequestQuit(); // §GameShell._quitRequested — не кликати Application.Quit просто з OnGUI
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(8f);
                Widgets.TooltipLine(UkrainianText.Get("ui.escape.hint", g));
            });
            // onClose навмисно НЕ передано (фікс-ревью, блокер): GameShell —
            // єдиний власник переходу _escapeOpen, він же єдиний читач Escape
            // (Event-based, раз на подію). Другий незалежний слухач тут (через
            // Widgets.Modal.onClose, теж на сирому Input.GetKeyDown) двічі
            // перемикав прапорець за той самий кадр — меню встигало лише
            // промайнути одним Repaint і одразу закривалось.
        }

        /// <summary>SessionState — англійський enum; тут лише переклад слова причини, а не сам стан (інваріант R7).</summary>
        private static string StateLabel(SessionState state, Gender g)
        {
            string key = "ui.state." + state.ToString().ToLowerInvariant();
            return UkrainianText.Has(key, g) ? UkrainianText.Get(key, g) : state.ToString();
        }

        /// <summary>Кнопки розміру інтерфейсу (50 / 75 / 100 %) — спільні для меню паузи й титулу.</summary>
        internal static void DrawUiScale(Gender g)
        {
            GUILayout.BeginHorizontal();
            foreach (float option in UiScale.Options)
            {
                string label = UkrainianText.Format("ui.uiscale.option", g, "pct", ((int)System.Math.Round(option * 100f)).ToString());
                if (Widgets.TabButton(label, System.Math.Abs(UiScale.Factor - option) < 0.01f)) UiScale.Factor = option;
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>Три кнопки рівня графіки — спільні для меню паузи й титулу.</summary>
        internal static void DrawGraphicsLevels(GameShell shell, Gender g)
        {
            var current = GraphicsTier.Current;
            GUILayout.BeginHorizontal();
            if (Widgets.TabButton(UkrainianText.Get("ui.gfx.low", g), current == GraphicsLevel.Low))
                shell.SetGraphics(GraphicsLevel.Low);
            if (Widgets.TabButton(UkrainianText.Get("ui.gfx.medium", g), current == GraphicsLevel.Medium))
                shell.SetGraphics(GraphicsLevel.Medium);
            if (Widgets.TabButton(UkrainianText.Get("ui.gfx.high", g), current == GraphicsLevel.High))
                shell.SetGraphics(GraphicsLevel.High);
            GUILayout.EndHorizontal();
        }
    }
}
