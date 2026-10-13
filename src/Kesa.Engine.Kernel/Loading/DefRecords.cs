using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>Def 리터럴의 필드 하나 (const 값 또는 컴포넌트 값).</summary>
    public sealed class DefField
    {
        public DefField(string name, Value value, SourceSpan span)
        {
            Name = name;
            Value = value;
            Span = span;
        }

        public string Name { get; }

        /// <summary>평가한 값. <c>inherit</c>가 든 식이면 <see cref="DeferredValue"/> (④에서 평가).</summary>
        public Value Value { get; }

        /// <summary>키워드 인자 이름의 위치. 이후 단계의 오류와 변경 이력(S9)이 가리킨다.</summary>
        public SourceSpan Span { get; }
    }

    /// <summary>
    /// ②b가 등록한 Def 하나. 아직 패치(③), 상속 펼치기(④), 링크(⑤), 검증(⑥) 전이라 값은 적힌 그대로이고
    /// 다른 Def를 가리키는 값은 문자열이다.
    /// </summary>
    public sealed class DefRecord
    {
        internal DefRecord(DefType type, string id, string mod, SourceSpan span, NameRef? parent, bool isAbstract,
            bool isOverride, IReadOnlyList<DefField> fields)
        {
            Type = type;
            Id = id;
            Mod = mod;
            Span = span;
            Parent = parent;
            IsAbstract = isAbstract;
            IsOverride = isOverride;
            Fields = fields;
        }

        public DefType Type { get; internal set; }
        public string Id { get; }
        public string Mod { get; }

        /// <summary>id 인자의 위치.</summary>
        public SourceSpan Span { get; }

        public NameRef? Parent { get; }
        public bool IsAbstract { get; }
        public bool IsOverride { get; }

        /// <summary>적힌 순서.</summary>
        public IReadOnlyList<DefField> Fields { get; }

        /// <summary><c>override=True</c>로 이 Def가 교체한 Def.</summary>
        public DefRecord? Replaced { get; internal set; }

        public bool TryGetField(string name, out DefField field)
        {
            foreach (var f in Fields)
            {
                if (f.Name != name) continue;
                field = f;
                return true;
            }
            field = null!;
            return false;
        }
    }

    /// <summary><c>Health(base_max=12)</c>: Def 리터럴 안의 컴포넌트 값.</summary>
    public sealed class ComponentValue : Value
    {
        internal ComponentValue(ComponentType component, IReadOnlyList<DefField> fields, SourceSpan span)
        {
            Component = component;
            Fields = fields;
            Span = span;
        }

        public ComponentType Component { get; internal set; }
        public IReadOnlyList<DefField> Fields { get; }
        public SourceSpan Span { get; }
        public override string KindName => Component.Name + "(...) value";
    }

    /// <summary>Def 생성자의 반환값. 다른 필드에 넣을 수 없다 (Def 참조는 id 문자열).</summary>
    public sealed class DefHandleValue : Value
    {
        internal DefHandleValue(DefRecord record) => Record = record;
        public DefRecord Record { get; }
        public override string KindName => "Def";
    }

    /// <summary>등록이 끝난 Def들 (②b 끝).</summary>
    public sealed class DefRegistry
    {
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<DefRecord> _defs = new List<DefRecord>();

        /// <summary>등록 순서 (로드 순서, 파일 실행 순서). override는 원래 자리를 차지한다.</summary>
        public IReadOnlyList<DefRecord> Defs => _defs;

        public bool TryGet(string id, out DefRecord record)
        {
            if (_index.TryGetValue(id, out int i))
            {
                record = _defs[i];
                return true;
            }
            record = null!;
            return false;
        }

        internal void Add(DefRecord record)
        {
            record.Replaced = null;
            _index.Add(record.Id, _defs.Count);
            _defs.Add(record);
        }

        internal void Replace(DefRecord record)
        {
            int i = _index[record.Id];
            record.Replaced = _defs[i];
            _defs[i] = record;
        }

        internal IEnumerable<string> Ids
        {
            get
            {
                foreach (var d in _defs) yield return d.Id;
            }
        }
    }
}
