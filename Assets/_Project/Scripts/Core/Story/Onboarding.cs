using Game.Core.Characters;
using Game.Core.Companions;
using Game.Core.Quests;
using Game.Core.Saves;

namespace Game.Core.Story
{
    /// <summary>Шаги ведомого первого часа (US-17.4: системы открываются по одной).</summary>
    public enum OnboardingStep
    {
        Prologue = 0,        // пролог-бой на окраине + микс-развилка (US-17.1)
        SettlementIntro = 1, // доступ в село: осмотреться (подтверждение игрока)
        FirstAssignment = 2, // первое назначение на позицию (позиции → события/усиление)
        FirstWait = 3,       // первый ход времени (лечение/ожидание в днях)
        FirstExpedition = 4, // первый выезд: карта → вылазка → возврат
        Done = 5             // петля собрана — дальше игра сама
    }

    /// <summary>
    /// Ведомый онбординг (US-17.4): НЕ песочница — системы вводятся по одной, с
    /// мягкими контекстными подсказками. Чистая логика: авто-шаги закрываются
    /// предикатами по состоянию кампании (TryAdvance), событийные — уведомлениями
    /// от вызывающего слоя. Подача подсказок — UI-фаза.
    /// </summary>
    public sealed class OnboardingFlow
    {
        // Прогресс выводится из персистентных флагов кампании (SaveData.flags):
        // после загрузки сейва свежий flow фаст-форвардится циклом TryAdvance.
        public const string IntroAckFlag = "onboarding_intro_ack";
        public const string WaitedFlag = "onboarding_waited";
        public const string DoneFlag = "onboarding_done";

        public OnboardingStep Step { get; private set; } = OnboardingStep.Prologue;
        public bool IsDone => Step == OnboardingStep.Done;

        private int _dayAtWaitStart = -1;
        private bool _expeditionConcluded; // «липкое» событие: не теряется до шага

        /// <summary>Мягкая контекстная подсказка текущего шага (US-17.4).</summary>
        public string Hint
        {
            get
            {
                switch (Step)
                {
                    case OnboardingStep.Prologue:
                        return "Окраина под огнём. Этот бой — всерьёз: прикрой своих и уведи отряд.";
                    case OnboardingStep.SettlementIntro:
                        return "Это село станет твоей базой. Осмотрись: Лазарет лечит в днях, совет решает дела.";
                    case OnboardingStep.FirstAssignment:
                        return "Назначь напарника на позицию — его навыки будут решать события базы.";
                    case OnboardingStep.FirstWait:
                        return "Время здесь — ресурс: прокрути день, чтобы раны затянулись, а стройки шли.";
                    case OnboardingStep.FirstExpedition:
                        return "Выбери точку на карте и снаряди первый выезд. Материалы добываются только снаружи.";
                    default:
                        return "Петля собрана: город, ростер, вылазки. Дальше — твоя кампания.";
                }
            }
        }

        /// <summary>Пролог решён (флаг стоит) — вызывается и из TryAdvance по состоянию.</summary>
        public void NotifyPrologueResolved()
        {
            if (Step == OnboardingStep.Prologue) Step = OnboardingStep.SettlementIntro;
        }

        /// <summary>Игрок осмотрелся в селе (подтверждение вводного шага). Флаг персистится.</summary>
        public void AcknowledgeIntro(Campaign campaign)
        {
            campaign?.Flags.Add(IntroAckFlag);
            if (Step == OnboardingStep.SettlementIntro) Step = OnboardingStep.FirstAssignment;
        }

        /// <summary>
        /// Первая вылазка завершена (зовёт оркестрация после ConcludeExpedition).
        /// Событие «липкое»: завершение раньше шага не теряется (закроет его позже).
        /// </summary>
        public void NotifyExpeditionConcluded(Campaign campaign)
        {
            _expeditionConcluded = true;
            campaign?.Flags.Add(DoneFlag);
            if (Step == OnboardingStep.FirstExpedition) Step = OnboardingStep.Done;
        }

        /// <summary>
        /// Авто-продвижение по состоянию кампании (зови после действий игрока).
        /// true — шаг закрылся и Step сместился.
        /// </summary>
        public bool TryAdvance(Campaign campaign)
        {
            if (campaign == null) return false;
            switch (Step)
            {
                case OnboardingStep.Prologue:
                    if (campaign.Flags.Contains(DefaultQuests.PrologueDoneFlag))
                    {
                        Step = OnboardingStep.SettlementIntro;
                        return true;
                    }
                    return false;

                case OnboardingStep.SettlementIntro:
                    // Подтверждение персистится флагом — после загрузки шаг не повторяется.
                    if (campaign.Flags.Contains(IntroAckFlag))
                    {
                        Step = OnboardingStep.FirstAssignment;
                        return true;
                    }
                    return false;

                case OnboardingStep.FirstAssignment:
                    foreach (var slot in campaign.Base.Slots)
                        if (slot.IsOccupied)
                        {
                            Step = OnboardingStep.FirstWait;
                            _dayAtWaitStart = campaign.Base.CurrentDay;
                            return true;
                        }
                    return false;

                case OnboardingStep.FirstWait:
                    if (_dayAtWaitStart < 0) _dayAtWaitStart = campaign.Base.CurrentDay;
                    if (campaign.Flags.Contains(WaitedFlag) || campaign.Base.CurrentDay > _dayAtWaitStart)
                    {
                        campaign.Flags.Add(WaitedFlag);
                        Step = OnboardingStep.FirstExpedition;
                        return true;
                    }
                    return false;

                case OnboardingStep.FirstExpedition:
                    if (_expeditionConcluded || campaign.Flags.Contains(DoneFlag))
                    {
                        Step = OnboardingStep.Done;
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Исходы пролога (US-17.1): микс-развилка задаёт стартовые отношения, а выбор
    /// «бросить своих» может засеять антагониста — босса акта 1 («один уходит к
    /// злодею»). Сид зовётся после завершения пролог-квеста.
    /// </summary>
    public static class Prologue
    {
        public const string BossSeededFlag = "act1_boss_seeded";

        /// <summary>
        /// Если в прологе бросили своих (флаг prologue_spurned) и переговорщик пал
        /// духом до дна — он уходит к злодею со своим гиром (US-9.4). Возвращает
        /// снимок антагониста или null.
        /// </summary>
        public static AntagonistRecord TrySeedDefector(Campaign campaign)
        {
            if (campaign == null) return null;
            if (!campaign.Flags.Contains(DefaultQuests.PrologueSpurnedFlag)) return null;
            if (campaign.Flags.Contains(BossSeededFlag)) return null;

            var negotiator = campaign.Roster.Get("negotiator");
            if (negotiator == null || !DefectionSystem.ShouldDefect(negotiator)) return null;

            var record = DefectionSystem.Defect(negotiator, campaign.Base);
            campaign.Antagonists.Add(record);
            campaign.Flags.Add(BossSeededFlag);
            return record;
        }
    }
}
