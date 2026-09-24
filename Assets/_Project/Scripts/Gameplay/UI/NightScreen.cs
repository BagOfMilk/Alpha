using Game.Core.Characters.Creation;
using Game.Core.Loop;
using Game.Core.Quests;
using Game.Core.Session;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Вечір/ніч (§3.1-3.5): пропозиція квесту, патруль/сон, вікно реакції на
    /// форсовану кризу доби 5 (жодного View-прапорця "вікно відкрите" немає —
    /// GameSession сам кидає InvalidOperationException поза вікном, і
    /// TryRun показує це рядком, §"open issues" звіту E1b), фінал доби 5.
    /// </summary>
    public sealed class NightScreen
    {
        public void Draw(GameShell shell)
        {
            var g = shell.ProtagonistGender;
            bool evening = shell.Session.State == SessionState.Evening;

            Widgets.Panel(UkrainianText.Get(evening ? "ui.night.evening.title" : "ui.night.night.title", g), () =>
            {
                DrawQuestOffer(shell, g);

                if (evening)
                    DrawPatrol(shell, g);

                var view = shell.Session.CurrentView;
                if (view.Day == 5)
                    DrawCrisis(shell, g);

                if (!evening && !view.IsFreePlay && view.Day == 5)
                    DrawFinale(shell, g);

                GUILayout.Space(10f);
                if (evening)
                {
                    if (Widgets.PrimaryButton(UkrainianText.Get("ui.confirm_evening", g)))
                        shell.TryRun(() => shell.Session.ConfirmEvening());
                }
                else
                {
                    if (Widgets.PrimaryButton(UkrainianText.Get("ui.advance_night", g)))
                        shell.TryRun(() => shell.Session.AdvanceNight());
                }
            }, GUILayout.ExpandWidth(true));
        }

        private static void DrawQuestOffer(GameShell shell, Gender g)
        {
            var offer = shell.TryRun(() => shell.Session.OfferQuestStage(DefaultQuests.HafiyaId));
            if (offer == null) return;

            Widgets.Section(UkrainianText.Get("ui.quest.offer.title", g), () =>
            {
                if (offer.Stage == 0 && UkrainianText.Has(DefaultQuests.OfferKey, g))
                    GUILayout.Label(UkrainianText.Get(DefaultQuests.OfferKey, g), AlphaSkin.Body);
                else
                    GUILayout.Label(UkrainianText.Format("ui.quests.stage", g, "stage", offer.Stage.ToString()), AlphaSkin.Body);

                if (offer.Options != null && offer.Options.Count > 0)
                {
                    for (int index = 0; index < offer.Options.Count; index++)
                    {
                        var option = offer.Options[index];
                        string label = UkrainianText.Has(option.TextKey, g) ? UkrainianText.Get(option.TextKey, g) : option.TextKey;
                        if (option.HasCandidate)
                        {
                            if (Widgets.PrimaryButton(label))
                                shell.TryRun(() => shell.Session.ResolveQuestChoice(index));
                        }
                        else
                        {
                            Widgets.DisabledButton(label, UkrainianText.Get("ui.common.none", g));
                        }
                    }
                }
                else if (Widgets.PrimaryButton(UkrainianText.Get("ui.common.confirm", g)))
                {
                    shell.TryRun(() => shell.Session.ResolveQuestChoice(0));
                }
            });
        }

        private static void DrawPatrol(GameShell shell, Gender g)
        {
            Widgets.Section(UkrainianText.Get("ui.night.patrol.section", g), () =>
            {
                GUILayout.Label(UkrainianText.Get("ui.night.title", g), AlphaSkin.Body);
                GUILayout.BeginHorizontal();
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.night.patrol", g)))
                    shell.TryRun(() => shell.Session.SetPatrol(true));
                if (Widgets.SecondaryButton(UkrainianText.Get("ui.night.sleep", g)))
                    shell.TryRun(() => shell.Session.SetPatrol(false));
                GUILayout.EndHorizontal();
            });
        }

        private static void DrawCrisis(GameShell shell, Gender g)
        {
            Widgets.Section(UkrainianText.Get("ui.night.crisis.title", g), () =>
            {
                GUILayout.BeginHorizontal();
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.night.crisis.spend_gold", g)))
                    shell.TryRun(() => shell.Session.ReactToCrisis(CrisisReaction.SpendGold));
                if (Widgets.SecondaryButton(UkrainianText.Get("ui.night.crisis.send_defender", g)))
                    shell.TryRun(() => shell.Session.ReactToCrisis(CrisisReaction.SendDefender));
                if (Widgets.SecondaryButton(UkrainianText.Get("ui.night.crisis.ignore", g)))
                    shell.TryRun(() => shell.Session.ReactToCrisis(CrisisReaction.Ignore));
                GUILayout.EndHorizontal();
            });
        }

        private static void DrawFinale(GameShell shell, Gender g)
        {
            Widgets.Section(UkrainianText.Get("ui.night.finale.title", g), () =>
            {
                if (UkrainianText.Has("finale.tuhar.present", g)) GUILayout.Label(UkrainianText.Get("finale.tuhar.present", g), AlphaSkin.Tooltip);
                if (UkrainianText.Has("finale.burunda.present", g)) GUILayout.Label(UkrainianText.Get("finale.burunda.present", g), AlphaSkin.Tooltip);

                GUILayout.Label(UkrainianText.Get("finale.option.quiet", g), AlphaSkin.Body);
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.decision.path.quiet", g)))
                    shell.TryRun(() => shell.Session.ResolveFinale(IncidentPath.Quiet));

                GUILayout.Label(UkrainianText.Get("finale.option.bloody", g), AlphaSkin.Body);
                if (Widgets.DangerButton(UkrainianText.Get("ui.decision.path.bloody", g)))
                    shell.TryRun(() => shell.Session.ResolveFinale(IncidentPath.Bloody));
            });
        }
    }
}
