using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Game.Core.Base;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Каталог моделей треку V (Поправка №18, V4): кожна будівля, що має ділянку в сцені села, має власну
    /// модель у <c>Assets/Art/Models</c>, і генератор Blender будує їй п'ять стадій (вузли stage1..5).
    /// </summary>
    public class ArtCatalogTests
    {
        private static string Repo => Path.GetDirectoryName(Application.dataPath);

        [Test]
        public void EveryPlottedBuilding_HasAModel_WithFiveStages()
        {
            string builder = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", "Editor", "GameSceneBuilder.cs"));
            var plotted = Regex.Matches(builder, @"Plot\(group, Game\.Core\.Base\.DefaultBuildings\.(\w+)")
                .Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.GreaterOrEqual(plotted.Count, 10, "ділянок у сцені села");

            string kit = File.ReadAllText(Path.Combine(Repo, "tools", "blender", "alpha_kit.py"));
            foreach (var constant in plotted)
            {
                var field = typeof(DefaultBuildings).GetField(constant, BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(field, constant);
                string id = (string)field.GetValue(null);
                Assert.IsTrue(File.Exists(Path.Combine(Application.dataPath, "Art", "Models", id + ".fbx")), "немає моделі будівлі " + id);
                StringAssert.Contains("\"" + id + "\"", kit, "генератор не будує " + id);
            }
            StringAssert.Contains("stages = [_empty(f\"{name}.stage{k}\", root) for k in range(1, 6)]", kit, "п'ять стадій у генераторі");
        }
    }
}
