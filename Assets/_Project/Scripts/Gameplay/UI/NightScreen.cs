using Game.Core.Characters.Creation;
using Game.Core.Loop;
using Game.Core.Quests;
using Game.Core.Session;
using Game.Core.Session.Views;
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
        // Кеш пропозиції квесту (фікс-ревью, блокер) — той самий прийом, що й
        // у HubScreen.DrawQuests: OfferQuestStage логує подію на КОЖЕН виклик,
        // а це вечірній/нічний екран, який малюється щокадру, доки гравець на
        // ньому сидить. Ключ кешу — доба + стан (Evening/Night — два окремі
        // візити на добу, кожен вартий свого запиту).
        private QuestOfferView _questOffer;
        private int _questOfferDay = int.MinValue;
        private SessionState _questOfferState = (SessionState)(-1);

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

        private void DrawQuestOffer(GameShell shell, Gender g)
        {
            var view = shell.Session.CurrentView;
            var state = shell.Session.State;
            if (_questOffer == null || _questOfferDay != view.Day || _questOfferState != state)
            {
                _questOffer = shell.TryRun(() => shell.Session.OfferQuestStage(DefaultQuests.HafiyaId));
                _questOfferDay = view.Day;
                _questOfferState = state;
            }

            var offer = _questOffer;
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
                            {
                                shell.TryRun(() => shell.Session.ResolveQuestChoice(index));
                                _questOffer = null; // етап міг змінитись — перезапит наступним кадром
                            }
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
                    _questOffer = null;
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
                // Полірування (ціль 6 «Рішення», owner: "тактичний бій:
                // N ворогів"): раніше текст казав лише "тактичний бій" без
                // числа — GetFinaleEnemyCount() рахує ТОЙ САМИЙ план
                // (Finale.BuildAssault), що й реальний бій, без побічних
                // ефектів — чисте читання, не команда (без shell.TryRun,
                // щоб не смикати FeedVillageStage на кожен OnGUI-кадр).
                int enemyCount = shell.Session.GetFinaleEnemyCount();
                if (enemyCount > 0)
                    GUILayout.Label(UkrainianText.Format("ui.night.finale.enemy_count", g, "count", enemyCount.ToString()), AlphaSkin.Tooltip);
                if (Widgets.DangerButton(UkrainianText.Get("ui.decision.path.bloody", g)))
                    shell.TryRun(() => shell.Session.ResolveFinale(IncidentPath.Bloody));
            });
        }
    }
}
