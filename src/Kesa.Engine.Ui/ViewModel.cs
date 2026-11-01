using System;
using System.Collections.Generic;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 카메라 리그의 속성 규칙 (G440·G442·G446). 리그를 움직이는 것은 호스트이고 여기는 로드 때 볼 것만 둔다.
    /// 모든 View가 받는 <c>name</c>·<c>rig</c>·<c>space</c>·<c>look-at</c>·<c>offset</c>은 해석기가 맡는다.
    /// </summary>
    public sealed class ViewRig
    {
        /// <param name="required">빠지면 로드 문제인 속성과 그 예(<c>"5"</c>).</param>
        /// <param name="check">적힌 속성 값(이름, 값)을 더 본다. 틀리면 이유, 맞으면 null.</param>
        public ViewRig(string name, IReadOnlyDictionary<string, LookAttributeType> attributes, IReadOnlyDictionary<string, string> required,
            Func<string, LookAttributeValue, string?>? check = null)
        {
            Name = name;
            Attributes = attributes;
            Required = required;
            Check = check;
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, LookAttributeType> Attributes { get; }

        public IReadOnlyDictionary<string, string> Required { get; }

        public Func<string, LookAttributeValue, string?>? Check { get; }
    }

    /// <summary>
    /// <c>.view.xml</c>의 <c>&lt;View&gt;</c> 하나 (G441·G445·G446). 볼 자리는 로드 때 그 공간으로 바꿔 둔다.
    /// </summary>
    public sealed class ViewDeclaration
    {
        public ViewDeclaration(string name, string rig, string mod, string place, string space, BridgeValue lookAt, IReadOnlyList<long> offset,
            IReadOnlyDictionary<string, LookAttributeValue> settings)
        {
            Name = name;
            Rig = rig;
            Mod = mod;
            Place = place;
            Space = space;
            LookAt = lookAt;
            Offset = offset;
            Settings = settings;
        }

        public string Name { get; }
        public string Rig { get; }
        public string Mod { get; }
        public string Place { get; }
        public string Space { get; }

        /// <summary>적힌 <c>look-at</c>을 자리 값으로 읽은 것.</summary>
        public BridgeValue LookAt { get; }

        /// <summary>동·북·위(고정소수점 원래 정수값, 칸 단위). 빼면 0.</summary>
        public IReadOnlyList<long> Offset { get; }

        /// <summary>리그만의 속성 중 적힌 것.</summary>
        public IReadOnlyDictionary<string, LookAttributeValue> Settings { get; }

        /// <summary>볼 위치: <c>look-at</c>의 자리에서 <c>offset</c>만큼 민 곳.</summary>
        public LookPosition Target { get; internal set; }

        /// <summary>그 자리의 방향 틀 (G443).</summary>
        public LookFrame Frame { get; internal set; }

        /// <summary>그 공간 한 칸의 길이(고정소수점 원래 정수값).</summary>
        public long Unit { get; internal set; }

        /// <summary>적힌 수 하나(고정소수점 원래 정수값), 없으면 <paramref name="otherwise"/>.</summary>
        public long Number(string name, long otherwise) => Settings.TryGetValue(name, out var value) ? value.Numbers[0] : otherwise;
    }
}
