using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;

namespace Zea.SqlFormat.Parsing;

internal sealed class ContextualSqlPrediction(SQLStandardParser parser, ATN graph, TsqlPredictionGraph budget, bool contextual)
    : ParserATNSimulator(parser, graph, graph.decisionToDFA,
        parser.Interpreter.getSharedContextCache())
{
    protected override ATNConfigSet ComputeReachSet(ATNConfigSet closure, int token, bool fullCtx)
    {
        budget.CheckBudget();
        var reach = base.ComputeReachSet(closure, token, fullCtx);
        return reach;
    }

    public override int AdaptivePredict(ITokenStream input, int decision, ParserRuleContext outerContext)
    {
        budget.CheckBudget();
        if (!contextual)
        {
            var predicted = base.AdaptivePredict(input, decision, outerContext);
            return predicted;
        }

        var state = atn.GetDecisionState(decision);
        if (state.nonGreedy || decisionToDFA[decision].IsPrecedenceDfa)
        {
            return base.AdaptivePredict(input, decision, outerContext);
        }

        // A unique first token needs no ATN simulation. Include caller lookahead
        // for alternatives that can finish without consuming a token.
        var token = input.LA(1);
        var alternative = 0;
        bool? matchesFollow = null;
        for (var index = 0; index < state.NumberOfTransitions; index++)
        {
            var transition = state.Transition(index);
            if (transition.TransitionType != TransitionType.EPSILON)
            {
                alternative = 0;
                break;
            }

            var first = atn.NextTokens(transition.target);
            var matches = first.Contains(token);
            if (!matches && first.Contains(TokenConstants.EPSILON))
            {
                matchesFollow ??= atn.GetExpectedTokens(atn.ruleToStopState[state.ruleIndex].stateNumber, outerContext)
                    .Contains(token);
                matches = matchesFollow.Value;
            }

            if (!matches)
            {
                continue;
            }

            if (alternative != 0)
            {
                alternative = 0;
                break;
            }

            alternative = index + 1;
        }

        if (alternative != 0)
        {
            return alternative;
        }

        // With this grammar, starting without the caller stack can explore distant,
        // unrelated continuations before reaching a conflict. Use ANTLR's LL engine
        // with the actual caller stack for decisions that need prediction.
        var position = input.Index;
        var mark = input.Mark();
        try
        {
            this.input = input;
            startIndex = position;
            context = outerContext;
            thisDfa = decisionToDFA[decision];
            var start = ComputeStartState(state, outerContext, fullCtx: true);
            var result = ExecATNWithFullContext(thisDfa, new DFAState(start), start, input, position, outerContext);
            return result;
        }
        finally
        {
            mergeCache = null;
            thisDfa = null;
            input.Seek(position);
            input.Release(mark);
        }
    }
}
