using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Script
{
    public class LexerTests
    {
        private static (IReadOnlyList<Token> Tokens, IReadOnlyList<Diagnostic> Diagnostics) Lex(string text)
        {
            var bag = new DiagnosticBag();
            var tokens = Lexer.Tokenize(text, "test.py", bag);
            return (tokens, bag.ToSortedList());
        }

        private static string Kinds(IReadOnlyList<Token> tokens) =>
            string.Join(" ", tokens.Select(t => t.Kind == TokenKind.Op || t.Kind == TokenKind.Name ? t.Text : t.Kind.ToString()));

        [Test]
        public void IndentAndDedent()
        {
            var (tokens, diags) = Lex("a\n  b\n  c\nd\n");
            Assert.That(diags, Is.Empty);
            Assert.That(Kinds(tokens), Is.EqualTo("a Newline Indent b Newline c Newline Dedent d Newline EndOfFile"));
        }

        [Test]
        public void BlankAndCommentLinesDoNotAffectIndentation()
        {
            var (tokens, diags) = Lex("a\n\n   # comment\n  b\n");
            Assert.That(diags, Is.Empty);
            Assert.That(Kinds(tokens), Is.EqualTo("a Newline Indent b Newline Dedent EndOfFile"));
        }

        [Test]
        public void NewlinesInsideBracketsAreJoined()
        {
            var (tokens, diags) = Lex("f(1,\n    2)\n");
            Assert.That(diags, Is.Empty);
            Assert.That(Kinds(tokens), Is.EqualTo("f ( Int , Int ) Newline EndOfFile"));
        }

        [TestCase("a\nb\n")]
        [TestCase("a\r\nb\r\n")]
        [TestCase("a\rb\r")]
        public void AllNewlineStylesCountLinesTheSame(string text)
        {
            var (tokens, _) = Lex(text);
            var b = tokens.Single(t => t.IsName("b"));
            Assert.That(b.Span.Line, Is.EqualTo(2));
            Assert.That(b.Span.Col, Is.EqualTo(1));
        }

        [Test]
        public void ByteOrderMarkIsStripped()
        {
            var (tokens, diags) = Lex("﻿abc\n");
            Assert.That(diags, Is.Empty);
            Assert.That(tokens[0].Text, Is.EqualTo("abc"));
            Assert.That(tokens[0].Span.Col, Is.EqualTo(1));
        }

        [Test]
        public void ColumnsCountUtf16Units()
        {
            var (tokens, _) = Lex("x = \"한글\" + y\n");
            var y = tokens.Single(t => t.IsName("y"));
            Assert.That(y.Span.Col, Is.EqualTo(12));
        }

        [TestCase("체력 = 3\n", "체력")]
        [TestCase("unit.위치 = 1\n", "위치")]
        [TestCase("hp체력 = 1\n", "hp체력")]
        [TestCase("ﬁeld = 1\n", "ﬁeld")]
        public void NonAsciiIdentifiersAreRejectedAsOneName(string text, string name)
        {
            var (tokens, diags) = Lex(text);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.LexNonAsciiIdentifier));
            Assert.That(tokens.Count(t => t.Kind == TokenKind.Name && t.Text == name), Is.EqualTo(1));
        }

        [Test]
        public void NonAsciiIsFineInStringsAndComments()
        {
            var (_, diags) = Lex("label = \"정찰병\"  # 한글 주석\n");
            Assert.That(diags, Is.Empty);
        }

        [Test]
        public void TabInIndentationIsAnError()
        {
            var (_, diags) = Lex("a\n\tb\n");
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.LexTab));
        }

        [Test]
        public void InconsistentDedentIsAnError()
        {
            var (_, diags) = Lex("a\n    b\n  c\n");
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.LexBadIndent));
        }

        [TestCase("1.5", 1_500_000L)]
        [TestCase("0.000001", 1L)]
        [TestCase(".5", 500_000L)]
        [TestCase("2.", 2_000_000L)]
        [TestCase("1_000.25", 1_000_250_000L)]
        [TestCase("1.5000000", 1_500_000L)]
        public void FixedLiterals(string text, long raw)
        {
            var (tokens, diags) = Lex(text);
            Assert.That(diags, Is.Empty);
            Assert.That(tokens[0].Kind, Is.EqualTo(TokenKind.Fixed));
            Assert.That(tokens[0].IntValue, Is.EqualTo(raw));
        }

        [Test]
        public void FixedLiteralWithTooManyDigitsIsAnError()
        {
            var (_, diags) = Lex("0.0000001");
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.LexFixedPrecision));
        }

        [TestCase("9223372036854775807", 9223372036854775807L)]
        [TestCase("1_000", 1000L)]
        [TestCase("0", 0L)]
        public void IntLiterals(string text, long value)
        {
            var (tokens, diags) = Lex(text);
            Assert.That(diags, Is.Empty);
            Assert.That(tokens[0].Kind, Is.EqualTo(TokenKind.Int));
            Assert.That(tokens[0].IntValue, Is.EqualTo(value));
        }

        [TestCase("9223372036854775808", Codes.LexIntOverflow)]
        [TestCase("1e5", Codes.LexBadNumber)]
        [TestCase("0x1F", Codes.LexBadNumber)]
        [TestCase("007", Codes.LexBadNumber)]
        [TestCase("1__0", Codes.LexBadNumber)]
        public void BadNumbers(string text, string code)
        {
            var (_, diags) = Lex(text);
            Assert.That(diags.Select(d => d.Code), Does.Contain(code));
        }

        [Test]
        public void StringEscapes()
        {
            var (tokens, diags) = Lex("'a\\n\\'b\\u00e9'");
            Assert.That(diags, Is.Empty);
            Assert.That(tokens[0].StringValue, Is.EqualTo("a\n'bé"));
        }

        [Test]
        public void TripleQuotedStringSpansLines()
        {
            var (tokens, diags) = Lex("x = \"\"\"a\r\nb\"\"\"\ny\n");
            Assert.That(diags, Is.Empty);
            Assert.That(tokens.Single(t => t.Kind == TokenKind.String).StringValue, Is.EqualTo("a\nb"));
            Assert.That(tokens.Single(t => t.IsName("y")).Span.Line, Is.EqualTo(3));
        }

        [Test]
        public void UnterminatedString()
        {
            var (_, diags) = Lex("'abc\n");
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.LexUnterminatedString));
        }

        [Test]
        public void FStringIsRejectedAsUnsupported()
        {
            var (_, diags) = Lex("f'{x}'");
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.LexUnsupportedString));
        }

        [Test]
        public void UnclosedBracketIsReportedAtOpener()
        {
            var (_, diags) = Lex("f(1,\n2\n");
            var d = diags.Single(x => x.Code == Codes.LexUnbalanced);
            Assert.That(d.Line, Is.EqualTo(1));
            Assert.That(d.Col, Is.EqualTo(2));
        }

        [Test]
        public void MismatchedBracket()
        {
            var (_, diags) = Lex("f(1]\n");
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.LexUnbalanced));
        }
    }
}
