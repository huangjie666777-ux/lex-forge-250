using LexForge250;

var rules = new List<LexRule>
{
    new("Keyword", "if|else|while|return"),
    new("Ident", "[a-zA-Z_][a-zA-Z0-9_]*"),
    new("Number", "[0-9]+(\\.[0-9]+)?"),
    new("Op", "\\+|\\-|\\*|/|==|="),
    new("LParen", "\\("),
    new("RParen", "\\)"),
    new("Semicolon", ";"),
    new("Whitespace", "[ \t\r\n]+", Skip: true),
};

Lexer lexer = LexerCompiler.Compile(rules);

const string source = "if (count == 42) return total + 3.5;\nelse whilex = 0;";
Console.WriteLine($"Source: {source}");
Console.WriteLine();
foreach (LexToken t in lexer.Scan(source))
    Console.WriteLine($"{t.Name,-10} '{t.Text}'  offset={t.Offset} len={t.Length} line={t.Line} col={t.Column}");

Console.WriteLine();
try
{
    lexer.Scan("ok := 1");
}
catch (LexerScanException ex)
{
    Console.WriteLine($"Scan error: {ex.Message}");
}
