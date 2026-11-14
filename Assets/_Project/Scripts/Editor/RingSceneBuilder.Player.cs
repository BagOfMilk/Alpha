using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Gameplay.EditorTools
{
    /// <summary>
    /// Збірка Ring.exe (зона «Відео огляд», docs/ring/ZONES.md): окремий файл від сцени (RingSceneBuilder.cs — «Изучение»),
    /// щоб правки збірки й сцени не перемішувались. Імена методів для <c>-executeMethod</c> ті самі.
    /// </summary>
    public static partial class RingSceneBuilder
    {
        /// <summary>
        /// Окремий білд зрізу: лише сцена RingFeud у Build/Ring/Ring.exe (Build Settings не чіпаємо — сцена передається
        /// прямо). Для огляду вигляду: <c>Ring.exe -ring-shots &lt;тека&gt;</c> проходить ворожнечу ботом і знімає екрани.
        /// Із консолі: -executeMethod Game.Gameplay.EditorTools.RingSceneBuilder.BuildPlayer
        ///
        /// Без Development (власник, 08.11.2026: «Зроби так шоб гра більше не ходила в інтернет»): Development-збірка при
        /// старті відкриває мережевий порт підключення профайлера (player connection, 192.168.x.x:55000, режим Listen у
        /// boot.config) — Windows питала дозвіл на мережу, а в кадрі стояв напис «Development Build». Код гри в мережу не
        /// ходить, сервіси Unity (аналітика, звіти про збої) вимкнені в UnityConnectSettings. Профайлер — <see cref="BuildPlayerDev"/>.
        /// </summary>
        public static void BuildPlayer() { BuildPlayerTo("Ring", false); }

        /// <summary>
        /// Збірка з профайлером і налагоджувачем (Development) — лише коли потрібен Unity Profiler; вона відкриває мережевий
        /// порт. Build/RingDev/Ring.exe. Для огляду й тестів — <see cref="BuildPlayer"/> або Play Mode редактора (RingInEditor).
        /// </summary>
        public static void BuildPlayerDev() { BuildPlayerTo("RingDev", true); }

        /// <summary>Збірка для показу власнику — та сама, що <see cref="BuildPlayer"/>, у Build/RingShow/Ring.exe (лишається для старих команд).</summary>
        public static void BuildPlayerShow() { BuildPlayerTo("RingShow", false); }

        /// <summary>
        /// Ідентичність застосунку (ТЗ B00 §3): назва «Ринг», значок, без заставки Unity — лише на час збірки Ring.exe й
        /// повертається у finally. Unity-проєкт спільний з архівною Alpha (Alpha.exe з того самого ProjectSettings), тож
        /// ProjectSettings.asset не змінюється. companyName лишається, доки власник не вирішить. Сейви зі старої теки
        /// …/DefaultCompany/Alpha переносить гра при першому запуску (RingGameBootstrap.MigrateSave).
        /// </summary>
        private static void BuildPlayerTo(string folder, bool development)
        {
            Build();
            RingFontAssets.Ensure();
            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Build", folder);
            Directory.CreateDirectory(outDir);
            string oldName = PlayerSettings.productName;
            bool oldSplash = PlayerSettings.SplashScreen.show;
            bool oldTiming = PlayerSettings.enableFrameTimingStats;
            var oldIcons = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            bool ok = false;
            try
            {
                PlayerSettings.productName = "Ринг";
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Ring/Brand/app_icon_1024.png");
                if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
                else Debug.LogWarning("[Ринг] значок застосунку не знайдено: Assets/Art/Ring/Brand/app_icon_1024.png");
                // Заставку вимикаємо, лише якщо ліцензія дозволяє (Unity повертає show = true, якщо ні).
                PlayerSettings.SplashScreen.show = false;
                // Час кадру CPU/GPU для зонда FPS (-ring-fps, RingFpsProbe) — правдивий і з вікном за краєм екрана.
                PlayerSettings.enableFrameTimingStats = true;
                Debug.Log("[Ринг] заставка Unity: " + (PlayerSettings.SplashScreen.show ? "лишилась (ліцензія не дозволяє)" : "вимкнена"));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = Path.Combine(outDir, "Ring.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = development ? BuildOptions.Development : BuildOptions.None
                });
                ok = report.summary.result == BuildResult.Succeeded;
                Debug.Log("[Ринг] Білд: " + report.summary.result + ", помилок " + report.summary.totalErrors);
            }
            finally
            {
                PlayerSettings.productName = oldName;
                PlayerSettings.SplashScreen.show = oldSplash;
                PlayerSettings.enableFrameTimingStats = oldTiming;
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, oldIcons, IconKind.Any);
                AssetDatabase.SaveAssets();
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
