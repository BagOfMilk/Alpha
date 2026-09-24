using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Титул (§ "Screens" пакета E1b): нова гра (правило бою, пропустити
    /// створення), продовжити/завантажити слот, тренувальний бій, вихід.
    /// </summary>
    public sealed class TitleScreen
    {
        private bool _hitRulePercent;
        private bool _skipCreation;
        private bool _showSlots;

        public void Draw(GameShell shell)
        {
            var g = shell.ProtagonistGender;
            var area = Widgets.CenteredRect(680f, 560f);
            GUILayout.BeginArea(area);
            Widgets.Panel(UkrainianText.Get("ui.title.header", g), () =>
            {
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.title.newgame", g)))
                {
                    var options = new NewGameOptions
                    {
                        HitRule = _hitRulePercent ? HitRuleKind.Percent : HitRuleKind.Threshold,
                        Seed = 1,
                        Roller = shell.Roller,
                        SkipCreation = _skipCreation
                    };
                    shell.TryRun(() => shell.Session.NewGame(options));
                    shell.ProtagonistGender = Gender.Male;
                }

                _hitRulePercent = GUILayout.Toggle(_hitRulePercent,
                    _hitRulePercent
                        ? UkrainianText.Get("ui.title.hitrule.percent", g)
                        : UkrainianText.Get("ui.title.hitrule.threshold", g));
                _skipCreation = GUILayout.Toggle(_skipCreation, UkrainianText.Get("ui.title.skip_creation", g));

                GUILayout.Space(10f);

                if (Widgets.SecondaryButton(UkrainianText.Get("ui.title.continue", g)))
                    _showSlots = !_showSlots;

                if (_showSlots) DrawSlots(shell, g);

                GUILayout.Space(10f);

                if (Widgets.SecondaryButton(UkrainianText.Get("ui.title.training", g)))
                {
                    var options = new TrainingBattleOptions { HitRule = _hitRulePercent ? HitRuleKind.Percent : HitRuleKind.Threshold };
                    shell.TryRun(() => shell.Session.NewTrainingBattle(options));
                }

                GUILayout.Space(20f);

                if (Widgets.DangerButton(UkrainianText.Get("ui.title.quit", g)))
                    Application.Quit(0);
            }, GUILayout.Width(680f));
            GUILayout.EndArea();
        }

        private static void DrawSlots(GameShell shell, Gender g)
        {
            var headers = Game.Gameplay.SaveFileStore.ListHeaders();
            for (int i = 0; i < headers.Count; i++)
            {
                var h = headers[i];
                string line = ScreenText.SaveSlotLine(h.Slot, h.Occupied, h.Headline, h.Day, g);
                GUILayout.BeginHorizontal();
                GUILayout.Label(line, AlphaSkin.Body, GUILayout.ExpandWidth(true));
                if (h.Occupied)
                {
                    if (Widgets.SecondaryButton("→", GUILayout.Width(48f)))
                    {
                        int slot = h.Slot;
                        shell.TryRun(() =>
                        {
                            string blob = Game.Gameplay.SaveFileStore.Read(slot);
                            shell.Session.PreloadSlot(slot, blob);
                            shell.Session.ContinueGame(slot, shell.Roller);
                        });
                    }
                }
                else
                {
                    Widgets.DisabledButton("→", null, GUILayout.Width(48f));
                }
                GUILayout.EndHorizontal();
            }
        }
    }
}
