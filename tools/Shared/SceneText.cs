using System;
using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Scenes;

namespace Alpha.Shared
{
    /// <summary>
    /// Печать портретной сцены текстом — ПЛЕЙСХОЛДЕРНЫЙ интерпретатор.
    ///
    /// Настоящий интерпретатор живёт в Game.Gameplay и рисует планы и портреты.
    /// Консоли он недоступен, но проверять постановку можно уже сейчас: ядро
    /// отдаёт ключи, здесь они превращаются в строки. Ровно тот же принцип, что
    /// у сигналов.
    /// </summary>
    public static class SceneText
    {
        public static void Play(Scene scene, IEnumerable<CharacterCard> cast, Action<string> write)
        {
            if (scene == null || write == null) return;

            var names = new Dictionary<string, string>();
            if (cast != null)
                foreach (var card in cast)
                    if (card != null && !string.IsNullOrEmpty(card.Id)) names[card.Id] = card.DisplayName;

            write("");
            write("  ── сцена: " + Line(scene.TitleKey) + " ──");

            for (int i = 0; i < scene.Steps.Count; i++)
            {
                var step = scene.Steps[i];
                switch (step.Kind)
                {
                    case SceneStepKind.Shot:
                        write("    [" + Framing(step.Framing) + "] " + Who(names, step.ActorId) +
                              (string.IsNullOrEmpty(step.SecondActorId) ? "" : " и " + Who(names, step.SecondActorId)));
                        break;

                    case SceneStepKind.Line:
                        write("    " + Who(names, step.ActorId) + ": " + Line(step.Key));
                        break;

                    case SceneStepKind.Beat:
                        write("    …");
                        break;

                    case SceneStepKind.Effect:
                        write("    ( " + Line(step.Key) + " )");
                        break;

                    case SceneStepKind.Transition:
                        write("  ── конец сцены ──");
                        break;
                }
            }
            write("");
        }

        private static string Who(Dictionary<string, string> names, string id)
        {
            if (string.IsNullOrEmpty(id)) return "пусто";
            return names.TryGetValue(id, out var name) ? name : id;
        }

        private static string Framing(ShotFraming framing)
        {
            switch (framing)
            {
                case ShotFraming.Close: return "крупный план";
                case ShotFraming.Two: return "двойной план";
                case ShotFraming.Empty: return "пустой план";
                default: return "план";
            }
        }

        /// <summary>Текст по ключу. Таблица плейсхолдерная — настоящая придёт из SO.</summary>
        private static string Line(string key)
        {
            switch (key)
            {
                case "scene.opening.neighbour.title": return "Сосед с претензией";
                case "scene.neighbour.offer":
                    return "«Пропусти их через перевал. Возьмут своё и уйдут. Тебе — доля».";
                case "scene.neighbour.threat":
                    return "«Не пустишь по-доброму — пройдут по-другому. И спросят уже с общины».";
                case "scene.neighbour.elder_refuses":
                    return "«Перевал не мой и не твой. Он общинный. Сход решит».";
                case "sfx.door.slam": return "хлопнула дверь";

                case "scene.pass.title": return "После перевала";
                case "scene.pass.best": return "«Склад цел. И все вернулись».";
                case "scene.pass.good": return "«Максим не встанет пару дней. Но склад цел».";
                case "scene.pass.base": return "«Она ушла за отцом. А склад вычистили до досок».";
                case "scene.pass.worst": return "«Максим ранен, её нет, склада нет. Община смотрит и молчит».";

                default: return key;
            }
        }
    }
}
