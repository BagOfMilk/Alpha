using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Колір команди на екіпіровці борця: маска текстури (R — металевість, G — AO, B — зона екіпіровки; tools/blender/ring_wrestler.py) показує, де штани й
    /// захист; на цих пікселях міняється відтінок, а яскравість і насиченість лишаються (чорні чоботи не чіпаємо).
    /// Читання текстур — через RenderTexture, тож налаштування імпорту (isReadable) не міняємо.
    /// </summary>
    public static class RingTint
    {
        public static Texture2D TeamTint(Texture2D albedo, Texture2D mask, float hue)
        {
            return Grade(albedo, mask, hue, 1.6f, Color.white);
        }

        /// <summary>
        /// Колір команди на одязі (синій канал маски — екіпіровка; зелений — AO, не шкіра) і підбір відтінку шкіри множником
        /// <paramref name="skin"/> (лише пікселі маски шкіри, м'яко за значенням зеленого каналу). Чорні чоботи й захист
        /// (низька насиченість) не чіпаємо; яскравість одягу — множник <paramref name="vMul"/>.
        /// </summary>
        public static Texture2D Grade(Texture2D albedo, Texture2D mask, float hue, float vMul, Color skin)
        {
            try
            {
                var a = ReadBack(albedo, RenderTextureReadWrite.sRGB, true);
                var m = ReadBack(mask, RenderTextureReadWrite.Linear, false);
                if (a == null || m == null || a.width != m.width || a.height != m.height) return null;
                Color32[] ca = a.GetPixels32();
                Color32[] cm = m.GetPixels32();
                // Середня шкіра (піксель шкіри маски й не одяг): плями текстури (світлі «латки» там, де маска чорна) тягнемо до неї.
                float mr = 0f, mg = 0f, mb = 0f; int mn = 0;
                for (int i = 0; i < ca.Length; i += 3)
                {
                    // Шкіра — усе поза екіпіровкою (синій канал маски низький); зелений — AO, за ним шкіру не шукаємо.
                    if (cm[i].b < 60) { mr += ca[i].r; mg += ca[i].g; mb += ca[i].b; mn++; }
                }
                Color mean = mn > 0 ? new Color(mr / mn / 255f, mg / mn / 255f, mb / mn / 255f) : new Color(0.9f, 0.78f, 0.68f);
                float meanL = mean.r * 0.3f + mean.g * 0.59f + mean.b * 0.11f;
                for (int i = 0; i < ca.Length; i++)
                {
                    float h, s, v;
                    Color c = ca[i];
                    Color.RGBToHSV(c, out h, out s, out v);
                    bool cloth = cm[i].b >= 140 && cm[i].g <= 110 && s >= 0.25f && v < 0.62f;   // штани й захист: темні й насичені
                    if (cloth)
                    {
                        ca[i] = Color.HSVToRGB(hue, Mathf.Min(1f, s * 1.1f), Mathf.Min(1f, v * vMul));
                        continue;
                    }
                    if (cm[i].b >= 140) continue;                                               // чоботи, наколінники, стрічка: як у текстурі
                    // Шкіра: світліші за середню латки зливаються з нею, далі - підбір відтінку.
                    float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                    // Латки текстури шкіри (MakeHuman) світліші за середню на 5–15 %; під світлом рингу вони ставали білими плямами.
                    float k = Mathf.Clamp01((l - meanL * 0.98f) / (meanL * 0.06f));
                    if (k > 0f && s < 0.45f) c = Color.Lerp(c, mean, k);
                    c = new Color(c.r * skin.r, c.g * skin.g, c.b * skin.b, 1f);
                    ca[i] = c;
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
