#nullable enable
using System;
using System.Collections.Generic;
using Kesa.Engine.Ui;
using UnityEngine;

namespace Kesa.Engine.Unity
{
    /// <summary>
    /// 카메라 리그 하나가 View 하나를 Unity <see cref="Camera"/>에 건 것 (G440·G442). 지금 볼 자리·확대는 이 물체의 상태이고 View는 처음 값과 한계다.
    /// 끌어 옮기기(<see cref="Pan"/>)와 확대(<see cref="Zoom"/>)는 게임이 입력을 읽어 부른다.
    /// </summary>
    public abstract class BoardCamera
    {
        protected BoardCamera(Camera camera, Transform board, ViewDeclaration view)
        {
            Camera = camera;
            Board = board;
            View = view;
            Target = Vector(view.Target);
            Unit = GameObjectLookRenderer.Real(view.Unit);
            Up = Vector(view.Frame.Up);
            North = Vector(view.Frame.North);
        }

        public Camera Camera { get; }
        public Transform Board { get; }
        public ViewDeclaration View { get; }

        /// <summary>지금 보는 곳(판 기준).</summary>
        public Vector3 Target { get; private set; }

        /// <summary>공간 한 칸의 길이.</summary>
        protected float Unit { get; }

        protected Vector3 Up { get; }
        protected Vector3 North { get; }

        /// <summary>판에서 화면 <paramref name="from"/> 아래 있던 곳이 <paramref name="to"/> 아래로 오게 옮긴다. 바닥은 볼 자리를 지나는 면이다.</summary>
        public void Pan(Vector2 from, Vector2 to)
        {
            var floor = new Plane(Board.TransformDirection(Up), Board.TransformPoint(Target));
            if (!Hit(floor, from, out var a) || !Hit(floor, to, out var b)) return;
            Target += Board.InverseTransformVector(a - b);
            Place();
        }

        /// <summary>휠 눈금만큼 다가간다(음수면 물러남). View에 <c>zoom</c>이 없으면 하지 않는다.</summary>
        public abstract void Zoom(float steps);

        /// <summary>카메라를 지금 상태로 놓는다.</summary>
        public abstract void Place();

        /// <summary>
        /// 그 자리의 틀에서 내려다보는 방향 (G442·G443): <paramref name="tilt"/> 0이 바로 위, 90이 수평으로 북을 봄,
        /// <paramref name="turn"/>은 위에서 보아 시계 방향(그림 요소의 <c>turn</c>과 같은 돌림).
        /// </summary>
        protected Quaternion Facing(float tilt, float turn) =>
            Board.rotation * Quaternion.LookRotation(North, Up) * Quaternion.AngleAxis(turn, Vector3.up) * Quaternion.Euler(90f - tilt, 0f, 0f);

        /// <summary>View의 <c>zoom</c>("최소,최대", 칸 단위). 없으면 거짓.</summary>
        protected bool ZoomRange(out float least, out float most)
        {
            least = most = 0;
            if (!View.Settings.TryGetValue("zoom", out var zoom)) return false;
            least = GameObjectLookRenderer.Real(zoom.Numbers[0]) * Unit;
            most = GameObjectLookRenderer.Real(zoom.Numbers[1]) * Unit;
            return true;
        }

        protected float Number(string name, float otherwise) =>
            View.Settings.TryGetValue(name, out var value) ? GameObjectLookRenderer.Real(value.Numbers[0]) : otherwise;

        private bool Hit(Plane floor, Vector2 screen, out Vector3 point)
        {
            var ray = Camera.ScreenPointToRay(screen);
            point = default;
            if (!floor.Raycast(ray, out float distance)) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        private static Vector3 Vector(LookPosition p) =>
            new Vector3(GameObjectLookRenderer.Real(p.X), GameObjectLookRenderer.Real(p.Y), GameObjectLookRenderer.Real(p.Z));
    }

    /// <summary>위에서 정사영 (G442 <c>top</c>): <c>size</c>는 화면 반 높이(칸).</summary>
    public sealed class TopCamera : BoardCamera
    {
        // 정사영이라 거리는 그림에 들지 않고, 판 위로 솟은 것이 잘리지 않을 만큼만 띄운다
        private const float Height = 100f;

        private float _size;

        public TopCamera(Camera camera, Transform board, ViewDeclaration view) : base(camera, board, view)
        {
            _size = Number("size", 1f) * Unit;
        }

        public override void Zoom(float steps)
        {
            if (!ZoomRange(out float least, out float most)) return;
            _size = Mathf.Clamp(_size * Mathf.Pow(0.9f, steps), least, most);
            Place();
        }

        public override void Place()
        {
            var facing = Facing(0f, 0f);
            Camera.orthographic = true;
            Camera.orthographicSize = _size;
            Camera.transform.SetPositionAndRotation(Board.TransformPoint(Target) - facing * Vector3.forward * Height * Unit, facing);
        }
    }

    /// <summary>기울인 원근 (G442 <c>tilted</c>): <c>tilt</c>·<c>turn</c>(도), <c>distance</c>(칸), <c>fov</c>(도, 기본 40).</summary>
    public sealed class TiltedCamera : BoardCamera
    {
        private float _distance;

        public TiltedCamera(Camera camera, Transform board, ViewDeclaration view) : base(camera, board, view)
        {
            _distance = Number("distance", 10f) * Unit;
        }

        public override void Zoom(float steps)
        {
            if (!ZoomRange(out float least, out float most)) return;
            _distance = Mathf.Clamp(_distance * Mathf.Pow(0.9f, steps), least, most);
            Place();
        }

        public override void Place()
        {
            var facing = Facing(Number("tilt", 0f), Number("turn", 0f));
            Camera.orthographic = false;
            Camera.fieldOfView = Number("fov", 40f);
            Camera.transform.SetPositionAndRotation(Board.TransformPoint(Target) - facing * Vector3.forward * _distance, facing);
        }
    }

    /// <summary>
    /// 리그 이름 → 그 리그를 움직이는 C# (G440). 속성 규칙은 <see cref="LookKinds"/>에 <see cref="ViewRig"/>로 등록하고, 게임은 둘 다 더한다.
    /// </summary>
    public sealed class BoardCameras
    {
        private readonly SortedDictionary<string, Func<Camera, Transform, ViewDeclaration, BoardCamera>> _rigs =
            new SortedDictionary<string, Func<Camera, Transform, ViewDeclaration, BoardCamera>>(StringComparer.Ordinal);

        /// <summary><c>top</c>·<c>tilted</c>를 넣은 것.</summary>
        public static BoardCameras Standard()
        {
            var cameras = new BoardCameras();
            cameras.Add("top", (c, b, v) => new TopCamera(c, b, v));
            cameras.Add("tilted", (c, b, v) => new TiltedCamera(c, b, v));
            return cameras;
        }

        public void Add(string rig, Func<Camera, Transform, ViewDeclaration, BoardCamera> make) => _rigs.Add(rig, make);

        /// <summary>View를 카메라에 걸고 놓는다. 그 리그를 움직일 C#이 없으면 null.</summary>
        public BoardCamera? Start(Camera camera, Transform board, ViewDeclaration view)
        {
            if (!_rigs.TryGetValue(view.Rig, out var make)) return null;
            var rig = make(camera, board, view);
            rig.Place();
            return rig;
        }
    }
}
