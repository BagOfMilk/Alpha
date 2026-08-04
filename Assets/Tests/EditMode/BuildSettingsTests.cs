using System;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Вход в игру (итерация 18): собранный билд обязан стартовать с Campaign, а не
    /// с отладочного Boot. Инвариант живёт в правимом руками
    /// ProjectSettings/EditorBuildSettings.asset, и PlayMode-смоуки его не стерегут —
    /// они грузят сцены по имени, то есть к порядку слепы.
    /// </summary>
    public class BuildSettingsTests
    {
        private const string Campaign = "Assets/_Project/Scenes/Campaign.unity";
        private const string Battle = "Assets/_Project/Scenes/Battle.unity";
        private const string Boot = "Assets/_Project/Scenes/Boot.unity";

        [Test]
        public void FirstEnabledScene_IsCampaign()
        {
            var scenes = EditorBuildSettings.scenes;
            string first = null;
            foreach (var s in scenes)
                if (s.enabled) { first = s.path; break; }

            Assert.AreEqual(Campaign, first,
                "билд стартует с первой ВКЛЮЧЁННОЙ сцены — это должна быть кампания");
        }

        [Test]
        public void RequiredScenes_AreInBuild_AndEnabled()
        {
            // PlayMode-смоуки грузят эти сцены по имени: без членства в билде
            // LoadSceneAsync молча провалится.
            foreach (var path in new[] { Campaign, Battle, Boot })
            {
                var entry = Array.Find(EditorBuildSettings.scenes, s => s.path == path);
                Assert.IsNotNull(entry, $"{path} обязана быть в Build Settings");
                Assert.IsTrue(entry.enabled, $"{path} должна быть включена");
            }
        }
    }
}
