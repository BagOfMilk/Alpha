using System.IO;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    /// <summary>
    /// Заглушка UnityEngine.Application для headless-прогону.
    ///
    /// Навіщо: архітектурні тести грепають вихідники на диску і беруть корінь
    /// проєкту з Application.dataPath. Поза Unity цього типу не існує.
    /// Замість правки самих тестів підставляємо сумісну заглушку — тоді
    /// один і той самий файл тесту працює і в редакторі, і headless.
    ///
    /// Файл навмисно лежить ПОЗА Assets/, тому Unity його не компілює
    /// і конфлікту імен зі справжнім UnityEngine.Application не виникає.
    /// </summary>
    internal static class Application
    {
        /// <summary>Абсолютний шлях до папки Assets, як його віддає Unity.</summary>
        public static string dataPath => Path.Combine(RepoRoot, "Assets");

        /// <summary>
        /// Корінь репозиторію. Береться зі шляху до ЦЬОГО файлу, який компілятор
        /// зашиває на етапі збірки: результат не залежить ні від робочої
        /// директорії, ні від того, куди складені бінарники.
        /// </summary>
        private static string RepoRoot
        {
            get
            {
                string thisFile = ThisFilePath();
                // <repo>/tools/Game.Tests.Headless/UnityShim.cs → <repo>
                string toolsProject = Path.GetDirectoryName(thisFile);
                string tools = Path.GetDirectoryName(toolsProject);
                return Path.GetFullPath(Path.GetDirectoryName(tools));
            }
        }

        private static string ThisFilePath([CallerFilePath] string path = null) => path;
    }
}
