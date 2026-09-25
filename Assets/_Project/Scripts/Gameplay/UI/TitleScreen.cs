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

        /// <summary>
        /// Поправка №7 (рішення власника 24.09.2026): стиснутий темп шкали
        /// Напруги тестової збірки (<see cref="NewGameOptions.TestBuildTensionPace"/>) —
        /// дефолт true, той самий, що й у самого поля.
        /// </summary>
        private bool _testBuildTensionPace = true;

        public void Draw(GameShell shell)
        {
            var g = shell.ProtagonistGender;
            var area = Widgets.CenteredRect(680f, 620f);
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
                        SkipCreation = _skipCreation,
                        TestBuildTensionPace = _testBuildTensionPace
                    };
                    shell.TryRun(() => shell.Session.NewGame(options));
                    shell.ProtagonistGender = Gender.Male;
                }

                // Фікс-ревью (Фаза F): GUILayout.Toggle малює НЕЗМІНЕНИМ
                // вбудованим стилем Unity (AlphaSkin.Build() не заповнює
                // GUISkin.toggle) — крихітний чекбокс і дрібний текст замість
                // теплого темного скіну решти екрана, ледь читний на 1280×720.
                // TabButton — той самий "клікабельний перемикач" (Widgets.cs),
                // яким уже показані рід протагоніста/підхід вилазки/вкладки
                // хаба: великий, темний, з акцентним кольором обраного стану.
                Widgets.Section(UkrainianText.Get("ui.title.hitrule.section", g), () =>
                {
                    if (Widgets.TabButton(UkrainianText.Get("ui.title.hitrule.threshold", g), !_hitRulePercent))
                        _hitRulePercent = false;
                    if (Widgets.TabButton(UkrainianText.Get("ui.title.hitrule.percent", g), _hitRulePercent))
                        _hitRulePercent = true;
                });

                if (Widgets.TabButton(UkrainianText.Get("ui.title.skip_creation", g), _skipCreation))
                    _skipCreation = !_skipCreation;

                // Поправка №7: перемикач темпу шкали Напруги — той самий
                // TabButton-приём, що правило попадання вище.
                Widgets.Section(UkrainianText.Get("ui.title.tensionpace.section", g), () =>
                {
                    if (Widgets.TabButton(UkrainianText.Get("ui.title.tensionpace.test", g), _testBuildTensionPace))
                        _testBuildTensionPace = true;
                    if (Widgets.TabButton(UkrainianText.Get("ui.title.tensionpace.campaign", g), !_testBuildTensionPace))
                        _testBuildTensionPace = false;
                });

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
                    shell.RequestQuit(); // §GameShell._quitRequested — не кликати Application.Quit просто з OnGUI
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
                        // Фікс-ревью (Фаза F, "CORE GAPS"): рід протагоніста
                        // живе в GameSession (_pendingGender/ApplySave), не в
                        // GameShell — без цього рядка після Continue текст
                        // лишався б граматично неправильним (шкурка досі
                        // читає лише shell.ProtagonistGender), навіть коли
                        // сам сейв уже відновив правильний рід.
                        shell.ProtagonistGender = shell.Session.GetProtagonistCreationView()?.Gender ?? shell.ProtagonistGender;
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
