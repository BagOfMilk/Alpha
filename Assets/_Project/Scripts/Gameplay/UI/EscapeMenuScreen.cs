using Game.Core.Characters.Creation;
using Game.Core.Session;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>Меню паузи (Esc): зберегти/завантажити/вийти. Save доступний лише коли GameSession сам дозволяє (Morning/FreePlay) — інакше кнопка вимкнена з причиною.</summary>
    public sealed class EscapeMenuScreen
    {
        public void Draw(GameShell shell)
        {
            var g = shell.ProtagonistGender;
            Widgets.Modal(UkrainianText.Get("ui.escape.title", g), () =>
            {
                var state = shell.Session.State;
                bool canSave = state == SessionState.Morning || state == SessionState.FreePlay;

                if (Widgets.PrimaryButton(UkrainianText.Get("ui.escape.resume", g)))
                    shell.SetEscapeOpen(false);

                GUILayout.Space(8f);

                if (canSave)
                {
                    if (Widgets.SecondaryButton(UkrainianText.Get("ui.escape.save", g)))
                    {
                        shell.TryRun(() => shell.Session.SaveState(Game.Gameplay.SaveFileStore.AutosaveSlot));
                        shell.SetEscapeOpen(false);
                    }
                }
                else
                {
                    Widgets.DisabledButton(UkrainianText.Get("ui.escape.save", g),
                        UkrainianText.Get("ui.common.none", g) + " (" + StateLabel(state, g) + ")");
                }

                GUILayout.Space(8f);

                if (Widgets.DangerButton(UkrainianText.Get("ui.escape.quit", g)))
                    Application.Quit(0);

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
