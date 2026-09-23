using Game.Core.Characters;

namespace Game.Core.Scenes
{
    /// <summary>
    /// Сцены открытия «Перевал» (docs/FIRST_HOUR.md §2.2).
    ///
    /// Текста здесь нет — только ключи: реплики живут в таблицах, и правит их
    /// автор без программиста. Сцена проверяется SceneValidator до всякого
    /// редактора.
    /// </summary>
    public static class OpeningScenes
    {
        /// <summary>
        /// Сутки 1, утро — «Сосед с претензией».
        ///
        /// Тугар Вовк предлагает пропустить авангард орды через перевал за долю.
        /// Смысл сцены — телеграфия: игрок узнаёт антагониста ДО боя, и тот же
        /// человек будет стоять при командире орды в финале. Без этой сцены
        /// предательство в финале — сюрприз, а не расплата.
        /// </summary>
        public static Scene NeighbourWithADemand()
        {
            return new Scene("opening.neighbour", "scene.opening.neighbour.title")
                .Step(SceneStep.Shot(OpeningCast.TuharVovk().Id, ShotFraming.Close))
                .Step(SceneStep.Line("tuhar", "scene.neighbour.offer"))
                .Step(SceneStep.Shot("protagonist", ShotFraming.Close))
                .Step(SceneStep.Beat(1.5))
                .Step(SceneStep.Shot("tuhar", ShotFraming.Two, "protagonist"))
                .Step(SceneStep.Line("tuhar", "scene.neighbour.threat"))
                .Step(SceneStep.Shot("zakhar", ShotFraming.Close))
                .Step(SceneStep.Line("zakhar", "scene.neighbour.elder_refuses"))
                .Step(SceneStep.Effect("sfx.door.slam"))
                .Step(SceneStep.Transition("to.node1.pass"));
        }

        /// <summary>
        /// Развязка узла 1. Четыре исхода, ни один не гейм-овер: сцена одна,
        /// ключ реплики разный — состав ростера на сутки 2–5 решается здесь
        /// (чеклист §3 стр. 19).
        /// </summary>
        public static Scene PassResolution(string outcomeKey)
        {
            return new Scene("opening.pass_resolution", "scene.pass.title")
                .Step(SceneStep.Shot("protagonist", ShotFraming.Close))
                .Step(SceneStep.Line("protagonist", "scene.pass." + outcomeKey))
                .Step(SceneStep.Shot(null, ShotFraming.Empty))
                .Step(SceneStep.Beat(2.0))
                .Step(SceneStep.Transition("to.settlement.evening"));
        }

        /// <summary>Карточка протагониста: у участника сцены обязана быть карточка.</summary>
        public static CharacterCard Protagonist() => new CharacterCard(
            "protagonist", "Протагонист", SourceTier.Original, null,
            "тот, кого община пустила на порог");
    }
}
