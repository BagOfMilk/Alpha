using System.Collections.Generic;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Characters;
using Game.Gameplay.UI;
using Game.Gameplay.Walk;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Діалог у світі (власник, 08.10.2026: «зробити діалоги між персонажами як в baldursgayte 3»; «Камера в світі,
    /// як у BG3»): камера села переходить на крупні перспективні плани того, хто говорить, співрозмовники
    /// повертаються одне до одного, мовець жестикулює. ЩО знімати і ЗВІДКИ — <see cref="DialogueDirector"/>
    /// (чистий C#, тести); тут лише постаті сцени й камера.
    ///
    /// Камерою села керує <see cref="HeroWalker"/> в Update; цей режисер перезаписує позу в LateUpdate (з
    /// <see cref="GameShell"/>), тож правок у HeroWalker не потрібно, а після сцени камера повертається рівно туди,
    /// де була. Між планами — склейки, як у BG3; у межах плану — повільний наїзд.
    /// Хто не стоїть поруч у селі (Тугар, ті, хто ще на посту далеко) — тимчасова постать набору навпроти героя.
    /// Статут PERF-01: без DoF і без нових світел — на Низькій графіці працює так само.
    /// </summary>
    public sealed class DialogueStage : MonoBehaviour
    {
        /// <summary>Далі за цю відстань (одиниці світу) жителя не знімаємо там, де стоїть, — ставимо постать поруч.</summary>
        private const float NearEnough = 3f;
        private const float ShotPushSeconds = 7f;

        private GameShell _shell;
        private bool _active;
        private Camera _camera;

        // збережена камера
        private bool _savedOrtho;
        private float _savedSize, _savedFov, _savedNear;
        private Vector3 _savedPos;
        private Quaternion _savedRot;

        private HeroWalker _hero;
        private Quaternion _heroSavedRot;
        private string _anchorOtherId;

        private sealed class Actor
        {
            public Transform Root;
            public Transform Head;
            public KitFigure Figure;
            public bool Temporary;
            public Quaternion SavedRotation;
            public AnimationClip SavedIdle;
        }

        private readonly Dictionary<string, Actor> _actors = new Dictionary<string, Actor>();
        private string _shotSignature;
        private DialogueCameraPose _pose;
        private float _shotAge;
        private string _talkingId;

        public bool IsActive => _active;

        public void Init(GameShell shell) => _shell = shell;

        /// <summary>Раз на кадр (з LateUpdate оболонки): сцена йде — тримати кадр; ні — віддати камеру.</summary>
        public void Tick(SceneStepView current, string displaySpeakerId)
        {
            if (current == null || _shell == null || _shell.Session == null)
            {
                End();
                return;
            }
            if (!_active && !Begin()) return;

            var shot = DialogueDirector.Plan(current.Framing, current.ActorId, current.SecondActorId, current.SpeakerId,
                GameSession.ProtagonistId);
            if (shot != null && shot.Signature != _shotSignature) Cut(shot);

            SetTalking(displaySpeakerId);

            _shotAge += Time.unscaledDeltaTime;
            if (_camera == null) return;
            var pos = DialogueDirector.PushIn(_pose, _shotAge / ShotPushSeconds);
            _camera.orthographic = false;
            _camera.fieldOfView = _pose.Fov;
            _camera.nearClipPlane = 0.02f;
            _camera.transform.position = ToV(pos);
            _camera.transform.rotation = Quaternion.LookRotation(ToV(_pose.LookAt) - ToV(pos), Vector3.up);
        }

        private bool Begin()
        {
            _hero = FindAnyObjectByType<HeroWalker>();
            _camera = _hero != null && _hero.hubCamera != null ? _hero.hubCamera : Camera.main;
            if (_camera == null || !_camera.isActiveAndEnabled || _hero == null) return false;
            _savedOrtho = _camera.orthographic;
            _savedSize = _camera.orthographicSize;
            _savedFov = _camera.fieldOfView;
            _savedNear = _camera.nearClipPlane;
            _savedPos = _camera.transform.position;
            _savedRot = _camera.transform.rotation;
            _heroSavedRot = _hero.transform.rotation;
            _shotSignature = null;
            _anchorOtherId = null;
            _active = true;
            return true;
        }

        /// <summary>Сцена скінчилась: камера, постаті, пози — як були.</summary>
        public void End()
        {
            if (!_active) return;
            _active = false;
            SetTalking(null);
            foreach (var pair in _actors)
            {
                var a = pair.Value;
                if (a.Root == null) continue;
                if (a.Temporary) Destroy(a.Root.gameObject);
                else if (a.Root != (_hero != null ? _hero.transform : null)) a.Root.rotation = a.SavedRotation;
            }
            _actors.Clear();
            if (_hero != null) _hero.transform.rotation = _heroSavedRot;
            if (_camera != null)
            {
                _camera.orthographic = _savedOrtho;
                _camera.orthographicSize = _savedSize;
                _camera.fieldOfView = _savedFov;
                _camera.nearClipPlane = _savedNear;
                _camera.transform.position = _savedPos;
                _camera.transform.rotation = _savedRot;
            }
            _shotSignature = null;
        }

        private void OnDestroy() => End();

        // ============================ кадр ============================

        private void Cut(DialogueShot shot)
        {
            var subject = ActorFor(shot.SubjectId);
            if (subject == null) return; // нікого показати — лишаємо попередній план
            string otherId = shot.OtherId;
            if (string.IsNullOrEmpty(otherId) && shot.SubjectId == GameSession.ProtagonistId) otherId = _anchorOtherId;
            var other = !string.IsNullOrEmpty(otherId) ? ActorFor(otherId) : null;
            if (shot.SubjectId != GameSession.ProtagonistId && _anchorOtherId == null) _anchorOtherId = shot.SubjectId;
            if (!string.IsNullOrEmpty(shot.OtherId) && shot.OtherId != GameSession.ProtagonistId && _anchorOtherId == null) _anchorOtherId = shot.OtherId;

            var heroActor = ActorFor(GameSession.ProtagonistId);
            var anchorOther = _anchorOtherId != null ? ActorFor(_anchorOtherId) : null;

            Vector3 subjectHead = HeadOf(subject);
            Vector3 otherHead = other != null ? HeadOf(other) : subjectHead + subject.Root.forward * 0.6f;
            Vector3 axisFrom = heroActor != null ? HeadOf(heroActor) : subjectHead;
            Vector3 axisTo = anchorOther != null ? HeadOf(anchorOther) : axisFrom + (_hero != null ? _hero.transform.forward : Vector3.forward);

            // Співрозмовники дивляться одне на одного.
            if (other != null)
            {
                Face(subject, otherHead);
                Face(other, subjectHead);
            }

            float height = FigureHeight(subject);
            _pose = DialogueDirector.Pose(shot.Kind, ToD(subjectHead), ToD(otherHead), ToD(axisFrom), ToD(axisTo), height);
            _shotSignature = shot.Signature;
            _shotAge = 0f;
        }

        private void Face(Actor actor, Vector3 point)
        {
            if (actor?.Root == null) return;
            var dir = point - actor.Root.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
            // Корінь героя повернутий на modelYawOffset під старі фігурки (див. HeroWalker.Face) — та сама поправка.
            if (_hero != null && actor.Root == _hero.transform) look *= Quaternion.Euler(0f, _hero.modelYawOffset, 0f);
            actor.Root.rotation = look;
        }

        /// <summary>Жест мовцю (Talk), спокій решті; після сцени — те, що було.</summary>
        private void SetTalking(string speakerId)
        {
            if (speakerId == _talkingId) return;
            if (_talkingId != null && _actors.TryGetValue(_talkingId, out var was)) RestoreIdle(was);
            _talkingId = speakerId;
            if (speakerId == null || !_actors.TryGetValue(speakerId, out var now)) return;
            var anim = now.Figure != null ? now.Figure.Animation : null;
            if (anim == null) return;
            KitFigure.TryFindLibraries(out _, out var anims);
            var talk = anims != null && anims.IsComplete ? anims.For(CharacterAnimState.Talk, WeaponStyle.Unarmed) : null;
            if (talk == null) return;
            if (now.SavedIdle == null) now.SavedIdle = anim.idle;
            anim.SetIdleClip(talk);
        }

        private static void RestoreIdle(Actor actor)
        {
            var anim = actor?.Figure != null ? actor.Figure.Animation : null;
            if (anim != null && actor.SavedIdle != null) anim.SetIdleClip(actor.SavedIdle);
        }

        // ============================ хто де стоїть ============================

        private Actor ActorFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_actors.TryGetValue(id, out var known) && known.Root != null) return known;

            Actor actor = null;
            if (id == GameSession.ProtagonistId)
            {
                if (_hero == null) return null;
                actor = new Actor { Root = _hero.transform, Figure = _hero.GetComponentInChildren<KitFigure>(), SavedRotation = _hero.transform.rotation };
            }
            else
            {
                var holder = VillageHolderOf(id);
                if (holder != null && _hero != null && Vector3.Distance(holder.position, _hero.transform.position) <= NearEnough)
                    actor = new Actor { Root = holder, Figure = holder.GetComponent<KitFigure>(), SavedRotation = holder.rotation };
                else
                    actor = SpawnBeside(id);
            }
            if (actor == null) return null;
            actor.Head = FindHead(actor.Root);
            if (actor.SavedIdle == null && actor.Figure != null && actor.Figure.Animation != null) actor.SavedIdle = actor.Figure.Animation.idle;
            _actors[id] = actor;
            return actor;
        }

        /// <summary>
        /// Постать жителя в селі — та сама розстановка, що будує сцену (<see cref="VillagePeople.Arrange"/>): на посту —
        /// <c>villager:&lt;пост&gt;</c>, без поста — <c>idle:&lt;місце&gt;</c>. null — у селі його не видно.
        /// </summary>
        private Transform VillageHolderOf(string id)
        {
            var roster = _shell.Session.GetRosterView();
            if (roster?.Companions == null) return null;
            var posts = new Dictionary<string, WalkPoint>();
            foreach (var c in roster.Companions)
                if (!string.IsNullOrEmpty(c.AssignedSlotId) && Active("villager:" + c.AssignedSlotId) != null)
                    posts[c.AssignedSlotId] = default(WalkPoint);
            var idle = new List<WalkPoint>();
            for (int i = 0; i < VillagePeople.IdleSpotCount; i++) idle.Add(default(WalkPoint));
            foreach (var spot in VillagePeople.Arrange(roster, posts, idle))
            {
                if (spot.CompanionId != id) continue;
                if (spot.AtPost)
                {
                    foreach (var c in roster.Companions)
                        if (c.Id == id) return Active("villager:" + c.AssignedSlotId);
                    return null;
                }
                return Active("idle:" + spot.IdleIndex);
            }
            return null;
        }

        private static Transform Active(string name)
        {
            var go = GameObject.Find(name);
            return go != null && go.activeInHierarchy ? go.transform : null;
        }

        /// <summary>Тимчасова постать набору навпроти героя (на розмовній відстані), обличчям до нього.</summary>
        private Actor SpawnBeside(string id)
        {
            if (_hero == null) return null;
            var look = _shell.Session.GetAppearance(id);
            if (look == null) return null;
            int index = 0;
            foreach (var a in _actors.Values) if (a.Temporary) index++;
            var heroPos = _hero.transform.position;
            var forward = _hero.transform.rotation * Quaternion.Euler(0f, -_hero.modelYawOffset, 0f) * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            float spread = index == 0 ? 0f : (index % 2 == 1 ? 35f : -35f) * ((index + 1) / 2);
            var dir = Quaternion.Euler(0f, spread, 0f) * forward;
            var go = new GameObject("dialogue:" + id);
            go.layer = _hero.gameObject.layer;
            go.transform.position = heroPos + dir * 0.6f;
            go.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            var figure = go.AddComponent<KitFigure>();
            if (!figure.Show(look, new List<string>(), CharacterAnimState.Idle, 1f, 0.37f * (index + 1), go.layer))
            {
                Destroy(go);
                return null;
            }
            return new Actor { Root = go.transform, Figure = figure, Temporary = true, SavedRotation = go.transform.rotation };
        }

        private static Transform FindHead(Transform root)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name.Equals("head", System.StringComparison.OrdinalIgnoreCase)) return t;
            var animator = root.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman) return animator.GetBoneTransform(HumanBodyBones.Head);
            return null;
        }

        private static Vector3 HeadOf(Actor actor)
        {
            if (actor.Head != null) return actor.Head.position;
            return actor.Root.position + Vector3.up * (0.93f * FigureHeight(actor));
        }

        /// <summary>Зріст постаті в одиницях світу (масштаб сцени × людський зріст ≈ 1,75 м).</summary>
        private static float FigureHeight(Actor actor)
        {
            if (actor?.Head != null && actor.Root != null)
            {
                float h = (actor.Head.position.y - actor.Root.position.y) / 0.93f;
                if (h > 0.05f) return h;
            }
            return 1.75f * Game.Gameplay.Visual.ArtScale.World;
        }

        private static DialogueVec ToD(Vector3 v) => new DialogueVec(v.x, v.y, v.z);
        private static Vector3 ToV(DialogueVec v) => new Vector3(v.X, v.Y, v.Z);
    }
}
