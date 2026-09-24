using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Quests;

namespace Game.Core.Scenes
{
    /// <summary>
    /// Проверка сценария БЕЗ редактора (Поправка №5.8) — ради этого сцена и
    /// сделана данными.
    ///
    /// Проверяется то, что ломает постановку молча: сцена без плана (говорят
    /// из пустоты), реплика без ключа (нечего подставить), участник без
    /// карточки (некого показать), шаги после перехода (не проиграются
    /// никогда), а с Поправки №7.8 — выбор без наслідку и без ветви, проверка
    /// на несуществующий скіл, метка ветвления в никуда.
    /// </summary>
    public static class SceneValidator
    {
        /// <summary>Скілы, на которые может ссылаться проверка варианта выбора — тот же перечень, что <c>SkillKeys</c> (Core/Checks).</summary>
        private static readonly HashSet<string> KnownSkillIds = new HashSet<string>
        {
            SkillKeys.Ranged.Id, SkillKeys.Melee.Id, SkillKeys.Tactics.Id,
            SkillKeys.Lockpick.Id, SkillKeys.Mechanics.Id, SkillKeys.Survival.Id, SkillKeys.Medicine.Id,
            SkillKeys.Persuade.Id, SkillKeys.Intimidate.Id, SkillKeys.Trade.Id
        };

        public static List<string> Validate(Scene scene, IEnumerable<CharacterCard> cast)
        {
            var problems = new List<string>();
            if (scene == null) { problems.Add("сцены нет"); return problems; }
            if (string.IsNullOrEmpty(scene.Id)) problems.Add("у сцены нет id");
            if (scene.Steps.Count == 0) { problems.Add($"{scene.Id}: сцена пуста"); return problems; }

            var known = new List<string>();
            if (cast != null)
                foreach (var card in cast)
                    if (card != null && !string.IsNullOrEmpty(card.Id)) known.Add(card.Id);

            var labels = new HashSet<string>();
            foreach (var s in scene.Steps)
                if (!string.IsNullOrEmpty(s.Label)) labels.Add(s.Label);

            bool sawShot = false;
            int transitionAt = -1;

            // "Мёртвая зона" после перехода: обычная (линейная) сцена не
            // ветвится, и шаг после перехода в ней и правда не проиграется
            // никогда. С Поправкой №7.8 сцена может ветвиться на метку
            // (SceneChoiceOption.NextLabel) — шаг С МЕТКОЙ начинает новую,
            // достижимую через прыжок ветвь и выводит из мёртвой зоны; в неё
            // возвращает уже ЕЁ собственный переход.
            bool deadZone = false;

            // Кто сейчас в кадре: говорить может тот, кого видно. Голос за
            // кадром — приём законный, но он объявляется пустым планом, а не
            // получается сам собой из забытого плана.
            string inFrameA = null, inFrameB = null;
            bool frameIsEmpty = false;

            for (int i = 0; i < scene.Steps.Count; i++)
            {
                var step = scene.Steps[i];
                string where = $"{scene.Id}, шаг {i + 1}";

                if (!string.IsNullOrEmpty(step.Label)) deadZone = false;
                if (deadZone)
                    problems.Add($"{where}: шаг после перехода — он не проиграется никогда");

                switch (step.Kind)
                {
                    case SceneStepKind.Shot:
                        sawShot = true;
                        inFrameA = step.ActorId;
                        inFrameB = step.SecondActorId;
                        frameIsEmpty = step.Framing == ShotFraming.Empty;
                        if (step.Framing == ShotFraming.None)
                            problems.Add($"{where}: план без раскадровки");
                        if (step.Framing == ShotFraming.Two && string.IsNullOrEmpty(step.SecondActorId))
                            problems.Add($"{where}: двойной план, а второго участника нет");
                        if (step.Framing != ShotFraming.Empty && string.IsNullOrEmpty(step.ActorId))
                            problems.Add($"{where}: в плане никого нет, хотя он не пустой");
                        break;

                    case SceneStepKind.Line:
                        // Реплика до первого плана — голос из ниоткуда: игрок не
                        // знает, кто говорит, а интерпретатору нечего показать.
                        if (!sawShot) problems.Add($"{where}: реплика раньше первого плана");
                        if (string.IsNullOrEmpty(step.ActorId)) problems.Add($"{where}: реплика без говорящего");
                        if (string.IsNullOrEmpty(step.Key)) problems.Add($"{where}: реплика без ключа текста");
                        if (sawShot && !frameIsEmpty && !string.IsNullOrEmpty(step.ActorId)
                            && step.ActorId != inFrameA && step.ActorId != inFrameB)
                            problems.Add($"{where}: говорит «{step.ActorId}», но в кадре не он — " +
                                         "поставь план или объяви голос за кадром пустым планом");
                        break;

                    case SceneStepKind.Effect:
                        if (string.IsNullOrEmpty(step.Key)) problems.Add($"{where}: эффект без ключа");
                        break;

                    case SceneStepKind.Beat:
                        if (step.Seconds <= 0) problems.Add($"{where}: пауза нулевой длины");
                        break;

                    case SceneStepKind.Transition:
                        if (string.IsNullOrEmpty(step.Key)) problems.Add($"{where}: переход без ключа");
                        transitionAt = i;
                        deadZone = true;
                        break;

                    case SceneStepKind.Choice:
                        ValidateChoice(step, where, labels, problems);
                        break;
                }
            }

            // Обычно сцена кончается шагом-переходом; но если в ней есть хоть
            // один выбор, каждый вариант которого либо сам завершает сцену
            // (TransitionKey), либо ветвится на метку (проверено поштучно в
            // ValidateChoice), переход может не быть отдельным шагом вовсе —
            // сцена кончается ВНУТРИ выбора (Поправка №7.8).
            bool hasChoice = false;
            for (int i = 0; i < scene.Steps.Count; i++)
                if (scene.Steps[i].Kind == SceneStepKind.Choice) { hasChoice = true; break; }
            if (transitionAt < 0 && !hasChoice) problems.Add($"{scene.Id}: сцена не кончается переходом");

            // У каждого участника обязана быть карточка: безымянных в игре нет
            // вовсе (Поправка №5.2), и портрет подбирается именно по карточке.
            foreach (var id in scene.Participants())
                if (!known.Contains(id))
                    problems.Add($"{scene.Id}: участник «{id}» без карточки персонажа");

            return problems;
        }

        public static bool IsValid(Scene scene, IEnumerable<CharacterCard> cast, out string problem)
        {
            var problems = Validate(scene, cast);
            problem = problems.Count == 0 ? null : string.Join("; ", problems);
            return problems.Count == 0;
        }

        /// <summary>
        /// Выбор реплики (Поправка №7.8): 2-4 варианта, у каждого — ключ
        /// текста, куда-то ведёт (переход или существующая метка) и несёт
        /// наслідок (без наслідку и без ветвления вариант ничего не меняет —
        /// это тот же дефект, что «выбор без видимого наслідку», §7.4), а
        /// проверка (если есть) ссылается на реальный скіл.
        /// </summary>
        private static void ValidateChoice(SceneStep step, string where, HashSet<string> labels, List<string> problems)
        {
            int count = step.Options != null ? step.Options.Count : 0;
            if (count < 2 || count > 4)
            {
                problems.Add($"{where}: вибір мусить мати 2-4 варіанти (маємо {count})");
                return;
            }

            for (int oi = 0; oi < step.Options.Count; oi++)
            {
                var opt = step.Options[oi];
                string owhere = $"{where}, варіант {oi + 1}";

                if (opt == null) { problems.Add($"{owhere}: порожній варіант"); continue; }
                if (string.IsNullOrEmpty(opt.TextKey)) problems.Add($"{owhere}: без ключа тексту");

                if (opt.HasCheck && !KnownSkillIds.Contains(opt.CheckSkill.Id))
                    problems.Add($"{owhere}: перевірка посилається на невідомий скіл «{opt.CheckSkill}»");
                if (opt.HasCheck && (opt.ConsequenceByBand == null || opt.ConsequenceByBand.Length != 4))
                    problems.Add($"{owhere}: ConsequenceByBand мусить мати рівно 4 елементи");

                bool hasBranch = !string.IsNullOrEmpty(opt.TransitionKey) || !string.IsNullOrEmpty(opt.NextLabel);
                if (!hasBranch) problems.Add($"{owhere}: не веде нікуди — ні переходом, ні міткою");
                if (!string.IsNullOrEmpty(opt.NextLabel) && !labels.Contains(opt.NextLabel))
                    problems.Add($"{owhere}: мітка «{opt.NextLabel}» не знайдена в сцені");

                bool hasConsequence = opt.HasCheck
                    ? HasAnyConsequence(opt.ConsequenceByBand)
                    : (opt.Consequence != null && !opt.Consequence.IsEmpty);
                bool branchesElsewhere = !string.IsNullOrEmpty(opt.NextLabel);
                if (!hasConsequence && !branchesElsewhere)
                    problems.Add($"{owhere}: немає наслідку і не веде на окрему мітку — вибір без ефекту (Поправка №7.4)");
            }
        }

        private static bool HasAnyConsequence(QuestConsequence[] byBand)
        {
            if (byBand == null) return false;
            for (int i = 0; i < byBand.Length; i++)
                if (byBand[i] != null && !byBand[i].IsEmpty) return true;
            return false;
        }
    }
}
