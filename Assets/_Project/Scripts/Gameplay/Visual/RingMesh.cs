using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay.Visual
{
    /// <summary>
    /// Плоске кільце (анулюс) у площині XZ — мітка сторони під бійцем і дозору (власник 08.10.2026: «зроби тонкі
    /// кільця замість дисків»). Сітка одна на кожну пару радіусів, кешується; лицем угору, без товщини.
    /// </summary>
    public static class RingMesh
    {
        private static readonly Dictionary<long, Mesh> Cache = new Dictionary<long, Mesh>();

        public static Mesh Get(float inner, float outer, int segments = 48)
        {
            long key = ((long)Mathf.RoundToInt(inner * 1000f) << 32) ^ ((long)Mathf.RoundToInt(outer * 1000f) << 8) ^ segments;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var vertices = new Vector3[segments * 2];
            var normals = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                vertices[i * 2] = new Vector3(c * inner, 0f, s * inner);
                vertices[i * 2 + 1] = new Vector3(c * outer, 0f, s * outer);
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;

                int n = (i + 1) % segments;
                int t = i * 6;
                // Обхід за годинниковою стрілкою, якщо дивитись згори, — лицем угору в Unity.
                triangles[t] = i * 2;
                triangles[t + 1] = n * 2;
                triangles[t + 2] = i * 2 + 1;
                triangles[t + 3] = i * 2 + 1;
                triangles[t + 4] = n * 2;
                triangles[t + 5] = n * 2 + 1;
            }

            var mesh = new Mesh { name = "ring_" + inner.ToString("0.00") + "_" + outer.ToString("0.00") };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Дочірній об'єкт-кільце з цим матеріалом і кольором (через блок властивостей), без тіні.</summary>
        public static GameObject Spawn(string name, Transform parent, float y, float inner, float outer, Material material, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = Get(inner, outer);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (material != null)
            {
                renderer.sharedMaterial = material;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(block);
            }
            return go;
        }
    }
}
