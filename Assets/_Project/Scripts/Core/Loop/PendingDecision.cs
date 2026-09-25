using System.Collections.Generic;
using Game.Core.Checks;

namespace Game.Core.Loop
{
    /// <summary>Як гравець збирається розбиратися з подією.</summary>
    public enum IncidentPath
    {
        /// <summary>Тихий шлях: повільно, дорого, безпечно (Поправка №1).</summary>
        Quiet = 0,
        /// <summary>Кривавий шлях: швидко і дуже важко.</summary>
        Bloody = 1
    }

    /// <summary>
    /// Один варіант розбору з ПОКАЗАНИМ порогом.
    ///
    /// Публічний цілком і навмисно: інваріант 8 вимагає, щоб поріг був видний
    /// ДО підтвердження. Приховувати тут нічого — це не метрика прихованої шкали,
    /// а умова задачі, яку гравець вирішує.
    /// </summary>
    public sealed class DecisionOption
    {
        public IncidentPath Path { get; }
        public SkillKey Skill { get; }
        public int Threshold { get; }
        public ApproachForm Form { get; }

        /// <summary>Хто візьметься. Порожньо — на позиції нікого.</summary>
        public string BestActorId { get; }
        public bool HasCandidate { get; }

        /// <summary>У що це виллється за нинішньою розстановкою.</summary>
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
    /// Подія, яка ЧЕКАЄ рішення гравця.
    ///
    /// Навіщо це взагалі існує. У конвеєрі дня було дванадцять кроків
    /// обчислення і нуль кроків уведення: інцидент обирався, перевірка резолвилася і
    /// жертва призначалася всередині одного тика, а гравець дізнавався про все з
    /// протоколу. Усе, що система вміла порахувати наперед, вона рахувала ЗАМІСТЬ
    /// гравця — і день переставав бути ходом.
    ///
    /// Тут же вперше оживає кривавий шлях: він був виписаний у семи інцидентах
    /// і не читався жодним рядком коду, бо обирати було нема де.
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
