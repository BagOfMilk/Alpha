using System.IO;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    /// <summary>
    /// Заглушка UnityEngine.Application для headless-прогона.
    ///
    /// Зачем: архитектурные тесты грепают исходники на диске и берут корень
    /// проекта из Application.dataPath. Вне Unity этого типа не существует.
    /// Вместо правки самих тестов подставляем совместимую заглушку — тогда
    /// один и тот же файл теста работает и в редакторе, и headless.
    ///
    /// Файл намеренно лежит ВНЕ Assets/, поэтому Unity его не компилирует
    /// и конфликта имён с настоящим UnityEngine.Application не возникает.
    /// </summary>
    internal static class Application
    {
        /// <summary>Абсолютный путь к папке Assets, как его отдаёт Unity.</summary>
        public static string dataPath => Path.Combine(RepoRoot, "Assets");

        /// <summary>
        /// Корень репозитория. Берётся из пути к ЭТОМУ файлу, который компилятор
        /// зашивает на этапе сборки: результат не зависит ни от рабочей
        /// директории, ни от того, куда сложены бинарники.
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
