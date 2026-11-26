namespace Zea.SqlFormat.Postgres;

internal static partial class PostgresFormatter
{
    private enum FrameKind { Script, Query, Expression, Arguments, Array, Definition, Case, Predicate }
    private readonly record struct LayoutFrame(FrameKind Kind, int First, int End, int Indent, int Open = -1, int Close = -1);
    private enum StatementKind { Other, Query, Insert, Update, Delete, Merge, Create, Routine }
    private enum Clause
    {
        None, Cte, Projection, From, JoinPredicate, Predicate, Group, Order,
        Values, Assignments, Returning, Definition, Conflict, MergeCondition, MergeAction
    }
    private enum PredicatePart { Ordinary, BetweenLower }

    private sealed class StatementLayout
    {
        internal StatementKind Kind;
        internal StatementKind RootKind;
        internal Clause Clause;
        internal PredicatePart Predicate;
        internal int ListIndent;
        internal int PredicateIndent;
        internal int JoinEnd = -1;
        internal int First;
        internal bool AtStart = true;
        internal bool AfterThen;

        internal StatementLayout(int indent, FrameKind frame)
        {
            Clause = frame switch
            {
                FrameKind.Definition => Clause.Definition,
                FrameKind.Predicate => Clause.Predicate,
                _ => Clause.None
            };
            ListIndent = frame == FrameKind.Definition ? indent : indent + 1;
            PredicateIndent = indent + 1;
        }
    }

    private static GapChoice[] Plan(Document document)
    {
        var choices = new GapChoice[document.Gaps.Length];
        for (int index = 0; index < choices.Length; index++)
        {
            choices[index] = Spacing(document, index);
        }

        var cases = new int[document.Atoms.Length];
        Array.Fill(cases, -1);
        var caseStack = new Stack<int>();
        for (int index = 0; index < document.Atoms.Length; index++)
        {
            if (document.Is(index, "CASE") && KeywordPosition(document, index, excludeAlias: true))
            {
                caseStack.Push(index);
            }
            else if (document.Is(index, "END") && KeywordPosition(document, index, excludeAlias: true) && caseStack.Count > 0)
            {
                cases[caseStack.Pop()] = index;
            }
        }

        var frames = new Stack<LayoutFrame>();
        frames.Push(new LayoutFrame(FrameKind.Script, 0, document.Atoms.Length, 0));
        while (frames.Count > 0)
        {
            LayoutFrame frame = frames.Pop();
            PlanFrame(document, choices, cases, frames, frame);
        }

        for (int index = 0; index < choices.Length; index++)
        {
            if (document.Gaps[index].Compact)
            {
                choices[index] = new GapChoice(document.Gaps[index].Rule == GapRule.Separator ? BreakKind.Space : BreakKind.Empty);
            }
        }

        return choices;
    }

    private static void PlanFrame(Document document, GapChoice[] choices, int[] cases, Stack<LayoutFrame> frames, LayoutFrame frame)
    {
        int first = FirstCode(document, frame.First, frame.End);
        if (first >= frame.End)
        {
            return;
        }

        var state = new StatementLayout(frame.Indent, frame.Kind);
        bool clauses = frame.Kind is FrameKind.Script or FrameKind.Query or FrameKind.Predicate;
        if (frame.Kind is FrameKind.Query or FrameKind.Definition)
        {
            Line(choices, first, frame.Indent);
            Line(choices, frame.Close, Math.Max(0, frame.Indent - 1));
        }
        else if (frame.Kind == FrameKind.Case)
        {
            Line(choices, frame.Close, frame.Indent);
        }

        for (int index = first; index < frame.End; index++)
        {
            Atom atom = document.Atoms[index];
            if (atom.IsComment)
            {
                continue;
            }

            string key = atom.Key;
            int previous = document.PreviousCode[index];
            int next = document.NextCode[index];

            if (state.AtStart)
            {
                if (index != first)
                {
                    Line(choices, index, frame.Indent);
                }
                state.Kind = StatementFor(key);
                state.RootKind = state.Kind;
                state.First = index;
                state.AtStart = false;
            }

            if (key == "CASE" && cases[index] > index && cases[index] < frame.End)
            {
                int close = cases[index];
                frames.Push(new LayoutFrame(FrameKind.Case, index + 1, close,
                    frame.Indent + (frame.Kind == FrameKind.Case ? 1 : 0), index, close));
                index = close;
                continue;
            }

            if (key is "(" or "[")
            {
                int close = document.MatchingDelimiter[index];
                FrameKind kind = GroupKind(document, index, close, state, frame);
                int indent = frame.Indent + (frame.Kind == FrameKind.Case ? 1 : 0)
                    + (kind is FrameKind.Query or FrameKind.Definition or FrameKind.Predicate ? 1 : 0);
                frames.Push(new LayoutFrame(kind, index + 1, close, indent, index, close));
                index = close;
                continue;
            }

            if (frame.Kind == FrameKind.Case)
            {
                if (key is "WHEN" or "ELSE" && KeywordPosition(document, index, excludeAlias: true))
                {
                    Line(choices, index, frame.Indent + 1);
                }
                continue;
            }

            if (key == ";")
            {
                state = new StatementLayout(frame.Indent, frame.Kind);
                continue;
            }

            if (key == ",")
            {
                if (state.Clause is Clause.Cte or Clause.Projection or Clause.From or Clause.Group or Clause.Order
                    or Clause.Values or Clause.Assignments or Clause.Returning or Clause.Definition)
                {
                    Line(choices, index, state.ListIndent);
                    if (index + 1 < choices.Length)
                    {
                        choices[index + 1] = new GapChoice(BreakKind.Empty);
                    }
                }
                continue;
            }

            if (!clauses || atom.Kind != Kind.Word || !KeywordPosition(document, index,
                excludeAlias: state.Clause is Clause.Projection or Clause.Returning))
            {
                continue;
            }

            if (key == "WITH" && CanStartQuery(document, state, index, first))
            {
                state.Clause = Clause.Cte;
                state.ListIndent = frame.Indent;
                state.Kind = StatementKind.Query;
            }
            else if (state.Kind == StatementKind.Create && key is "FUNCTION" or "PROCEDURE")
            {
                state.Kind = StatementKind.Routine;
            }
            else if (state.Kind == StatementKind.Routine && key is "RETURNS" or "LANGUAGE" or "AS")
            {
                Line(choices, index, frame.Indent);
            }
            else if (key == "SELECT" && CanStartQuery(document, state, index, first))
            {
                if (index != first)
                {
                    Line(choices, index, frame.Indent);
                }
                state.Kind = StatementKind.Query;
                SetClause(state, Clause.Projection, frame.Indent);
            }
            else if (state.Clause == Clause.Cte && key is "INSERT" or "UPDATE" or "DELETE" or "MERGE")
            {
                Line(choices, index, frame.Indent);
                state.Kind = StatementFor(key);
                state.RootKind = state.Kind;
                SetClause(state, Clause.None, frame.Indent);
            }
            else if (state.Kind == StatementKind.Merge && key == "WHEN")
            {
                Line(choices, index, frame.Indent);
                SetClause(state, Clause.MergeCondition, frame.Indent);
                state.AfterThen = false;
            }
            else if (state.Kind == StatementKind.Merge && key == "THEN")
            {
                state.AfterThen = true;
            }
            else if (state.Kind == StatementKind.Merge && state.AfterThen)
            {
                Line(choices, index, frame.Indent + 1);
                SetClause(state, Clause.MergeAction, frame.Indent + 1);
                state.AfterThen = false;
            }
            else if (key == "RETURNING" && state.RootKind is StatementKind.Insert or StatementKind.Update or StatementKind.Delete or StatementKind.Merge)
            {
                Line(choices, index, frame.Indent);
                SetClause(state, Clause.Returning, frame.Indent);
            }
            else if (key == "ON" && document.Is(next, "CONFLICT") && state.RootKind == StatementKind.Insert)
            {
                Line(choices, index, frame.Indent);
                state.Kind = StatementKind.Insert;
                SetClause(state, Clause.Conflict, frame.Indent);
            }
            else if (key == "DO" && state.Clause == Clause.Conflict)
            {
                Line(choices, index, frame.Indent + 1);
            }
            else if (key == "SET" && state.Kind == StatementKind.Update)
            {
                Line(choices, index, frame.Indent);
                SetClause(state, Clause.Assignments, frame.Indent);
            }
            else if (key == "SET" && state.Kind == StatementKind.Merge && state.Clause == Clause.MergeAction)
            {
                SetClause(state, Clause.Assignments, frame.Indent + 1);
            }
            else if (key == "SET" && state.Kind == StatementKind.Insert && state.Clause == Clause.Conflict)
            {
                Line(choices, index, frame.Indent + 1);
                SetClause(state, Clause.Assignments, frame.Indent + 1);
            }
            else if (key == "VALUES" && state.Kind is StatementKind.Query or StatementKind.Insert or StatementKind.Other or StatementKind.Merge)
            {
                if (index != first)
                {
                    Line(choices, index, frame.Indent + (state.Kind == StatementKind.Merge ? 1 : 0));
                }
                SetClause(state, Clause.Values, frame.Indent + (state.Kind == StatementKind.Merge ? 1 : 0));
            }
            else if (key == "FROM" && state.Kind is StatementKind.Query or StatementKind.Update)
            {
                Line(choices, index, frame.Indent);
                SetClause(state, Clause.From, frame.Indent);
                state.JoinEnd = -1;
            }
            else if (key == "USING" && state.Kind is StatementKind.Delete or StatementKind.Merge)
            {
                Line(choices, index, frame.Indent);
                SetClause(state, Clause.From, frame.Indent);
            }
            else if (key == "WHERE" && state.Kind is StatementKind.Query or StatementKind.Update or StatementKind.Delete or StatementKind.Insert)
            {
                Line(choices, index, frame.Indent);
                SetClause(state, Clause.Predicate, frame.Indent);
            }
            else if (state.Kind == StatementKind.Query && (key is "GROUP" or "ORDER") && document.Is(next, "BY"))
            {
                Line(choices, index, frame.Indent);
                SetClause(state, key == "GROUP" ? Clause.Group : Clause.Order, frame.Indent);
            }
            else if (state.Kind == StatementKind.Query && key == "HAVING")
            {
                Line(choices, index, frame.Indent);
                SetClause(state, Clause.Predicate, frame.Indent);
            }
            else if (state.Kind == StatementKind.Query && (key is "LIMIT" or "OFFSET" or "WINDOW" or "FETCH"
                || key == "FOR" && document.Atoms[next < frame.End ? next : index].Key is "UPDATE" or "SHARE" or "NO" or "KEY"))
            {
                Line(choices, index, frame.Indent);
                SetClause(state, Clause.None, frame.Indent);
            }
            else if (state.Kind == StatementKind.Query && key is "UNION" or "INTERSECT" or "EXCEPT")
            {
                Line(choices, index, frame.Indent);
                SetClause(state, Clause.None, frame.Indent);
            }
            else if (state.Kind == StatementKind.Query && key == "INTO")
            {
                Line(choices, index, frame.Indent);
                SetClause(state, Clause.None, frame.Indent);
            }
            else if (state.Clause is Clause.From or Clause.JoinPredicate && index > state.JoinEnd
                && JoinEnd(document, index, frame.End) is int joinEnd && joinEnd >= index)
            {
                Line(choices, index, frame.Indent);
                SetClause(state, Clause.From, frame.Indent);
                state.JoinEnd = joinEnd;
            }
            else if (key == "ON" && (state.Clause == Clause.From && state.JoinEnd >= 0 || state.Kind == StatementKind.Merge))
            {
                Line(choices, index, frame.Indent + (state.Kind == StatementKind.Merge ? 0 : 1));
                SetClause(state, Clause.JoinPredicate, frame.Indent + (state.Kind == StatementKind.Merge ? 0 : 1));
            }
            else if (state.Clause is Clause.Predicate or Clause.JoinPredicate or Clause.MergeCondition)
            {
                if (key == "BETWEEN")
                {
                    state.Predicate = PredicatePart.BetweenLower;
                }
                else if (key == "AND" && state.Predicate == PredicatePart.BetweenLower)
                {
                    state.Predicate = PredicatePart.Ordinary;
                }
                else if (key is "AND" or "OR")
                {
                    Line(choices, index, state.PredicateIndent);
                }
            }
        }
    }

    private static StatementKind StatementFor(string key) => key switch
    {
        "SELECT" or "WITH" or "VALUES" => StatementKind.Query,
        "INSERT" => StatementKind.Insert,
        "UPDATE" => StatementKind.Update,
        "DELETE" => StatementKind.Delete,
        "MERGE" => StatementKind.Merge,
        "CREATE" => StatementKind.Create,
        "DO" => StatementKind.Routine,
        _ => StatementKind.Other
    };

    private static bool KeywordPosition(Document document, int index, bool excludeAlias)
    {
        int previous = document.PreviousCode[index];
        return previous < 0 || document.Atoms[previous].Key is not ("." or ":")
            && (!excludeAlias || !document.Is(previous, "AS"));
    }

    private static bool CanStartQuery(Document document, StatementLayout state, int index, int first)
        => index == first || state.Kind is StatementKind.Query or StatementKind.Insert || state.Clause == Clause.Cte
            || state.Kind == StatementKind.Create && document.Is(document.PreviousCode[index], "AS")
            || document.Atoms[state.First].Key is "EXPLAIN" or "PREPARE" or "DECLARE";

    private static void SetClause(StatementLayout state, Clause clause, int indent)
    {
        state.Clause = clause;
        state.ListIndent = indent + 1;
        state.PredicateIndent = indent + 1;
        state.Predicate = PredicatePart.Ordinary;
    }

    private static FrameKind GroupKind(Document document, int open, int close, StatementLayout state, LayoutFrame parent)
    {
        if (document.Is(open, "["))
        {
            return FrameKind.Array;
        }

        int first = FirstCode(document, open + 1, close);
        if (first < close && document.Atoms[first].Key is "SELECT" or "WITH" or "VALUES" or "INSERT" or "UPDATE" or "DELETE" or "MERGE")
        {
            return FrameKind.Query;
        }

        if (state.Kind == StatementKind.Create && parent.Kind is FrameKind.Script or FrameKind.Query)
        {
            bool definition = false;
            for (int index = state.First; index < open; index++)
            {
                string key = document.Atoms[index].Key;
                if (key == ";")
                {
                    definition = false;
                }
                else if (key is "TABLE" or "TYPE")
                {
                    definition = true;
                }
                else if (key is "AS" or "INDEX" or "FUNCTION" or "PROCEDURE")
                {
                    definition = false;
                }
                if (document.MatchingDelimiter[index] > index)
                {
                    index = document.MatchingDelimiter[index];
                }
            }

            if (definition)
            {
                return FrameKind.Definition;
            }
        }

        int previous = document.PreviousCode[open];
        if (state.Clause is Clause.Predicate or Clause.JoinPredicate && previous >= 0
            && document.Atoms[previous].Key is "(" or "AND" or "OR" or "NOT" or "WHERE" or "HAVING" or "ON")
        {
            return FrameKind.Predicate;
        }
        return previous >= 0 && (document.Atoms[previous].Kind is Kind.Word or Kind.QuotedName || document.Is(previous, ")"))
            ? FrameKind.Arguments : FrameKind.Expression;
    }

    private static int JoinEnd(Document document, int first, int end)
    {
        int index = first;
        for (int count = 0; count < 4 && index < end; count++, index = document.NextCode[index])
        {
            string key = document.Atoms[index].Key;
            if (key == "JOIN")
            {
                return index;
            }
            if (key is not ("LEFT" or "RIGHT" or "FULL" or "INNER" or "OUTER" or "CROSS" or "NATURAL"))
            {
                break;
            }
        }
        return -1;
    }

    private static int FirstCode(Document document, int first, int end)
        => first < end && document.Atoms[first].IsComment ? document.NextCode[first] : first;

    private static void Line(GapChoice[] choices, int index, int indent)
    {
        if (index >= 0 && index < choices.Length)
        {
            choices[index] = new GapChoice(BreakKind.Line, indent);
        }
    }

    private static GapChoice Spacing(Document document, int gap)
    {
        if (gap == 0 || gap == document.Atoms.Length)
        {
            return new GapChoice(BreakKind.Keep);
        }

        Atom left = document.Atoms[gap - 1];
        Atom right = document.Atoms[gap];
        if (left.IsComment || right.IsComment)
        {
            return new GapChoice(BreakKind.Keep);
        }
        if (right.Key is "," or ";" or ")" or "]" || left.Key is "(" or "["
            || left.Key is "." or "::" or ":" || right.Key is "." or "::" or ":" or "[")
        {
            return new GapChoice(BreakKind.Empty);
        }
        if (right.Key == "(")
        {
            bool space = left.Kind is not (Kind.Word or Kind.QuotedName) && left.Key is not (")" or "]")
                || left.Key is "AS" or "IN" or "EXISTS" or "ON" or "USING" or "OVER" or "FILTER" or "FROM"
                    or "JOIN" or "WHERE" or "AND" or "OR" or "NOT" or "BY" or "SELECT" or "VALUES" or "SET"
                    or "RETURNING" or "WHEN" or "THEN" or "ELSE" or "WITH" or "MATERIALIZED" or "ANY" or "ALL" or "SOME";
            return new GapChoice(space ? BreakKind.Space : BreakKind.Empty);
        }
        if (left.Key is "+" or "-" && UnarySign(document, gap - 1))
        {
            return new GapChoice(BreakKind.Empty);
        }
        return new GapChoice(BreakKind.Space);
    }

    private static bool UnarySign(Document document, int index)
    {
        int previous = document.PreviousCode[index];
        return previous < 0 || document.Atoms[previous].Kind == Kind.Operator
            || document.Atoms[previous].Key is "(" or "[" or "," or ";" or "SELECT" or "WHERE" or "WHEN" or "THEN"
                or "ELSE" or "AND" or "OR" or "NOT" or "BETWEEN" or "VALUES" or "SET" or "RETURNING" or "BY";
    }
}
