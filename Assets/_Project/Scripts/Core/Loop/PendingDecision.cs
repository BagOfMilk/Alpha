using System.Collections.Generic;
using Game.Core.Checks;

namespace Game.Core.Loop
{
    /// <summary>Как игрок собирается разбираться с событием.</summary>
    public enum IncidentPath
    {
        /// <summary>Тихий путь: медленно, дорого, безопасно (Поправка №1).</summary>
        Quiet = 0,
        /// <summary>Кровавый путь: быстро и очень тяжело.</summary>
        Bloody = 1
    }

    /// <summary>
    /// Один вариант разбора с ПОКАЗАННЫМ порогом.
    ///
    /// Публичен целиком и намеренно: инвариант 8 требует, чтобы порог был виден
    /// ДО подтверждения. Скрывать здесь нечего — это не метрика скрытой шкалы,
    /// а условие задачи, которую игрок решает.
    /// </summary>
    public sealed class DecisionOption
    {
        public IncidentPath Path { get; }
        public SkillKey Skill { get; }
        public int Threshold { get; }
        public ApproachForm Form { get; }

        /// <summary>Кто возьмётся. Пусто — на позиции никого.</summary>
        public string BestActorId { get; }
        public bool HasCandidate { get; }

        /// <summary>Во что это выльется при нынешней расстановке.</summary>
        public OutcomeBand ExpectedBand { get; }

        public DecisionOption(IncidentPath path, SkillKey skill, int threshold, ApproachForm form,
            string bestActorId, bool hasCandidate, OutcomeBand expectedBand)
        {
            Path = path;
            Skill = skill;
            Threshold = threshold;
            Form = form;
            BestActorId = bestActorId;
            HasCandidate = hasCandidate;
            ExpectedBand = expectedBand;
        }
    }

    /// <summary>
    /// Событие, которое ЖДЁТ решения игрока.
    ///
    /// Зачем это вообще существует. В конвейере дня было двенадцать шагов
    /// вычисления и ноль шагов ввода: инцидент выбирался, проверка резолвилась и
    /// жертва назначалась внутри одного тика, а игрок узнавал обо всём из
    /// протокола. Всё, что система умела посчитать наперёд, она считала ВМЕСТО
    /// игрока — и день переставал быть ходом.
    ///
    /// Здесь же впервые оживает кровавый путь: он был выписан в семи инцидентах
    /// и не читался ни одной строкой кода, потому что выбирать было негде.
    /// </summary>
    public sealed class PendingDecision
    {
        public string IncidentId { get; }
        public string TopicId { get; }
        public string DomainTag { get; }
        public string RelevantPositionId { get; }
        public bool IsCrisis { get; }

        public IReadOnlyList<DecisionOption> Options { get; }

        public PendingDecision(string incidentId, string topicId, string domainTag,
            string relevantPositionId, bool isCrisis, IReadOnlyList<DecisionOption> options)
        {
            IncidentId = incidentId;
            TopicId = topicId;
            DomainTag = domainTag;
            RelevantPositionId = relevantPositionId;
            IsCrisis = isCrisis;
            Options = options ?? new List<DecisionOption>();
        }
    }
}
