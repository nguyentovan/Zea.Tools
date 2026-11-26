using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;

namespace Zea.SqlFormat.Parsing;

partial class SQLStandardLexer
{
    internal void CacheTsqlLexerStartStates()
    {
        Interpreter = new TsqlStartStateCachingSimulator(this);
    }

    private sealed class TsqlStartStateCachingSimulator(SQLStandardLexer lexer)
        : LexerATNSimulator(lexer, lexer.Atn, new DFA[] { new DFA(lexer.Atn.GetDecisionState(0), 0) },
            lexer.Interpreter.getSharedContextCache())
    {
        private readonly DFAState?[] _startStates = new DFAState?[2];

        internal void Prepare()
        {
            // Before consuming the first character, GO is the only T-SQL
            // predicate that depends on the current input position.
            var index = lexer.IsBatchSeparatorAhead() ? 1 : 0;
            var start = _startStates[index];
            if (start == null)
            {
                var input = (ICharStream)lexer.InputStream;
                var closure = ComputeStartState(input, atn.modeToStartState[Lexer.DEFAULT_MODE]);
                closure.hasSemanticContext = false;
                start = AddDFAState(closure);
                _startStates[index] = start;
            }

            decisionToDFA[Lexer.DEFAULT_MODE].s0 = start;
        }
    }
}
