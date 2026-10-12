namespace Kesa.Engine.Diagnostics
{
    /// <summary>
    /// 진단 코드. 잠정 체계: 영역 접두사 + 세 자리 번호 (spec-gaps G6).
    /// LEX = 렉서, PAR = 파서, MOD = 모드 매니페스트·정렬.
    /// </summary>
    public static class Codes
    {
        public const string LexTab = "LEX001";
        public const string LexBadIndent = "LEX002";
        public const string LexUnterminatedString = "LEX003";
        public const string LexBadEscape = "LEX004";
        public const string LexUnsupportedString = "LEX005";
        public const string LexBadNumber = "LEX006";
        public const string LexFixedPrecision = "LEX007";
        public const string LexIntOverflow = "LEX008";
        public const string LexUnexpectedChar = "LEX009";
        public const string LexUnbalanced = "LEX010";
        public const string LexNonAsciiIdentifier = "LEX011";

        public const string ParSyntax = "PAR001";
        public const string ParUnsupported = "PAR002";
        public const string ParDuplicateKeyword = "PAR003";
        public const string ParPositionalAfterKeyword = "PAR004";

        public const string ModNoManifest = "MOD001";
        public const string ModBadManifest = "MOD002";
        public const string ModUnknownField = "MOD003";
        public const string ModNotLiteral = "MOD004";
        public const string ModBadId = "MOD005";
        public const string ModDuplicateId = "MOD006";
        public const string ModMissingDependency = "MOD007";
        public const string ModCycle = "MOD008";
        public const string ModDependencyFailed = "MOD009";
        public const string ModSelfReference = "MOD010";
    }
}
