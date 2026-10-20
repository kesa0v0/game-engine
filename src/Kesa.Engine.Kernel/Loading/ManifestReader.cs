using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// <c>mod.py</c>를 AST로만 읽는다 (spec S1: 파일을 실행하지 않음).
    /// mod.py에는 <c>Mod(...)</c> 호출 하나와 <c>use(...)</c>/<c>use_all(...)</c>만 올 수 있고, 인자는 모두 리터럴이다.
    /// </summary>
    public static class ManifestReader
    {
        private static readonly string[] Fields =
        {
            "id", "version", "dependencies", "loadAfter", "optional", "defaultLanguage",
        };

        /// <param name="text">mod.py 내용</param>
        /// <param name="directory">모드 루트 기준 모드 폴더 이름</param>
        /// <returns>오류가 있으면 null (진단은 <paramref name="diagnostics"/>에 기록)</returns>
        public static ModManifest? Read(string text, string directory, DiagnosticBag diagnostics) =>
            Read(text, directory, diagnostics, out _);

        /// <param name="declaredId">
        /// 다른 필드에 오류가 있어도 id만은 읽혔으면 그 id. 이 모드에 의존하는 모드가
        /// "설치 안 됨"이 아니라 "로드 실패"로 보고되게 한다.
        /// </param>
        public static ModManifest? Read(string text, string directory, DiagnosticBag diagnostics, out string? declaredId)
        {
            declaredId = null;
            string file = directory + "/mod.py";
            var local = new DiagnosticBag();
            var module = Parser.Parse(text, file, local);
            if (local.HasErrors)
            {
                diagnostics.AddRange(local.ToSortedList());
                return null;
            }

            CallExpr? modCall = null;
            var uses = new List<UseEntry>();

            foreach (var stmt in module.Body)
            {
                if (stmt is ExprStmt es && es.Expr is CallExpr call && call.Callee is NameExpr callee)
                {
                    switch (callee.Name)
                    {
                        case "Mod":
                            if (modCall != null)
                            {
                                local.Error(Codes.ModBadManifest, "mod.py contains more than one Mod(...) call", call.Span,
                                    related: new[] { modCall.Span });
                            }
                            else
                            {
                                modCall = call;
                            }
                            continue;
                        case "use":
                            ReadUse(call, UseKind.File, uses, local);
                            continue;
                        case "use_all":
                            ReadUse(call, UseKind.Directory, uses, local);
                            continue;
                    }
                }

                local.Error(Codes.ModBadManifest, "mod.py may only contain Mod(...), use(...) and use_all(...)", stmt.Span);
            }

            if (modCall == null)
            {
                local.Error(Codes.ModBadManifest, "mod.py has no Mod(...) call", new SourceSpan(file, 1, 1));
                diagnostics.AddRange(local.ToSortedList());
                return null;
            }

            var manifest = ReadMod(modCall, directory, uses, local);
            declaredId = manifest?.Id;
            diagnostics.AddRange(local.ToSortedList());
            return local.HasErrors ? null : manifest;
        }

        private static ModManifest? ReadMod(CallExpr call, string directory, List<UseEntry> uses, DiagnosticBag diags)
        {
            string? id = null;
            string? version = null;
            string? defaultLanguage = null;
            var dependencies = new List<ModRef>();
            var loadAfter = new List<ModRef>();
            var optional = new List<ModRef>();

            foreach (var arg in call.Arguments)
            {
                if (arg.Name == null)
                {
                    diags.Error(Codes.ModBadManifest, "Mod(...) takes keyword arguments only", arg.Span,
                        hint: "write Mod(id=\"my_mod\", ...)");
                    continue;
                }

                switch (arg.Name)
                {
                    case "id":
                        id = ReadString(arg, diags);
                        if (id != null && !IsValidId(id))
                        {
                            diags.Error(Codes.ModBadId, "invalid mod id '" + id + "'", arg.Value.Span, hint: IdRule);
                            id = null;
                        }
                        else if (id == ReservedId)
                        {
                            diags.Error(Codes.ModBadId, "mod id '" + ReservedId + "' is reserved for the engine", arg.Value.Span,
                                hint: "the engine names its own commands and text keys with it; pick another id");
                            id = null;
                        }
                        break;
                    case "version":
                        version = ReadString(arg, diags);
                        break;
                    case "defaultLanguage":
                        defaultLanguage = ReadString(arg, diags);
                        // 번역 폴더 이름과 같은 BCP 47 태그 (G256)
                        if (defaultLanguage != null && !LanguageTag.IsValid(defaultLanguage))
                        {
                            diags.Error(Codes.ModBadManifest, "defaultLanguage '" + defaultLanguage + "' is not a language tag",
                                arg.Value.Span, hint: LanguageTag.Rule);
                            defaultLanguage = null;
                        }
                        break;
                    case "dependencies":
                        ReadIdList(arg, dependencies, diags);
                        break;
                    case "loadAfter":
                        ReadIdList(arg, loadAfter, diags);
                        break;
                    case "optional":
                        ReadIdList(arg, optional, diags);
                        break;
                    default:
                        diags.Error(Codes.ModUnknownField, "unknown Mod field '" + arg.Name + "'", arg.Span,
                            hint: Suggest.Hint(arg.Name, Fields));
                        break;
                }
            }

            if (id == null)
            {
                if (!HasArgument(call, "id"))
                    diags.Error(Codes.ModBadManifest, "Mod(...) requires id=", call.Span);
                return null;
            }

            // 이후 오류가 있어도 매니페스트를 만들어 id를 돌려준다. 오류 여부는 호출자가 판단한다.

            CheckSelfReference(id, dependencies, "dependencies", diags);
            CheckSelfReference(id, loadAfter, "loadAfter", diags);
            CheckSelfReference(id, optional, "optional", diags);

            return new ModManifest(id, directory, call.Span, version, defaultLanguage, dependencies, loadAfter, optional, uses);
        }

        private static bool HasArgument(CallExpr call, string name)
        {
            foreach (var a in call.Arguments)
                if (a.Name == name) return true;
            return false;
        }

        private static void CheckSelfReference(string id, List<ModRef> refs, string field, DiagnosticBag diags)
        {
            foreach (var r in refs)
            {
                if (r.Id == id) diags.Error(Codes.ModSelfReference, "mod '" + id + "' lists itself in " + field, r.Span);
            }
        }

        private static string? ReadString(Argument arg, DiagnosticBag diags)
        {
            if (arg.Value is StringLiteral s) return s.Value;
            diags.Error(Codes.ModNotLiteral, "Mod field '" + arg.Name + "' must be a string literal", arg.Value.Span);
            return null;
        }

        private static void ReadIdList(Argument arg, List<ModRef> into, DiagnosticBag diags)
        {
            if (!(arg.Value is ListExpr list))
            {
                diags.Error(Codes.ModNotLiteral, "Mod field '" + arg.Name + "' must be a list of string literals",
                    arg.Value.Span);
                return;
            }

            foreach (var item in list.Items)
            {
                if (!(item is StringLiteral s))
                {
                    diags.Error(Codes.ModNotLiteral, "Mod field '" + arg.Name + "' must contain string literals only",
                        item.Span);
                    continue;
                }
                if (!IsValidId(s.Value))
                {
                    diags.Error(Codes.ModBadId, "invalid mod id '" + s.Value + "'", s.Span, hint: IdRule);
                    continue;
                }
                into.Add(new ModRef(s.Value, s.Span));
            }
        }

        private static void ReadUse(CallExpr call, UseKind kind, List<UseEntry> into, DiagnosticBag diags)
        {
            string fn = kind == UseKind.File ? "use" : "use_all";
            if (call.Arguments.Count != 1 || call.Arguments[0].Name != null)
            {
                diags.Error(Codes.ModBadManifest, fn + "(...) takes exactly one path", call.Span);
                return;
            }
            if (!(call.Arguments[0].Value is StringLiteral s))
            {
                diags.Error(Codes.ModNotLiteral, fn + "(...) path must be a string literal", call.Arguments[0].Value.Span);
                return;
            }

            string path = s.Value;
            string? problem = CheckRelativePath(path);
            if (problem == null && kind == UseKind.File && !path.EndsWith(".py", StringComparison.Ordinal))
                problem = "use(...) must name a .py file";
            if (problem != null)
            {
                diags.Error(Codes.ModBadManifest, problem + ": '" + path + "'", s.Span);
                return;
            }

            if (kind == UseKind.Directory) path = path.TrimEnd('/');
            into.Add(new UseEntry(kind, path, s.Span));
        }

        /// <summary>모드 폴더 밖이나 절대 경로를 가리키면 문제를 설명하는 문자열, 괜찮으면 null.</summary>
        private static string? CheckRelativePath(string path)
        {
            if (path.Length == 0) return "empty path";
            if (path.IndexOf('\\') >= 0) return "use '/' as the path separator";
            if (path[0] == '/' || path.IndexOf(':') >= 0) return "path must be relative to the mod folder";
            foreach (var segment in path.Split('/'))
            {
                if (segment == "..") return "path must not leave the mod folder";
            }
            return null;
        }

        private const string IdRule = "mod ids are lowercase letters, digits and '_', starting with a letter";

        /// <summary>엔진이 쓰는 이름 (spec-gaps G273): 엔진 커맨드(<c>engine.pick</c>)와 번역 키(<c>engine-hidden</c>). 모드 id로 쓸 수 없다.</summary>
        public const string ReservedId = "engine";

        /// <summary>모드 id 규칙 (spec-gaps G2): [a-z][a-z0-9_]*. 점은 네임스페이스 구분자라 쓸 수 없다.</summary>
        public static bool IsValidId(string id)
        {
            if (id.Length == 0 || id[0] < 'a' || id[0] > 'z') return false;
            foreach (char c in id)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }
    }
}
