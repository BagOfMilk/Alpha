using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Quests;

namespace Game.Core.Scenes
{
    /// <summary>
    /// Перевірка сценарію БЕЗ редактора (Поправка №5.8) — заради цього сцена і
    /// зроблена даними.
    ///
    /// Перевіряється те, що ламає постановку мовчки: сцена без плану (говорять
    /// із порожнечі), репліка без ключа (нема чого підставити), учасник без
    /// картки (нема кого показати), кроки після переходу (не програються
    /// ніколи), а з Поправки №7.8 — вибір без наслідку і без гілки, перевірка
    /// на неіснуючий скіл, мітка розгалуження в нікуди.
    /// </summary>
    public static class SceneValidator
    {
        /// <summary>Скіли, на які може посилатися перевірка варіанта вибору — той самий перелік, що <c>SkillKeys</c> (Core/Checks).</summary>
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

            // "Мертва зона" після переходу: звичайна (лінійна) сцена не
            // розгалужується, і крок після переходу в ній і справді не програється
            // ніколи. З Поправкою №7.8 сцена може розгалужуватися на мітку
            // (SceneChoiceOption.NextLabel) — крок З МІТКОЮ починає нову,
            // досяжну через стрибок гілку і виводить із мертвої зони; у неї
            // повертає вже ЇЇ власний перехід.
            bool deadZone = false;

            // Хто зараз у кадрі: говорити може той, кого видно. Голос за
            // кадром — прийом законний, але він оголошується порожнім планом, а не
            // виходить сам собою із забутого плану.
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
                        // Репліка до першого плану — голос з нізвідки: гравець не
                        // знає, хто говорить, а інтерпретатору нема чого показати.
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

            // Зазвичай сцена закінчується кроком-переходом; але якщо в ній є хоч
            // один вибір, кожен варіант якого або сам завершує сцену
            // (TransitionKey), або розгалужується на мітку (перевірено поштучно в
            // ValidateChoice), перехід може не бути окремим кроком узагалі —
            // сцена закінчується ВСЕРЕДИНІ вибору (Поправка №7.8).
            bool hasChoice = false;
            for (int i = 0; i < scene.Steps.Count; i++)
                if (scene.Steps[i].Kind == SceneStepKind.Choice) { hasChoice = true; break; }
            if (transitionAt < 0 && !hasChoice) problems.Add($"{scene.Id}: сцена не кончается переходом");

            // У кожного учасника зобов'язана бути картка: безіменних у грі нема
            // взагалі (Поправка №5.2), і портрет підбирається саме за карткою.
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
        /// Вибір репліки (Поправка №7.8): 2-4 варіанти, у кожного — ключ
        /// тексту, кудись веде (перехід або існуюча мітка) і несе
        /// наслідок (без наслідку і без розгалуження варіант нічого не змінює —
        /// це той самий дефект, що «вибір без видимого наслідку», §7.4), а
        /// перевірка (якщо є) посилається на реальний скіл.
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
