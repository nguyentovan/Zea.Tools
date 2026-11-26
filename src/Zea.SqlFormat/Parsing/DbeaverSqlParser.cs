using Antlr4.Runtime;
using Zea.SqlFormat.Formatters.RightWay;

namespace Zea.SqlFormat.Parsing;

internal static class DbeaverSqlParser
{
    public static SqlSegment Parse(string sql, string dialect, TsqlPredictionGraph? tsqlGraph)
    {
        var diagnostics = new SqlErrorListener(dialect);
        var stream = CreateTokenStream(sql, dialect, diagnostics);
        var tokens = ReadLosslessTokens(sql, dialect, stream);

        if (tokens.All(token => token.Type == SQLStandardLexer.Separator))
        {
            throw new FormatException($"Could not parse {dialect}. Input contains no SQL.");
        }

        var selectParser = new SqlFluffSelectParser(tokens, dialect);
        var select = selectParser.Match();
        if (select != null)
        {
            return select;
        }

        var parser = CreateParser(stream, dialect, diagnostics);
        SQLStandardParser.SqlScriptContext root;
        if (tsqlGraph != null)
        {
            root = ParseTsql(parser, stream, tokens, diagnostics, tsqlGraph);
        }
        else
        {
            root = parser.sqlScript();
        }

        var result = DbeaverTreeAdapter.Convert(root, tokens, dialect);
        return result;
    }

    internal static IReadOnlyList<IToken> Tokenize(string sql, string dialect)
    {
        var diagnostics = new SqlErrorListener(dialect);
        var stream = CreateTokenStream(sql, dialect, diagnostics);
        var tokens = ReadLosslessTokens(sql, dialect, stream);
        return tokens;
    }

    private static CommonTokenStream CreateTokenStream(string sql, string dialect, SqlErrorListener diagnostics)
    {
        var input = new AntlrInputStream(sql);
        var lexer = new SQLStandardLexer(input);
        lexer.Dialect = dialect;
        if (dialect == "tsql")
        {
            lexer.CacheTsqlLexerStartStates();
        }

        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(diagnostics);

        var stream = new CommonTokenStream(lexer);
        stream.Fill();
        return stream;
    }

    private static SQLStandardParser.SqlScriptContext ParseTsql(SQLStandardParser parser, CommonTokenStream stream,
        IReadOnlyList<IToken> tokens, SqlErrorListener diagnostics, TsqlPredictionGraph predictionGraph)
    {
        var hasOperators = HasValueExpressionOperators(tokens);
        parser.HasValueExpressionOperators = hasOperators;
        var graph = predictionGraph.Get(parser, tokens);
        parser.UseContextualPrediction(predictionGraph, graph);
        try
        {
            var result = parser.sqlScript();
            return result;
        }
        catch (FormatException error) when (error is not TsqlPredictionLimitException)
        {
            // Use the original prediction and diagnostics for inputs the
            // reduced graph does not accept.
            stream.Seek(0);
            parser = CreateParser(stream, "tsql", diagnostics);
            parser.HasValueExpressionOperators = hasOperators;
            parser.UseContextualPrediction(predictionGraph);
        }

        try
        {
            var result = parser.sqlScript();
            return result;
        }
        catch (FormatException error) when (error is not TsqlPredictionLimitException)
        {
            stream.Seek(0);
            parser = CreateParser(stream, "tsql", diagnostics);
            parser.UseContextualPrediction(predictionGraph, contextual: false);
            var result = parser.sqlScript();
            return result;
        }
    }

    private static bool HasValueExpressionOperators(IReadOnlyList<IToken> tokens)
    {
        var previous = TokenConstants.InvalidType;
        foreach (var token in tokens)
        {
            if (token.Channel != TokenConstants.DefaultChannel)
            {
                continue;
            }

            var current = token.Type;
            if (current is SQLStandardLexer.PlusSign or SQLStandardLexer.MinusSign or SQLStandardLexer.Solidus
                or SQLStandardLexer.ConcatenationOperator or SQLStandardLexer.ShiftLeft or SQLStandardLexer.ShiftRight)
            {
                return true;
            }

            if (current == SQLStandardLexer.Asterisk && previous is not (SQLStandardLexer.SELECT or SQLStandardLexer.Period))
            {
                return true;
            }

            // SELECT * and a.* are wildcards in the T-SQL grammar.
            previous = current;
        }

        return false;
    }

    private static SQLStandardParser CreateParser(CommonTokenStream stream, string dialect, SqlErrorListener diagnostics)
    {
        var parser = new SQLStandardParser(stream);
        parser.Dialect = dialect;
        parser.RemoveErrorListeners();
        parser.AddErrorListener(diagnostics);
        return parser;
    }

    private static IReadOnlyList<IToken> ReadLosslessTokens(string sql, string dialect, CommonTokenStream stream)
    {
        var tokens = new List<IToken>(stream.Size - 1);
        var offset = 0;
        for (var index = 0; index < stream.Size - 1; index++)
        {
            var token = stream.Get(index);
            if (token.StartIndex != offset || token.StopIndex < offset || token.StopIndex >= sql.Length)
            {
                throw new FormatException($"Could not parse {dialect}. The lexer did not cover the input at offset {offset}.");
            }

            tokens.Add(token);
            offset = token.StopIndex + 1;
        }

        if (offset != sql.Length)
        {
            throw new FormatException($"Could not parse {dialect}. The lexer did not cover the input at offset {offset}.");
        }

        return tokens;
    }

    private sealed class SqlErrorListener(string dialect) : BaseErrorListener, IAntlrErrorListener<int>
    {
        public override void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            IToken offendingSymbol,
            int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            throw CreateError(line, charPositionInLine, msg);
        }

        void IAntlrErrorListener<int>.SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            int offendingSymbol,
            int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            throw CreateError(line, charPositionInLine, msg);
        }

        private FormatException CreateError(int line, int column, string message)
        {
            var error = new FormatException($"Could not parse {dialect}. Line {line}, column {column + 1}: {message}");
            return error;
        }
    }
}
