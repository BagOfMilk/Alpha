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
                        UkrainianText.Get("ui.common.none", g) + " (" + state + ")");
                }

                GUILayout.Space(8f);

                if (Widgets.DangerButton(UkrainianText.Get("ui.escape.quit", g)))
                    Application.Quit(0);

                GUILayout.Space(8f);
                Widgets.TooltipLine(UkrainianText.Get("ui.escape.hint", g));
            }, () => shell.SetEscapeOpen(false));
        }
    }
}
