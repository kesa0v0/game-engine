using System.Collections.Generic;
using Kesa.Engine.Schema;

namespace Kesa.Engine.Loading
{
    /// <summary>한 회차의 ②a·②b가 남긴 것: 살아남은 모드(로드 순서), 완성된 스키마, 등록된 Def.</summary>
    public sealed class DefinitionSnapshot
    {
        internal DefinitionSnapshot(IReadOnlyList<ModManifest> mods, SchemaSet schema, DefRegistry defs)
        {
            Mods = mods;
            Schema = schema;
            Defs = defs;
        }

        public IReadOnlyList<ModManifest> Mods { get; }
        public SchemaSet Schema { get; }
        public DefRegistry Defs { get; }
    }
}
