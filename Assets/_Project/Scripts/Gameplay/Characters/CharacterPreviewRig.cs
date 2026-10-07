using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay.Characters
{
    /// <summary>
    /// Живе 3D-прев'ю персонажа (екран створення героя, далі — «лялька» інвентаря; Поправка №19.3).
    /// Окремий станок далеко під світом (як у <c>PortraitRig</c>, але живий): своя камера рендерить
    /// модель у <see cref="Texture"/> щокадру, поки прев'ю ввімкнене; UI Toolkit показує цю текстуру.
    /// Модель перебудовується лише коли змінився підпис плану (<see cref="CharacterKitPlan.Signature"/>).
    /// </summary>
    public sealed class CharacterPreviewRig : MonoBehaviour
    {
        /// <summary>Шар станка: 30 зайнятий PortraitRig, 29 вільний.</summary>
        public const int StageLayer = 29;
        private static readonly Vector3 StagePosition = new Vector3(0f, -600f, 0f);

        public CharacterKitLibrary Library;
        public int TextureWidth = 640;
        public int TextureHeight = 960;

        private Transform _stage;
        private Camera _camera;
        private RenderTexture _texture;
        private GameObject _model;
        private string _signature;
        private Vector3 _baseFacing = Vector3.forward;
        // Світло станка: у URP без шарів світла (m_SupportsLightLayers: 0) cullingMask не гарантований,
        // тож направлене світло вмикається лише поки прев'ю видно — інакше підсвітило б село.
        private readonly System.Collections.Generic.List<GameObject> _lights = new System.Collections.Generic.List<GameObject>();

        /// <summary>Поворот моделі навколо вертикалі, градуси (тягнути мишею).</summary>
        public float Yaw { get; set; }

        public RenderTexture Texture
        {
            get { EnsureRig(); return _texture; }
        }

        public bool HasModel => _model != null;

        /// <summary>Вмикає/вимикає камеру станка (не рендерити, коли екрана не видно).</summary>
        public void SetActive(bool active)
        {
            EnsureRig();
            _camera.enabled = active;
            foreach (var l in _lights) l.SetActive(active);
            if (_model != null) _model.SetActive(active);
        }

        public void Show(CharacterKitPlan plan)
        {
            EnsureRig();
            if (plan == null) return;
            string sig = plan.Signature();
            if (sig == _signature && _model != null) return;
            if (_model != null) Destroy(_model);
            _model = CharacterAssembler.Build(Library, plan, _stage, StageLayer);
            _signature = sig;
            if (_model == null) return;
            _model.transform.localPosition = Vector3.zero;
            _model.transform.localRotation = Quaternion.identity;
            _baseFacing = CharacterAssembler.Facing(_model);
            Frame();

            // Не бінд-поза, а жива стійка: кліп Idle з бібліотеки набору (якщо є).
            var anims = GetComponent<CharacterAnimLibrary>();
            var idle = anims != null ? anims.For(CharacterAnimState.Idle, WeaponStyle.Unarmed) : null;
            if (idle != null)
            {
                var anim = _model.AddComponent<FigureAnimation>();
                anim.idle = idle;
                anim.enabled = false;
                anim.enabled = true;
            }
        }

        private void LateUpdate()
        {
            if (_model == null) return;
            // Обличчям до камери (камера дивиться в -Z станка), плюс поворот мишею.
            float face = Quaternion.FromToRotation(_baseFacing, Vector3.back).eulerAngles.y;
            _model.transform.localRotation = Quaternion.Euler(0f, face + Yaw, 0f);
        }

        private void Frame()
        {
            var bounds = new Bounds(_model.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in _model.GetComponentsInChildren<Renderer>())
            {
                if (!r.gameObject.activeInHierarchy) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            float height = any ? Mathf.Max(1.2f, bounds.size.y) : 1.8f;
            var center = any ? bounds.center : _stage.position + Vector3.up * 0.9f;
            float dist = height * 0.5f / Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.12f;
            _camera.transform.position = new Vector3(center.x, center.y + height * 0.02f, center.z + dist);
            _camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
        }

        private void EnsureRig()
        {
            if (_stage != null) return;
            var stage = new GameObject("CharacterPreviewStage");
            stage.transform.SetParent(transform, false);
            stage.transform.position = StagePosition;
            stage.layer = StageLayer;
            _stage = stage.transform;

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_stage, false);
            camGo.layer = StageLayer;
            _camera = camGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = 22f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 50f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.09f, 0.08f, 0.07f, 1f);
            _camera.cullingMask = 1 << StageLayer;
            _texture = new RenderTexture(TextureWidth, TextureHeight, 24) { name = "CharacterPreviewRT", antiAliasing = 4 };
            _camera.targetTexture = _texture;

            AddLight("PreviewKey", new Vector3(30f, 200f, 0f), 1.6f, new Color(1f, 0.95f, 0.86f));
            AddLight("PreviewFill", new Vector3(15f, 150f, 0f), 0.55f, new Color(0.75f, 0.82f, 1f));
            AddLight("PreviewRim", new Vector3(20f, 20f, 0f), 0.9f, new Color(1f, 0.9f, 0.75f));
        }

        private void AddLight(string name, Vector3 euler, float intensity, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_stage, false);
            go.transform.rotation = Quaternion.Euler(euler);
            go.layer = StageLayer;
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.cullingMask = 1 << StageLayer;
            light.shadows = LightShadows.None;
            go.SetActive(false);
            _lights.Add(go);
        }

        private void OnDestroy()
        {
            if (_texture != null) _texture.Release();
        }
    }
}
