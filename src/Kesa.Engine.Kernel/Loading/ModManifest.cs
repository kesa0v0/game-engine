using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Loading
{
    /// <summary>모드 id 참조 하나와 그것이 적힌 위치.</summary>
    public readonly struct ModRef
    {
        public ModRef(string id, SourceSpan span)
        {
            Id = id;
            Span = span;
        }

        public string Id { get; }
        public SourceSpan Span { get; }
    }

    public enum UseKind
    {
        File,
        Directory,
    }

    /// <summary><c>use("a.py")</c> 또는 <c>use_all("dir/")</c> 한 줄.</summary>
    public readonly struct UseEntry
    {
        public UseEntry(UseKind kind, string path, SourceSpan span)
        {
            Kind = kind;
            Path = path;
            Span = span;
        }

        public UseKind Kind { get; }

        /// <summary>모드 폴더 기준 상대 경로, 구분자 '/'.</summary>
        public string Path { get; }
        public SourceSpan Span { get; }
    }

    /// <summary><c>mod.py</c>를 파일 실행 없이 읽은 결과 (spec S1 ①).</summary>
    public sealed class ModManifest
    {
        public ModManifest(string id, string directory, SourceSpan span, string? version, string? defaultLanguage,
            IReadOnlyList<ModRef> dependencies, IReadOnlyList<ModRef> loadAfter, IReadOnlyList<ModRef> optional,
            IReadOnlyList<UseEntry> uses)
        {
            Id = id;
            Directory = directory;
            Span = span;
            Version = version;
            DefaultLanguage = defaultLanguage;
            Dependencies = dependencies;
            LoadAfter = loadAfter;
            Optional = optional;
            Uses = uses;
        }

        public string Id { get; }

        /// <summary>모드 루트 기준 모드 폴더 이름.</summary>
        public string Directory { get; }

        /// <summary><c>Mod(...)</c> 호출의 위치.</summary>
        public SourceSpan Span { get; }

        public string? Version { get; }
        public string? DefaultLanguage { get; }

        /// <summary>필수 의존성: 없거나 로드에 실패하면 이 모드도 비활성화된다.</summary>
        public IReadOnlyList<ModRef> Dependencies { get; }

        /// <summary>순서 힌트: 있으면 그 뒤에 로드한다. 없어도 된다.</summary>
        public IReadOnlyList<ModRef> LoadAfter { get; }

        /// <summary>선택 의존성: 있으면 그 뒤에 로드한다. 없어도 된다.</summary>
        public IReadOnlyList<ModRef> Optional { get; }

        /// <summary>파일 실행 순서 (spec S1).</summary>
        public IReadOnlyList<UseEntry> Uses { get; }
    }
}
