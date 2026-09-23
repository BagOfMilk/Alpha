using System.Collections.Generic;
using Game.Core.Characters;

namespace Game.Core.Scenes
{
    /// <summary>
    /// Проверка сценария БЕЗ редактора (Поправка №5.8) — ради этого сцена и
    /// сделана данными.
    ///
    /// Проверяется то, что ломает постановку молча: сцена без плана (говорят
    /// из пустоты), реплика без ключа (нечего подставить), участник без
    /// карточки (некого показать), шаги после перехода (не проиграются
    /// никогда).
    /// </summary>
    public static class SceneValidator
    {
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

            bool sawShot = false;
            int transitionAt = -1;

            // Кто сейчас в кадре: говорить может тот, кого видно. Голос за
            // кадром — приём законный, но он объявляется пустым планом, а не
            // получается сам собой из забытого плана.
            string inFrameA = null, inFrameB = null;
            bool frameIsEmpty = false;

            for (int i = 0; i < scene.Steps.Count; i++)
            {
                var step = scene.Steps[i];
                string where = $"{scene.Id}, шаг {i + 1}";

                if (transitionAt >= 0)
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
                        break;
                }
            }

            if (transitionAt < 0) problems.Add($"{scene.Id}: сцена не кончается переходом");

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
    }
}
