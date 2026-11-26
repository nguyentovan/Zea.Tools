using System.Diagnostics;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;

namespace Zea.SqlFormat.Parsing;

internal sealed class TsqlPredictionGraph
{
    private readonly long _started = Stopwatch.GetTimestamp();

    internal void CheckBudget()
    {
        // A grammar ambiguity must leave time for dialect-specific recovery.
        if (Stopwatch.GetElapsedTime(_started) > TimeSpan.FromSeconds(1))
        {
            throw new TsqlPredictionLimitException();
        }
    }

    private ATN? _graph;
    private HashSet<int>? _symbols;
    private bool _hasOperators;

    public ATN Get(SQLStandardParser parser, IReadOnlyList<IToken> tokens)
    {
        var symbols = new HashSet<int> { TokenConstants.EOF };
        foreach (var token in tokens)
        {
            if (token.Channel == TokenConstants.DefaultChannel)
            {
                symbols.Add(token.Type);
            }
        }

        if (_graph != null && _hasOperators == parser.HasValueExpressionOperators && _symbols!.SetEquals(symbols))
        {
            return _graph;
        }

        var graph = Build(parser, symbols);
        _graph = graph;
        _symbols = symbols;
        _hasOperators = parser.HasValueExpressionOperators;
        return graph;
    }

    private static ATN Build(SQLStandardParser parser, HashSet<int> symbols)
    {
        // Use a private copy. Two parses of the same SQL may share it, but
        // neither changes the generated parser's ATN or another SQL request.
        var graph = new ATNDeserializer().Deserialize(parser.SerializedAtn);
        var stateCount = graph.states.Count;
        var reachable = new bool[stateCount];
        var dependents = new List<(int Source, int Required)>?[stateCount];
        var pending = new Queue<int>();

        // Walk backwards from rule exits. Invoking another rule requires
        // both that rule and the continuation at the call site to be viable.
        foreach (var state in graph.states)
        {
            if (state == null)
            {
                continue;
            }

            if (state is RuleStopState)
            {
                reachable[state.stateNumber] = true;
                pending.Enqueue(state.stateNumber);
                continue;
            }

            for (var index = 0; index < state.NumberOfTransitions; index++)
            {
                var edge = state.Transition(index);
                if (edge is RuleTransition call)
                {
                    AddDependency(call.target.stateNumber, state.stateNumber, call.followState.stateNumber);
                    AddDependency(call.followState.stateNumber, state.stateNumber, call.target.stateNumber);
                }
                else if (FixedResult(parser, edge) != false &&
                    (edge.IsEpsilon || MatchesSymbol(edge, symbols, graph.maxTokenType)))
                {
                    AddDependency(edge.target.stateNumber, state.stateNumber, -1);
                }
            }
        }

        while (pending.TryDequeue(out var target))
        {
            if (dependents[target] is not { } incoming)
            {
                continue;
            }

            foreach (var (source, required) in incoming)
            {
                if (!reachable[source] && (required < 0 || reachable[required]))
                {
                    reachable[source] = true;
                    pending.Enqueue(source);
                }
            }
        }

        // Preserve alternative indices and rule-call states used by the
        // generated parser. Redirect impossible epsilon paths to a dead end.
        var deadEnd = new BasicState();
        deadEnd.epsilonOnlyTransitions = true;
        graph.AddState(deadEnd);

        for (var index = 0; index < stateCount; index++)
        {
            var state = graph.states[index];
            if (state == null || state is RuleStopState)
            {
                continue;
            }

            for (var slot = 0; slot < state.NumberOfTransitions; slot++)
            {
                var edge = state.Transition(slot);
                if (edge is PredicateTransition predicate && FixedResult(parser, predicate) == false)
                {
                    // FailedPredicateException still reads the predicate at
                    // its generated state when the parser reports an error.
                    var blocked = new PredicateTransition(deadEnd, predicate.ruleIndex, predicate.predIndex,
                        predicate.isCtxDependent);
                    state.SetTransition(slot, blocked);
                }
                else if (edge is EpsilonTransition && !reachable[edge.target.stateNumber])
                {
                    state.SetTransition(slot, new EpsilonTransition(deadEnd));
                }
            }
        }

        return graph;

        void AddDependency(int target, int source, int required)
        {
            dependents[target] ??= new List<(int Source, int Required)>();
            dependents[target]!.Add((source, required));
        }
    }

    private static bool MatchesSymbol(Transition transition, HashSet<int> symbols, int maxTokenType)
    {
        foreach (var symbol in symbols)
        {
            if (transition.Matches(symbol, TokenConstants.MinUserTokenType, maxTokenType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool? FixedResult(SQLStandardParser parser, Transition transition)
    {
        if (transition is not PredicateTransition predicate || predicate.isCtxDependent ||
            !parser.IsFixedTsqlPredicate(predicate.predIndex))
        {
            return null;
        }

        var result = parser.Sempred(null, predicate.ruleIndex, predicate.predIndex);
        return result;
    }
}

internal sealed class TsqlPredictionLimitException()
    : FormatException("Could not parse tsql within the grammar prediction budget.")
{
}
