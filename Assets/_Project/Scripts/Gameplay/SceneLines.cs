namespace Game.Gameplay
{
    /// <summary>
    /// Текст реплик по ключам — ПЛЕЙСХОЛДЕР до таблиц-ScriptableObject.
    ///
    /// Правило не меняется: ядро отдаёт ключ, текст подбирается здесь. Когда
    /// появится SO-таблица, этот класс уходит целиком, а сцены, ядро и
    /// консольная сборка не меняются ни на строку — они ключами и обмениваются.
    ///
    /// У консольной сборки (tools/Shared/SceneText.cs) своя такая же таблица:
    /// она до Unity не дотягивается. Обе временные и умрут вместе.
    /// </summary>
    public static class SceneLines
    {
        public static string Text(string key)
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
