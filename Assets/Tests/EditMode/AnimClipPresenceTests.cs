using System;
using System.IO;
using System.Text;
using Game.Gameplay.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Кожен кліп, який таблиця станів (<see cref="AnimStateTable"/>) просить для стану й стилю зброї, справді є у
    /// бібліотеках UAL (<c>Assets/Art/Animations</c>): інакше постать мовчки стоїть у бінд-позі. Імена кліпів —
    /// рядки у FBX, тож перевірка обходиться без Unity.
    /// </summary>
    public class AnimClipPresenceTests
    {
        [Test]
        public void EveryClipOfTheStateTable_ExistsInTheUalLibraries()
        {
            string dir = Path.Combine(Application.dataPath, "Art", "Animations");
            Assert.That(Directory.Exists(dir), Is.True, "немає " + dir);
            var text = new StringBuilder();
            foreach (var f in Directory.GetFiles(dir, "*.fbx"))
                text.Append(Encoding.ASCII.GetString(File.ReadAllBytes(f)));
            string all = text.ToString();
            foreach (CharacterAnimState st in Enum.GetValues(typeof(CharacterAnimState)))
                foreach (WeaponStyle style in Enum.GetValues(typeof(WeaponStyle)))
                {
                    string clip = AnimStateTable.For(st, style).Clip;
                    if (string.IsNullOrEmpty(clip)) continue;
                    Assert.That(all.Contains(clip), Is.True, st + "/" + style + ": кліпу «" + clip + "» немає в UAL");
                }
        }
    }
}
