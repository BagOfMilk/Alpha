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

        /// <summary>
        /// Максимова квестова глава арки «Не за кров» (Поправка №7.8, п.4):
        /// той самий кеш, що вище, для ДРУГОЇ незалежної лінії — без нього
        /// щойно відкрита GameShell.RouteOfferedSceneContentIfAvailable глава не
        /// мала б ЖОДНОГО видимого екрана в Evening/Night (лише в HubScreen.
        /// DrawQuests на ранковій вкладці «Квести», куди гравець і не
        /// зазирнув би того самого вечора).
        /// </summary>
        private QuestOfferView _maksymQuestOffer;
        private int _maksymQuestOfferDay = int.MinValue;
        private SessionState _maksymQuestOfferState = (SessionState)(-1);

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
            if (_maksymQuestOffer == null || _maksymQuestOfferDay != view.Day || _maksymQuestOfferState != state)
            {
                _maksymQuestOffer = shell.TryRun(() => shell.Session.OfferQuestStage(DefaultQuests.MaksymCh1Id));
                _maksymQuestOfferDay = view.Day;
                _maksymQuestOfferState = state;
            }

            if (_questOffer != null && DrawOneQuestOffer(shell, g, _questOffer)) _questOffer = null;
            if (_maksymQuestOffer != null && DrawOneQuestOffer(shell, g, _maksymQuestOffer)) _maksymQuestOffer = null;
        }

        /// <summary>
        /// Один рядок пропозиції квесту — узагальнено з єдиної Гафіїної лінії
        /// (Поправка №7.8, п.4), тим самим малюнком: етап 0 показує офферний
        /// текст, Choice-етап — кнопки варіантів, Check-етап (без Options) —
        /// голий "Підтвердити" (саме так Гафіїн етап "grass" і резолвиться
        /// увечері — у нього немає готового варіанту, лише сама перевірка).
        /// </summary>
        private static bool DrawOneQuestOffer(GameShell shell, Gender g, QuestOfferView offer)
        {
            bool consumed = false;
            // Назва квесту в заголовку і поріг етапу-перевірки (інваріант 8):
            // раніше тут стояло голе «Пропозиція / Етап 1 / Підтвердити»
            // (знайдено довгим автопрогоном 25.09.2026).
            string questName = ScreenText.QuestName(offer.QuestId, g);
            string sectionTitle = string.IsNullOrEmpty(questName)
                ? UkrainianText.Get("ui.quest.offer.title", g)
                : UkrainianText.Format("ui.quest.offer.title_named", g, "quest", questName);
            string checkLine = ScreenText.QuestCheckLine(offer, g);
            Widgets.Section(sectionTitle, () =>
            {
                // Фікс-ревью (Поправка №7.8, п.4, знайдено тур-автоплеєм):
                // DefaultQuests.OfferKey — Гафіїна КОНКРЕТНА константа
                // ("quest.hafiya.offer"); поки лінія була одна, offer.Stage==0
                // завжди й був Гафіїним етапом, тож хардкод випадково влучав.
                // З Максимовою лінією той самий хардкод показував би ГАФІЇН
                // текст пропозиції на Максимовому етапі 0. Гафіїн questId
                // ("hafiya") не несе префікса "quest.", Максимів
                // ("quest.maksym.ch1") вже несе — той самий розлад
                // конвенції, що вже враховано в HubScreen.DrawQuestOffer.
                string offerKeyPrefixed = "quest." + offer.QuestId + ".offer";
                string offerKeyBare = offer.QuestId + ".offer";
                string offerBodyKey = UkrainianText.Has(offerKeyPrefixed, g) ? offerKeyPrefixed : offerKeyBare;
                if (offer.Stage == 0 && UkrainianText.Has(offerBodyKey, g))
                    GUILayout.Label(UkrainianText.Get(offerBodyKey, g), AlphaSkin.Body);
                else if (!string.IsNullOrEmpty(offer.StageTextKey) && UkrainianText.Has(offer.StageTextKey, g))
                    GUILayout.Label(UkrainianText.Get(offer.StageTextKey, g), AlphaSkin.Body);
                else if (checkLine.Length == 0)
                    GUILayout.Label(UkrainianText.Format("ui.quests.stage", g, "stage", offer.Stage.ToString()), AlphaSkin.Body);
                if (checkLine.Length > 0)
                    GUILayout.Label(checkLine, AlphaSkin.Body);

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
                                int idx = index;
                                string questId = offer.QuestId;
                                // Дві незалежні лінії квесту водночас (Гафія +
                                // Максим) ділять ОДИН GameSession.
                                // _currentQuestOffer (той самий фікс, що вже
                                // в HubScreen.DrawQuestOffer) — перезапит
                                // цього questId ПРЯМО перед ResolveQuestChoice
                                // синхронізує вказівник назад на нього.
                                shell.TryRun(() =>
                                {
                                    shell.Session.OfferQuestStage(questId);
                                    shell.Session.ResolveQuestChoice(idx);
                                });
                                consumed = true;
                            }
                        }
                        else
                        {
                            Widgets.DisabledButton(label, UkrainianText.Get("ui.common.none", g));
                        }
                    }
                }
                else if (Widgets.PrimaryButton(UkrainianText.Get(checkLine.Length > 0 ? "ui.quest.check.attempt" : "ui.common.confirm", g)))
                {
                    string questId = offer.QuestId;
                    shell.TryRun(() =>
                    {
                        shell.Session.OfferQuestStage(questId);
                        shell.Session.ResolveQuestChoice(0);
                    });
                    consumed = true;
                }
            });
            return consumed;
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
                    GUILayout.Label(UkrainianText.Format("ui.night.finale.enemy_count", g, "count", enemyCount.ToString(), "enemies", ScreenText.EnemiesCount(enemyCount)), AlphaSkin.Tooltip);
                if (Widgets.DangerButton(UkrainianText.Get("ui.decision.path.bloody", g)))
                    shell.TryRun(() => shell.Session.ResolveFinale(IncidentPath.Bloody));
            });
        }
    }
}
