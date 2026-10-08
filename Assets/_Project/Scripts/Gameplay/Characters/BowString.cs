using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay.Characters
{
    /// <summary>
    /// Тятива лука: пряма між кінцями плечей, а в натягу — через праву долоню, з накладеною стрілою (власник
    /// 08.10.2026: «лук стріляє кліпом пістоля… так не повинно буть»). У сітці лука тятиви немає — жорстка сітка не
    /// гнеться за рукою. Натяг — поки грає постріл з лука до миті випуску (<see cref="AnimStateTable.ImpactAt"/>) або
    /// стійка дозору лучника і права долоня позаду тятиви.
    /// </summary>
    [RequireComponent(typeof(KitWeaponPoints))]
    public sealed class BowString : MonoBehaviour
    {
        private KitWeaponPoints _points;
        private LineRenderer _line;
        private FigureAnimation _anim;
        private GameObject _arrow;
        private static Material _material;

        private void Awake()
        {
            _points = GetComponent<KitWeaponPoints>();
            var go = new GameObject("bow_string");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.numCapVertices = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            if (_material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                _material = new Material(shader != null ? shader : Shader.Find("Standard"));
                var c = new Color(0.86f, 0.82f, 0.7f);
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", c);
                _material.color = c;
            }
            _line.sharedMaterial = _material;
        }

        private void LateUpdate()
        {
            if (_points == null || !_points.HasBow || _points.HandL == null) { _line.enabled = false; return; }
            if (_anim == null) _anim = GetComponent<FigureAnimation>();
            float scale = _points.HandL.lossyScale.x;
            _line.startWidth = _line.endWidth = 0.008f * scale;
            _line.enabled = true;

            var a = _points.HandL.TransformPoint(_points.BowTipA);
            var b = _points.HandL.TransformPoint(_points.BowTipB);
            var grip = _points.HandL.TransformPoint(_points.BowGrip);
            var mid = (a + b) * 0.5f;
            var back = (mid - grip).normalized;                       // від руків'я до лучника
            var palm = _points.HandR != null ? _points.HandR.TransformPoint(_points.PalmR) : mid;
            float behind = Vector3.Dot(palm - mid, back);
            bool drawing = Drawing() && behind > 0.01f * scale && (palm - mid).magnitude < 0.9f * scale;

            if (drawing)
            {
                _line.positionCount = 3;
                _line.SetPosition(0, a);
                _line.SetPosition(1, palm);
                _line.SetPosition(2, b);
                ShowArrow(palm, grip);
            }
            else
            {
                _line.positionCount = 2;
                _line.SetPosition(0, a);
                _line.SetPosition(1, b);
                if (_arrow != null) _arrow.SetActive(false);
            }
        }

        /// <summary>Чи тягне зараз лучник: постріл до випуску або стійка дозору.</summary>
        private bool Drawing()
        {
            if (_anim == null) return false;
            var shot = _anim.CurrentOneShot;
            if (shot != null)
                return AnimStateTable.NormalizeClipName(shot.name) == "Bow_Shoot" &&
                       _anim.OneShotTime < shot.length * AnimStateTable.ImpactAt(shot.name);
            return _anim.idle != null && AnimStateTable.NormalizeClipName(_anim.idle.name) == "Bow_Aim_Loop";
        }

        private void ShowArrow(Vector3 nock, Vector3 grip)
        {
            // Дочірня до моделі: довжина — у метрах моделі (масштаб постаті додає батько).
            if (_arrow == null)
            {
                _arrow = KitWeaponPoints.CreateArrow(transform, 0.82f);
                foreach (var t in _arrow.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            }
            _arrow.SetActive(true);
            var dir = grip - nock;
            if (dir.sqrMagnitude < 1e-6f) return;
            _arrow.transform.SetPositionAndRotation(nock, Quaternion.LookRotation(dir.normalized, Vector3.up));
        }
    }
}
