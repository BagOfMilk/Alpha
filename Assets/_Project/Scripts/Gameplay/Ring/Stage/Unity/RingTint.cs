using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Колір команди на екіпіровці борця: маска текстури (синій канал — одяг, зелений — шкіра) показує, де штани й
    /// захист; на цих пікселях міняється відтінок, а яскравість і насиченість лишаються (чорні чоботи не чіпаємо).
    /// Читання текстур — через RenderTexture, тож налаштування імпорту (isReadable) не міняємо.
    /// </summary>
    public static class RingTint
    {
        public static Texture2D TeamTint(Texture2D albedo, Texture2D mask, float hue)
        {
            try
            {
                var a = ReadBack(albedo, RenderTextureReadWrite.sRGB, true);
                var m = ReadBack(mask, RenderTextureReadWrite.Linear, false);
                if (a == null || m == null || a.width != m.width || a.height != m.height) return null;
                Color32[] ca = a.GetPixels32();
                Color32[] cm = m.GetPixels32();
                for (int i = 0; i < ca.Length; i++)
                {
                    if (cm[i].b < 140 || cm[i].g > 110) continue;
                    float h, s, v;
                    Color.RGBToHSV(ca[i], out h, out s, out v);
                    if (s < 0.25f) continue;
                    ca[i] = Color.HSVToRGB(hue, Mathf.Min(1f, s * 1.1f), Mathf.Min(1f, v * 1.6f));
                }
                a.SetPixels32(ca);
                a.Apply(true, true);
                Object.Destroy(m);
                return a;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Ринг] 3D: фарбування екіпіровки не вдалось: " + ex.Message);
                return null;
            }
        }

        private static Texture2D ReadBack(Texture2D src, RenderTextureReadWrite rw, bool mips)
        {
            if (src == null) return null;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, rw);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, mips, rw == RenderTextureReadWrite.Linear);
            t.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            t.Apply(mips, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return t;
        }
    }
}
