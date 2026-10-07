using LexForge250;
using Xunit;

namespace LexForge250.Tests;

public class RuleAuditTests
{
    [Fact]
    public void Overlap_ReportsBothNamesAndShortestWitness()
    {
        var lexer = LexerCompiler.Compile(new LexRule[]
        {
            new("First", "ab"),
            new("Second", "a(b|c)"),
        });

        RuleAuditReport report = lexer.Audit();

        RuleOverlap overlap = Assert.Single(report.Overlaps);
        Assert.Equal("First", overlap.FirstRule);
        Assert.Equal("Second", overlap.SecondRule);
        Assert.Equal("ab", overlap.Witness);
    }

    [Fact]
    public void SharedPrefix_IsNotAnOverlap()
    {
        var lexer = LexerCompiler.Compile(new LexRule[]
        {
            new("Short", "ab"),
            new("Long", "abc"),
        });

        RuleAuditReport report = lexer.Audit();

        Assert.Empty(report.Overlaps);
        Assert.Equal(new[] { "Short", "Long" }, report.Outcomes.Select(o => o.RuleName).ToArray());
        Assert.True(report.Outcomes.All(o => o.CanWin));
    }

    [Fact]
    public void Witness_IsShortestThenAsciiLexSmallest()
    {
        var lexer = LexerCompiler.Compile(new LexRule[]
        {
            new("R1", "ab|ba"),
            new("R2", "ab|ba"),
        });

        Assert.Equal("ab", Assert.Single(lexer.Audit().Overlaps).Witness);
    }

    [Fact]
    public void PairsAreReportedOnce_InStableOrder()
    {
        var lexer = LexerCompiler.Compile(new LexRule[]
        {
            new("A", "ab"),
            new("B", "ac"),
            new("C", "a[bc]"),
        });

        RuleAuditReport report = lexer.Audit();

        Assert.Equal(
            new[] { ("A", "C", "ab"), ("B", "C", "ac") },
            report.Overlaps.Select(o => (o.FirstRule, o.SecondRule, o.Witness)).ToArray());
    }

    [Fact]
    public void WinnableRule_GetsShortestWitness()
    {
        var lexer = LexerCompiler.Compile(new LexRule[]
        {
            new("Keyword", "if"),
            new("Ident", "[a-z]+"),
        });

        RuleAuditReport report = lexer.Audit();

        RuleOutcome keyword = report.Outcomes[0];
        RuleOutcome ident = report.Outcomes[1];
        Assert.True(keyword.CanWin);
        Assert.Equal("if", keyword.Witness);
        Assert.True(ident.CanWin);
        Assert.Equal("i", ident.Witness); // shortest word no earlier rule accepts
    }

    [Fact]
    public void FullyShadowed_BySingleEarlierRule()
    {
        var lexer = LexerCompiler.Compile(new LexRule[]
        {
            new("Broad", "a|ab"),
            new("Narrow", "a"),
        });

        RuleOutcome narrow = lexer.Audit().Outcomes[1];

        Assert.False(narrow.CanWin);
        Assert.Equal("a", narrow.ShadowWitness);
        Assert.Equal("Broad", narrow.WinningRule);
    }

    [Fact]
    public void FullyShadowed_ConsidersUnionOfAllEarlierRules()
    {
        // No single earlier rule contains C, but their union does:
        // "ab" is stolen by A, "ac" by B.
        var lexer = LexerCompiler.Compile(new LexRule[]
        {
            new("A", "ab"),
            new("B", "ac"),
            new("C", "a[bc]"),
        });

        RuleOutcome c = lexer.Audit().Outcomes[2];

        Assert.False(c.CanWin);
        Assert.Equal("ab", c.ShadowWitness);
        Assert.Equal("A", c.WinningRule);
    }

    [Fact]
    public void LongerWord_LetsLaterRuleWin()
    {
        var lexer = LexerCompiler.Compile(new LexRule[]
        {
            new("Short", "ab"),
            new("Long", "abc"),
        });

        RuleOutcome longRule = lexer.Audit().Outcomes[1];

        Assert.True(longRule.CanWin);
        Assert.Equal("abc", longRule.Witness);
    }

    [Fact]
    public void SkipRules_CompeteLikeAnyOtherRule()
    {
        var lexer = LexerCompiler.Compile(new LexRule[]
        {
            new("Ws", "[ ]+", Skip: true),
            new("Spaces", "[ ]"),
        });

        RuleOutcome spaces = lexer.Audit().Outcomes[1];
        Assert.False(spaces.CanWin);
        Assert.Equal(" ", spaces.ShadowWitness);
        Assert.Equal("Ws", spaces.WinningRule);
    }

    [Fact]
    public void AuditIsReusable_AndDoesNotPolluteScanning()
    {
        var rules = new List<LexRule>
        {
            new("A", "ab"),
            new("B", "abc"),
        };
        var lexer = LexerCompiler.Compile(rules);

        RuleAuditReport first = lexer.Audit();
        RuleAuditReport second = lexer.Audit();

        Assert.Equal(
            first.Outcomes.Select(o => (o.RuleName, o.CanWin, o.Witness)).ToArray(),
            second.Outcomes.Select(o => (o.RuleName, o.CanWin, o.Witness)).ToArray());

        var tokens = lexer.Scan("abcab");
        Assert.Equal(new[] { "B", "A" }, tokens.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void NullableSubExpressionUnderStar_CompilesButOverallNullableRejected()
    {
        // (a?)*b cannot match the empty string overall and must compile.
        var lexer = LexerCompiler.Compile(new LexRule[]
        {
            new("R", "(a?)*b"),
        });
        Assert.Equal("R", Assert.Single(lexer.Scan("b")).Name);
        Assert.Equal("R", Assert.Single(lexer.Scan("aaab")).Name);

        Assert.Throws<LexerCompileException>(() => LexerCompiler.Compile(new[] { new LexRule("R", "(a?)+") }));
        Assert.Throws<LexerCompileException>(() => LexerCompiler.Compile(new[] { new LexRule("R", "(a?)*") }));
    }

    [Fact]
    public void AuditAgreesWithScanner_OnShadowWitness()
    {
        var lexer = LexerCompiler.Compile(new LexRule[]
        {
            new("Keyword", "if"),
            new("Ident", "[a-z]+"),
        });

        RuleOutcome ident = lexer.Audit().Outcomes[1];
        Assert.True(ident.CanWin);

        var shadowed = LexerCompiler.Compile(new LexRule[]
        {
            new("Ident", "[a-z]+"),
            new("Keyword", "if"),
        });
        RuleOutcome keyword = shadowed.Audit().Outcomes[1];
        Assert.False(keyword.CanWin);
        Assert.Equal("if", keyword.ShadowWitness);
        Assert.Equal("Ident", keyword.WinningRule);
        Assert.Equal("Ident", Assert.Single(shadowed.Scan("if")).Name);
    }
}
