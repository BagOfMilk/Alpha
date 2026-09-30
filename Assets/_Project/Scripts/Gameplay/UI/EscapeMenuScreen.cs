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
    }
}
