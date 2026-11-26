/*
Poor Man's T-SQL Formatter - a small free Transact-SQL formatting 
library for .Net 2.0 and JS, written in C#. 
Copyright (C) 2011-2017 Tao Klerks

Additional Contributors:
 * Timothy Klenke, 2012

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Zea.SqlFormat.Formatters.RightWay
{
    internal sealed partial class RightWayFormatter
    {
        // ----------------------------------------------------------------
        // WrapOverflowingAliasLiterals: reach a wrap fixed point for post-pass-lengthened lines
        // ----------------------------------------------------------------

        // Matches a line of the form  <indent><lhs> = <lone-literal>[,;]  where <lhs> contains
        // no quote (so the '=' is the alias separator, never one inside a string) and the value
        // is a single string/binary literal. Group 3 is the literal; group 4 an optional
        // trailing separator.
        private static readonly Regex _aliasLiteralLine = new Regex(
            @"^(\s*)([^']*?)\s=\s(N?'(?:[^']|'')*'|0[xX][0-9A-Fa-f]+)([,;]?)\s*$",
            RegexOptions.None);

        /// <summary>
        /// The alias/align post-passes assemble `alias = value` lines AFTER the core tree walk
        /// has already made its max-line-width wrap decisions, so a line the passes lengthen
        /// (by inserting `ColumnAlias_N = `) can end up past MaxLineWidth without the wrap that
        /// the NEXT format pass would apply — the raw→formatted idempotency transient. When the
        /// value is a single literal token, the core would break immediately before it (the
        /// literal is unbreakable), putting it on its own line at the column indent. This pass
        /// reproduces that break here, so pass 1 already emits pass 2's layout. Padding and
        /// word-separator spaces are NOT part of the core's length accounting, so the threshold
        /// is measured from the trimmed LHS exactly as the core measures it.
        /// </summary>
        private string WrapOverflowingAliasLiterals(string output)
        {
            if (Options.MaxLineWidth <= 0) return output;
            var lines = SplitLinesPreservingEndings(output, out var endings);
            bool[] insideStringOrComment = ComputeLinesInsideStringOrComment(lines);
            var outLines = new List<string>(lines.Count);
            var outEndings = new List<string>(lines.Count);
            bool changed = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var m = LineTouchesStringOrComment(insideStringOrComment, i)
                    ? System.Text.RegularExpressions.Match.Empty
                    : _aliasLiteralLine.Match(lines[i]);
                if (m.Success)
                {
                    string indent = m.Groups[1].Value;
                    string lhsTrimmed = m.Groups[2].Value.TrimEnd();
                    string literal = m.Groups[3].Value;
                    // Core length just before the literal token: indent + LHS tokens + the '='
                    // operator. Separator spaces around '=' and any alignment padding are not
                    // counted by the core, so exclude them here too.
                    int coreLenBeforeValue = indent.Length + lhsTrimmed.Length + 1;
                    if (literal.Length + coreLenBeforeValue > Options.MaxLineWidth)
                    {
                        // Split: line 1 keeps the prefix through "=" (alignment padding before
                        // "=" preserved). The separator space before the wrapped value is dropped
                        // — the core strips it at a width break too (see AddOutputContent), so
                        // keeping it would disagree with the next pass (trailing-space drift).
                        outLines.Add(lines[i].Substring(0, m.Groups[3].Index).TrimEnd());
                        outEndings.Add(Environment.NewLine);
                        outLines.Add(indent + literal + m.Groups[4].Value);
                        outEndings.Add(endings[i]);
                        changed = true;
                        continue;
                    }
                }
                outLines.Add(lines[i]);
                outEndings.Add(endings[i]);
            }
            return changed ? JoinLinesPreservingEndings(outLines, outEndings) : output;
        }

        // ----------------------------------------------------------------
        // EnsureColumnAliases: add AS alias to every SELECT column that lacks one
        // ----------------------------------------------------------------

        /// <summary>
        /// Walks the formatted output and ensures every SELECT-list column has an explicit alias.
        /// - Simple column references (optionally table-qualified, bracket-quoted) → base column name.
        /// - Complex expressions (functions, arithmetic, CASE, wildcards) → ColumnAlias_N.
        /// N resets to 1 for each new query (each new top-level SELECT block).
        /// Columns that already have a top-level AS alias are left unchanged.
        /// </summary>
        private string EnsureColumnAliases(string output)
        {
            var lines = SplitLinesPreservingEndings(output, out var lineEndings);
            bool inSelectList = false;
            int autoAliasCounter = 0;
            int beginDepth = 0; // tracks BEGIN/END nesting depth
            int parenDepth = 0; // tracks open-paren depth inside a SELECT column expression
            int caseDepth = 0;  // tracks open CASE...END depth — CASE arms wrapped onto
                                // their own lines (AND/THEN/ELSE fragments) are expression
                                // continuations, never new columns
            bool[] insideStringOrComment = ComputeLinesInsideStringOrComment(lines);

            for (int i = 0; i < lines.Count; i++)
            {
                //never touch lines inside OR opening a multi-line string literal / block comment
                if (LineTouchesStringOrComment(insideStringOrComment, i)) { inSelectList = false; parenDepth = 0; caseDepth = 0; continue; }
                string line = lines[i];
                string trimmed = line.TrimStart();
                string trimmedUpper = trimmed.ToUpperInvariant();

                // Track BEGIN/END depth to avoid misidentifying block body lines as SELECT columns.
                // A bare BEGIN (not BEGIN TRANSACTION) increments depth; END decrements.
                if (trimmedUpper == "BEGIN" || trimmedUpper.StartsWith("BEGIN ") || trimmedUpper.StartsWith("BEGIN\t"))
                {
                    // Only count BEGIN that starts a block — not BEGIN TRANSACTION / BEGIN TRAN
                    string afterBegin = trimmedUpper.Length > 5 ? trimmedUpper.Substring(6).TrimStart() : "";
                    if (!afterBegin.StartsWith("TRAN") && !afterBegin.StartsWith("DISTRIBUTED"))
                        beginDepth++;
                    if (inSelectList) { inSelectList = false; parenDepth = 0; caseDepth = 0; } // BEGIN always ends a SELECT list
                    continue;
                }
                if (trimmedUpper == "END" || trimmedUpper.StartsWith("END ") || trimmedUpper.StartsWith("END\t")
                    || trimmedUpper.StartsWith("END;") || trimmedUpper.StartsWith("END,") || trimmedUpper.StartsWith("END)"))
                {
                    if (beginDepth > 0) beginDepth--;
                    if (inSelectList) { inSelectList = false; parenDepth = 0; caseDepth = 0; }
                    continue;
                }

                // If inside a BEGIN/END block, don't process as SELECT column lines.
                if (beginDepth > 0)
                {
                    // Nested SELECT inside a block: handle separately.
                    // Allow SELECT detection to reset inSelectList for nested queries.
                    if (trimmedUpper.StartsWith("SELECT ") || trimmedUpper == "SELECT")
                    {
                        inSelectList = true;
                        autoAliasCounter = 0;
                        parenDepth = 0;
                        caseDepth = 0;
                        int selectEnd = trimmed.IndexOf(' ');
                        if (selectEnd >= 0)
                        {
                            string afterSelect = trimmed.Substring(selectEnd + 1).TrimStart();
                            var (modPrefix1, colPart1, modOnly1) = StripSelectModifierPrefix(afterSelect);
                            if (!modOnly1)
                            {
                                string indent = line.Substring(0, line.Length - trimmed.Length);
                                bool hadTrailingComma1 = colPart1.EndsWith(",");
                                string colExpr1 = hadTrailingComma1 ? colPart1.Substring(0, colPart1.Length - 1).TrimEnd() : colPart1;
                                int netParens1 = CountNetParens(colExpr1);
                                //only alias when the inline expression is balanced (not the start
                                // of a multi-line expression such as ROW_NUMBER() OVER ( )
                                if (netParens1 <= 0 && CountNetCase(colExpr1) <= 0)
                                {
                                    var (rewritten, newCounter) = EnsureAlias(colExpr1, autoAliasCounter);
                                    autoAliasCounter = newCounter;
                                    string finalCol1 = rewritten + (hadTrailingComma1 ? "," : "");
                                    if (finalCol1 != colPart1)
                                        lines[i] = indent + "SELECT " + modPrefix1 + finalCol1;
                                }
                                parenDepth += netParens1;
                                if (parenDepth < 0) parenDepth = 0;
                                caseDepth += CountNetCase(colExpr1);
                                if (caseDepth < 0) caseDepth = 0;
                            }
                        }
                        continue;
                    }
                    // Non-SELECT lines inside a block: if we're tracking a SELECT list, process them;
                    // otherwise skip.
                    if (!inSelectList) continue;
                }

                // --- Detect start of SELECT list ---
                if (trimmedUpper.StartsWith("SELECT ") || trimmedUpper == "SELECT")
                {
                    inSelectList = true;
                    autoAliasCounter = 0; // reset counter per query
                    parenDepth = 0;
                    caseDepth = 0;

                    // The SELECT line itself may have first column inline.
                    int selectEnd = trimmed.IndexOf(' ');
                    if (selectEnd >= 0)
                    {
                        string afterSelect = trimmed.Substring(selectEnd + 1).TrimStart();
                        // Strip SELECT modifiers (TOP N / DISTINCT) before alias processing
                        var (modPrefix1, colPart1, modOnly1) = StripSelectModifierPrefix(afterSelect);
                        if (!modOnly1)
                        {
                            string indent = line.Substring(0, line.Length - trimmed.Length);
                            bool hadTrailingComma1 = colPart1.EndsWith(",");
                            string colExpr1 = hadTrailingComma1 ? colPart1.Substring(0, colPart1.Length - 1).TrimEnd() : colPart1;
                            int netParens1 = CountNetParens(colExpr1);
                            //only alias when the inline expression is balanced (not the start
                            // of a multi-line expression such as ROW_NUMBER() OVER ( )
                            if (netParens1 <= 0 && CountNetCase(colExpr1) <= 0)
                            {
                                var (rewritten, newCounter) = EnsureAlias(colExpr1, autoAliasCounter);
                                autoAliasCounter = newCounter;
                                string finalCol1 = rewritten + (hadTrailingComma1 ? "," : "");
                                if (finalCol1 != colPart1)
                                    lines[i] = indent + "SELECT " + modPrefix1 + finalCol1;
                            }
                            parenDepth += netParens1;
                            if (parenDepth < 0) parenDepth = 0;
                        }
                    }
                    continue;
                }

                // --- Detect end of SELECT list ---
                if (inSelectList)
                {
                    //the formatter never emits a blank line INSIDE a column list - a blank
                    // line means the statement ended (guards against the tracker running on
                    // into OPEN/DECLARE/etc. and column-izing arbitrary statements)
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        inSelectList = false;
                        parenDepth = 0;
                        caseDepth = 0;
                        continue;
                    }
                    // If we're inside an unbalanced open-paren or open CASE (multi-line
                    // expression continuation), just track depths and skip alias processing.
                    if (parenDepth > 0 || caseDepth > 0)
                    {
                        parenDepth += CountNetParens(trimmed);
                        if (parenDepth < 0) parenDepth = 0;
                        caseDepth += CountNetCase(trimmed);
                        if (caseDepth < 0) caseDepth = 0;
                        continue;
                    }

                    bool startsWithComma = trimmed.StartsWith(",");
                    // The previous column line ended with a trailing binary operator (an
                    // alias RHS wrapped: "FieldList =" then "'...'", or a concat "... +"
                    // then "N'...'"). This line is that wrapped operand, not a new column.
                    if (!startsWithComma && !IsClauseStartLine(trimmedUpper)
                        && PrevColumnLineEndsOpen(lines, i))
                    {
                        parenDepth += CountNetParens(trimmed);
                        if (parenDepth < 0) parenDepth = 0;
                        caseDepth += CountNetCase(trimmed);
                        if (caseDepth < 0) caseDepth = 0;
                        continue;
                    }
                    // Width-wrapped continuations of a taller expression (boolean AND/OR,
                    // CASE-arm keywords, operator-leading fragments) are not new columns.
                    if (!startsWithComma
                        && (trimmedUpper.StartsWith("AND ") || trimmedUpper.StartsWith("OR ")
                            || trimmedUpper.StartsWith("THEN ") || trimmedUpper.StartsWith("WHEN ")
                            || trimmedUpper.StartsWith("ELSE ") || trimmedUpper.StartsWith("AS ")
                            || "+-*/%=<>".IndexOf(trimmed[0]) >= 0
                            // a line opening with '(' (but not a scalar subquery) is a wrapped
                            // operand — e.g. a function call split "quotename \n (ObjectName)"
                            || (trimmed[0] == '(' && !trimmedUpper.StartsWith("(SELECT"))))
                    {
                        parenDepth += CountNetParens(trimmed);
                        if (parenDepth < 0) parenDepth = 0;
                        caseDepth += CountNetCase(trimmed);
                        if (caseDepth < 0) caseDepth = 0;
                        continue;
                    }
                    if (!startsWithComma && IsClauseStartLine(trimmedUpper))
                    {
                        inSelectList = false;
                        continue;
                    }

                    if (startsWithComma)
                    {
                        // Leading-comma style: ,expr  or  ,expr AS alias
                        string afterComma = trimmed.Substring(1).TrimStart();
                        int netParens = CountNetParens(afterComma);
                        int netCase = CountNetCase(afterComma);
                        // Only alias if the expression is balanced (not a multi-line expression start)
                        // and its own AS-alias isn't waiting on the next line (wrapped "... \n AS x").
                        if (netParens <= 0 && netCase <= 0 && !NextColumnLineIsAsAlias(lines, i))
                        {
                            var (rewritten, newCounter) = EnsureAlias(afterComma, autoAliasCounter);
                            autoAliasCounter = newCounter;
                            if (rewritten != afterComma)
                            {
                                string indent = line.Substring(0, line.Length - trimmed.Length);
                                lines[i] = indent + "," + rewritten;
                            }
                        }
                        // Track net parens/CASE for multi-line expression detection
                        parenDepth += netParens;
                        if (parenDepth < 0) parenDepth = 0;
                        caseDepth += netCase;
                        if (caseDepth < 0) caseDepth = 0;
                    }
                    else if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        bool hadTrailingComma = trimmed.EndsWith(",");
                        var (modPrefix2, colExpr2, modOnly2) = StripSelectModifierPrefix(trimmed.TrimEnd(','));
                        if (modOnly2)
                            continue; // pure TOP N or DISTINCT line — not a column, leave as-is

                        int netParens2 = CountNetParens(colExpr2);
                        int netCase2 = CountNetCase(colExpr2);
                        if (netParens2 <= 0 && netCase2 <= 0 && !NextColumnLineIsAsAlias(lines, i))
                        {
                            var (rewritten, newCounter) = EnsureAlias(colExpr2, autoAliasCounter);
                            autoAliasCounter = newCounter;
                            string indent2 = line.Substring(0, line.Length - trimmed.Length);
                            if (rewritten != colExpr2 || modPrefix2.Length > 0)
                                lines[i] = indent2 + modPrefix2 + rewritten + (hadTrailingComma ? "," : "");
                            parenDepth += netParens2;
                        }
                        else
                        {
                            parenDepth += netParens2;
                        }
                        if (parenDepth < 0) parenDepth = 0;
                        caseDepth += netCase2;
                        if (caseDepth < 0) caseDepth = 0;
                    }
                }
            }

            return JoinLinesPreservingEndings(lines, lineEndings);
        }

        /// <summary>
        /// Counts net open parens in a string (open minus close), ignoring parens inside string literals.
        /// Used to detect multi-line expression continuation.
        /// </summary>
        private static int CountNetParens(string s)
        {
            int depth = 0;
            bool inSingleQuote = false;
            bool inBracket = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inSingleQuote)
                {
                    if (c == '\'' && (i + 1 < s.Length && s[i + 1] == '\'')) { i++; continue; } // escaped quote
                    if (c == '\'') inSingleQuote = false;
                    continue;
                }
                if (inBracket)
                {
                    if (c == ']') inBracket = false;
                    continue;
                }
                if (c == '-' && i + 1 < s.Length && s[i + 1] == '-') break; //-- comment
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0) break; //comment continues past end of line
                    i = close + 1;
                    continue;
                }
                switch (c)
                {
                    case '\'' : inSingleQuote = true; break;
                    case '[': inBracket = true; break;
                    case '(': depth++; break;
                    case ')': depth--; break;
                }
            }
            return depth;
        }

        // Counts unmatched CASE keywords minus END keywords at the top paren level (outside
        // strings/brackets/parens). Positive means the expression opens a CASE that continues
        // on subsequent lines; used to skip EnsureAlias on the opening CASE token.
        private static int CountNetCase(string s)
        {
            int net = 0;
            int parenDepth = 0;
            bool inSingleQuote = false;
            bool inBracket = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inSingleQuote)
                {
                    if (c == '\'' && i + 1 < s.Length && s[i + 1] == '\'') { i++; continue; }
                    if (c == '\'') inSingleQuote = false;
                    continue;
                }
                if (inBracket) { if (c == ']') inBracket = false; continue; }
                if (c == '\'') { inSingleQuote = true; continue; }
                if (c == '[') { inBracket = true; continue; }
                if (c == '-' && i + 1 < s.Length && s[i + 1] == '-') break; //-- comment
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0) break; //comment continues past end of line
                    i = close + 1;
                    continue;
                }
                if (c == '(') { parenDepth++; continue; }
                if (c == ')') { parenDepth--; continue; }
                if (parenDepth > 0) continue;
                bool prevIsWord = i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_');
                if (prevIsWord) continue;
                if (i + 4 <= s.Length
                    && string.Compare(s, i, "CASE", 0, 4, StringComparison.OrdinalIgnoreCase) == 0
                    && (i + 4 >= s.Length || !(char.IsLetterOrDigit(s[i + 4]) || s[i + 4] == '_')))
                {
                    net++;
                    i += 3;
                }
                else if (i + 3 <= s.Length
                    && string.Compare(s, i, "END", 0, 3, StringComparison.OrdinalIgnoreCase) == 0
                    && (i + 3 >= s.Length || !(char.IsLetterOrDigit(s[i + 3]) || s[i + 3] == '_')))
                {
                    net--;
                    i += 2;
                }
            }
            return net;
        }

        /// <summary>
        /// Returns a copy of <paramref name="col"/> with an AS alias appended if none exists.
        /// Also returns the updated autoAliasCounter.
        /// - Leaves unchanged if a top-level AS alias is already present.
        /// - Leaves unchanged if col is a wildcard (* or t.*).
        /// - Simple column ref (e.g. col, t.col, [col], t.[col]) → appends AS &lt;baseName&gt;.
        /// - Complex expression → appends AS ColumnAlias_N (counter incremented).
        /// </summary>
        // True when the text has a '[' with no matching ']' (outside string literals).
        // '[' and ']' don't nest; ']]' is an escaped literal ']' inside a bracket-quoted name.
        private static bool HasUnclosedBracketOutsideString(string s)
        {
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\'')
                {
                    i++; // skip the string literal (handling '' escapes)
                    while (i < s.Length)
                    {
                        if (s[i] == '\'')
                        {
                            if (i + 1 < s.Length && s[i + 1] == '\'') { i += 2; continue; }
                            i++; break;
                        }
                        i++;
                    }
                }
                else if (c == '[')
                {
                    i++;
                    bool closed = false;
                    while (i < s.Length)
                    {
                        if (s[i] == ']')
                        {
                            if (i + 1 < s.Length && s[i + 1] == ']') { i += 2; continue; } // escaped ]]
                            closed = true; i++; break;
                        }
                        i++;
                    }
                    if (!closed) return true;
                }
                else i++;
            }
            return false;
        }

        private static (string result, int counter) EnsureAlias(string col, int counter)
        {
            string trimmed = col.Trim();

            //peel a trailing -- comment off before analysis: anything appended after it
            // would be swallowed by the comment ("u.Id -- note AS ColumnAlias_1"), and the
            // invisible alias would compound on every re-format. The alias goes BEFORE the
            // comment; the comment is re-attached at the end.
            string trailingComment = "";
            int commentStart = FindLineCommentStart(trimmed);
            if (commentStart >= 0)
            {
                trailingComment = " " + trimmed.Substring(commentStart);
                trimmed = trimmed.Substring(0, commentStart).TrimEnd();
                if (trimmed.Length == 0)
                    return (col, counter); //comment-only line
            }

            //peel a trailing statement terminator off before analysis; re-appended at the
            // end so "SELECT foo;" becomes "SELECT foo AS foo;", never "foo; AS foo;"
            string suffix = "";
            while (trimmed.EndsWith(";"))
            {
                suffix = ";" + suffix;
                trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
            }

            // Empty or comment-only — skip
            if (string.IsNullOrEmpty(trimmed)
                || (trimmed.StartsWith("/*") && trimmed.IndexOf("*/") == trimmed.Length - 2)
                || trimmed.StartsWith("--"))
                return (col, counter);

            // An unclosed '[' bracket (malformed input, e.g. `SELECT 1 AS [A` with no ']')
            // makes the alias structure unparseable: the LHS/expr split is ambiguous, so
            // re-aliasing stacks a fresh alias every pass (`[A = 1` -> `[A = 1 = [A = 1` ->
            // ..., unbounded). Well-formed columns always close their brackets, so skipping
            // these leaves valid SQL untouched while making the malformed case idempotent.
            if (HasUnclosedBracketOutsideString(trimmed))
                return (col, counter);

            // Ends with a trailing binary operator (=, +, ...): an INCOMPLETE expression
            // whose operand wraps to the next line. Aliasing it here appends "AS X" after
            // the operator ("a + AS ColumnAlias_1") — invalid, and it masks the trailing
            // operator so the wrapped operand line then looks like a new column too. Leave
            // the whole multi-line expression un-aliased (valid SQL, deterministic).
            if (EndsWithTrailingBinaryOperator(trimmed))
                return (col, counter);

            // Already has AS alias?
            if (FindAsOutsideParens(trimmed) >= 0)
                return (col, counter);

            // Already has an EqualSign-style alias (alias = expr) in the input SQL.
            // The column IS aliased — preserve the user's chosen style, do not rewrite to AS.
            // A string literal on the LHS ('alias' = expr, the legacy alias syntax that
            // RewriteAliasesToEqualSign itself emits for AS 'alias') counts too — without
            // this, REformatting stacks a second alias in front and corrupts the column.
            if (!trimmed.StartsWith("@"))
            {
                int eqPos = FindEqualSignOutsideParens(trimmed);
                if (eqPos > 0)
                {
                    string lhs = trimmed.Substring(0, eqPos).Trim();
                    if (IsSimpleColumnRef(lhs) || IsStringLiteralAlias(lhs))
                        return (col, counter);
                }
            }

            // Variable assignment SELECT @var = expr — not a column alias, leave alone
            if (trimmed.StartsWith("@") && trimmed.IndexOf('=') >= 0)
                return (col, counter);

            // Wildcard — skip (a trailing /* ... */ comment doesn't make it aliasable)
            string wildcardCheck = trimmed;
            int wbc = wildcardCheck.LastIndexOf("/*", System.StringComparison.Ordinal);
            if (wbc >= 0 && wildcardCheck.IndexOf("*/", wbc, System.StringComparison.Ordinal) == wildcardCheck.Length - 2)
                wildcardCheck = wildcardCheck.Substring(0, wbc).TrimEnd();
            if (wildcardCheck == "*" || wildcardCheck.EndsWith(".*"))
                return (col, counter);

            // AS-less bracketed alias (expr [My Alias]) — the column IS aliased.
            if (EndsWithBracketedAlias(trimmed))
                return (col, counter);

            // AS-less plain alias (expr alias) — the column IS aliased.
            if (EndsWithPlainAlias(trimmed))
                return (col, counter);

            // AS-less string-literal alias (expr 'alias' / expr N'alias', legacy syntax).
            if (EndsWithStringLiteralAlias(trimmed))
                return (col, counter);

            // Determine alias. A bare @variable is not a reusable column name (SELECT @v
            // must alias to ColumnAlias_N, never "@v = @v"), so it takes the complex path.
            string alias;
            if (IsSimpleColumnRef(trimmed) && !trimmed.StartsWith("@"))
            {
                alias = ExtractColumnBaseName(trimmed);
                if (IsReservedAliasKeyword(alias))
                {
                    // A bare keyword column (SELECT NULL) can't alias to itself; a derived
                    // alias that collides with a reserved keyword (t.HASH -> HASH) must be
                    // bracketed to stay valid (HASH = t.HASH is a syntax error).
                    if (string.Equals(alias, trimmed, System.StringComparison.OrdinalIgnoreCase))
                    {
                        counter++;
                        alias = "ColumnAlias_" + counter;
                    }
                    else
                        alias = "[" + alias + "]";
                }
            }
            else
            {
                counter++;
                alias = "ColumnAlias_" + counter;
            }

            // A -- comment that directly follows a statement terminator uses the core's
            // ";--comment" spacing (no gap), so re-attaching " --comment" would drift to
            // ";--comment" on the next pass. Match the core when suffix ends with ';'.
            if (suffix.EndsWith(";") && trailingComment.StartsWith(" --"))
                trailingComment = trailingComment.Substring(1);
            return (trimmed + " AS " + alias + suffix + trailingComment, counter);
        }

        /// <summary>
        /// Finds the start of a trailing -- comment outside string literals and bracket
        /// identifiers; -1 when the line carries none.
        /// </summary>
        private static int FindLineCommentStart(string s)
        {
            bool inSingleQuote = false;
            bool inBracket = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inSingleQuote)
                {
                    if (c == '\'' && i + 1 < s.Length && s[i + 1] == '\'') { i++; continue; }
                    if (c == '\'') inSingleQuote = false;
                    continue;
                }
                if (inBracket) { if (c == ']') inBracket = false; continue; }
                if (c == '\'') { inSingleQuote = true; continue; }
                if (c == '[') { inBracket = true; continue; }
                if (c == '-' && i + 1 < s.Length && s[i + 1] == '-') return i;
            }
            return -1;
        }

        /// <summary>
        /// True when the expression ends in a standalone [bracketed] token that acts as an
        /// AS-less alias (Name [Test Case Name]) — as opposed to a qualified reference
        /// (t.[Col]), a lone bracketed column ([Col]), or a bracketed operand (a + [b]).
        /// </summary>
        private static bool EndsWithBracketedAlias(string expr)
        {
            if (!expr.EndsWith("]")) return false;
            int open = expr.LastIndexOf('[');
            if (open <= 0) return false; //no room for an expression before it (lone [Col])
            char before = expr[open - 1];
            if (before != ' ' && before != '\t') return false; //t.[Col] and glued forms
            string prefix = expr.Substring(0, open - 1).TrimEnd();
            if (prefix.Length == 0) return false;
            //the alias must FOLLOW a complete expression: identifier/bracket/paren/quote
            // endings qualify; a trailing operator (a + [b]) means [b] is an operand
            char last = prefix[prefix.Length - 1];
            return char.IsLetterOrDigit(last) || last == '_' || last == ']' || last == ')' || last == '\'';
        }

        // Keywords that, appearing immediately before the final identifier, mean that
        // identifier is an OPERAND of the keyword, not an AS-less alias:
        //   x COLLATE Latin1   (Latin1 is the collation)   a AND b   a IS NULL   x LIKE y
        private static readonly HashSet<string> _operandExpectingBeforeAlias = new HashSet<string>(
            new[] { "COLLATE", "AND", "OR", "NOT", "LIKE", "IN", "BETWEEN", "ESCAPE", "IS", "AS", "THEN", "WHEN", "ELSE", "OVER" },
            System.StringComparer.OrdinalIgnoreCase);

        // Keywords that terminate/are part of an expression, so a column ending in one is
        // NOT aliased (a trailing NULL/END/DEFAULT is expression content, not an alias name).
        private static readonly HashSet<string> _nonAliasTerminatorKeywords = new HashSet<string>(
            new[] { "NULL", "END", "DEFAULT" }, System.StringComparer.OrdinalIgnoreCase);

        private static bool IsReservedAliasKeyword(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] == '[') return false; // already bracketed
            return SqlServerFormattingKeywords.Contains(name);
        }

        /// <summary>
        /// True when a line's last non-comment character is a binary operator that requires a
        /// following operand (=, +, -, *, /, %, &amp;, |, ^, and comparison &lt;/&gt;). Such a
        /// line's expression continues onto the next physical line, so that next line is a
        /// wrapped operand, not a new SELECT column. A trailing ',' (complete column) and a
        /// closing quote/paren/identifier (complete expression) return false.
        /// </summary>
        private static bool EndsWithTrailingBinaryOperator(string line)
        {
            string s = line;
            int lc = FindLineCommentStart(s);
            if (lc >= 0) s = s.Substring(0, lc);
            // strip a trailing /* ... */ block comment (whole-line-tail only)
            int bc = s.LastIndexOf("/*", System.StringComparison.Ordinal);
            if (bc >= 0 && s.IndexOf("*/", bc, System.StringComparison.Ordinal) == s.Length - 2)
                s = s.Substring(0, bc);
            s = s.TrimEnd();
            if (s.Length == 0) return false;
            // '*' is excluded: a trailing '*' is almost always a wildcard (SELECT *, t.*),
            // not multiplication, and treating it as an operator makes the following FROM
            // look like a wrapped operand. A trailing '.' is a member-access split (AL. \n Col).
            if ("+-/%=<>&|^.".IndexOf(s[s.Length - 1]) >= 0)
                return true;
            // A trailing word operator (expr wrapped after AS/AND/OR/THEN/...) also means the
            // operand/alias lands on the next line: "... END AS" then the alias, "x =" then RHS.
            int w = s.LastIndexOfAny(new[] { ' ', '\t' });
            string lastWord = w >= 0 ? s.Substring(w + 1) : s;
            return _trailingWordOperators.Contains(lastWord);
        }

        private static readonly HashSet<string> _trailingWordOperators = new HashSet<string>(
            new[] { "AS", "AND", "OR", "NOT", "THEN", "WHEN", "ELSE", "LIKE", "IN", "BETWEEN", "ESCAPE", "OVER", "COLLATE" },
            System.StringComparer.OrdinalIgnoreCase);

        private static bool IsWholeLineComment(string trimmed)
        {
            return trimmed.StartsWith("--")
                || (trimmed.StartsWith("/*") && trimmed.TrimEnd().EndsWith("*/"));
        }

        /// <summary>
        /// True when the nearest PRECEDING non-comment column line left its expression open
        /// (ended with a trailing operator). Whole-line comments between a wrapped operator
        /// and its operand ("script =" / "/* note */" / "N'...'") are skipped; a blank line
        /// is a statement boundary and stops the search.
        /// </summary>
        private static bool PrevColumnLineEndsOpen(IList<string> lines, int i)
        {
            for (int k = i - 1; k >= 0; k--)
            {
                string t = lines[k].TrimStart();
                if (string.IsNullOrWhiteSpace(t)) return false;
                if (IsWholeLineComment(t)) continue;
                return EndsWithTrailingBinaryOperator(lines[k]);
            }
            return false;
        }

        /// <summary>
        /// True when the next non-comment line CONTINUES the current column's expression, so
        /// the current line is not a complete column and must not be aliased: an "AS alias"
        /// tail (already aliased on the next line), or a fragment leading with a continuation
        /// operator/keyword (+ / AND / THEN ... — a concat/boolean wrapped onto the next line).
        /// Stops at a blank line, a leading comma (new column), or a clause keyword.
        /// </summary>
        private static bool NextColumnLineIsAsAlias(IList<string> lines, int i)
        {
            for (int k = i + 1; k < lines.Count; k++)
            {
                string t = lines[k].TrimStart();
                if (string.IsNullOrWhiteSpace(t)) return false;
                if (IsWholeLineComment(t)) continue;
                if (t.StartsWith(",")) return false; // next is a new (leading-comma) column
                string tu = t.ToUpperInvariant();
                if (IsClauseStartLine(tu)) return false;
                return tu.StartsWith("AS ") || tu == "AS"
                    || tu.StartsWith("AND ") || tu.StartsWith("OR ") || tu.StartsWith("THEN ")
                    || tu.StartsWith("WHEN ") || tu.StartsWith("ELSE ")
                    || "+-*/%=<>&|^".IndexOf(t[0]) >= 0
                    || (t[0] == '(' && !tu.StartsWith("(SELECT")); // function-call / group split
            }
            return false;
        }

        /// <summary>
        /// True when the expression ends in a bare (non-bracketed) identifier that acts as
        /// an AS-less alias: "expr alias" where a complete expression is followed by a
        /// plain identifier — 'B' Id, count(1) Cnt, CASE ... END AvailabilityGroup,
        /// 0 i, 'x' COLLATE coll c1. Prepending "ColumnAlias_N =" or appending "AS x" to
        /// such a column produces invalid SQL (alias = expr alias2), which is exactly what
        /// EnsureColumnAliases did before this check existed. Conservative: any doubt →
        /// treat as an operand (return false) rather than risk hiding a real missing alias.
        /// </summary>
        private static bool EndsWithPlainAlias(string expr)
        {
            expr = expr.TrimEnd();
            int ws = expr.LastIndexOfAny(new[] { ' ', '\t' });
            if (ws <= 0) return false; // single token — no room for "expr alias"
            string last = expr.Substring(ws + 1);
            // the alias must be a plain identifier (bracketed handled by EndsWithBracketedAlias),
            // must not start with @ (variables aren't aliases), and must not be one of the few
            // keywords that TERMINATE/are part of an expression (NULL/END/DEFAULT) - those mean
            // the column is un-aliased. Function-name-ish keywords (VALUE, HASH, TYPE_NAME) can
            // legitimately be AS-less aliases, so they are not excluded here.
            if (last.Length == 0 || last[0] == '@'
                || !System.Text.RegularExpressions.Regex.IsMatch(last, @"^[A-Za-z_#][A-Za-z0-9_$#]*$"))
                return false;
            if (_nonAliasTerminatorKeywords.Contains(last))
                return false;
            string prefix = expr.Substring(0, ws).TrimEnd();
            if (prefix.Length == 0) return false;
            // the alias must FOLLOW a complete expression: identifier/bracket/paren/quote/digit
            char lastPrefixChar = prefix[prefix.Length - 1];
            if (!(char.IsLetterOrDigit(lastPrefixChar) || lastPrefixChar == '_'
                  || lastPrefixChar == ']' || lastPrefixChar == ')' || lastPrefixChar == '\''))
                return false;
            // the token right before the alias must not expect an operand (a COLLATE b, a AND b)
            int pw = prefix.LastIndexOfAny(new[] { ' ', '\t' });
            string beforeAlias = pw >= 0 ? prefix.Substring(pw + 1) : prefix;
            if (_operandExpectingBeforeAlias.Contains(beforeAlias))
                return false;
            return true;
        }

        /// <summary>
        /// True when the expression ends in a bare string literal acting as an AS-less alias
        /// (legacy syntax): CONVERT(...) N'sp_BlitzIndex v2.02'  →  the trailing N'...' is the
        /// column's alias, not a concat operand. Re-aliasing such a column yields invalid SQL
        /// (alias = expr 'alias'). The string is an OPERAND (return false) when preceded by an
        /// operator (a + 'b'); it is an ALIAS when preceded by a complete expression.
        /// Also recognizes the glued empty-string alias ''r / N''r (upstream #200 adjacency).
        /// </summary>
        private static bool EndsWithStringLiteralAlias(string expr)
        {
            expr = expr.TrimEnd();
            // glued empty-string alias: ''r or N''alias (no space, adjacency-preserved)
            if (System.Text.RegularExpressions.Regex.IsMatch(expr, @"^N?''[A-Za-z_#][A-Za-z0-9_$#]*$"))
                return true;
            if (!expr.EndsWith("'")) return false;
            // walk back to the opening quote of the trailing literal (skip '' escapes)
            int i = expr.Length - 2;
            while (i >= 0)
            {
                if (expr[i] == '\'')
                {
                    if (i - 1 >= 0 && expr[i - 1] == '\'') { i -= 2; continue; }
                    break;
                }
                i--;
            }
            if (i < 0) return false;
            int litStart = i;
            if (litStart - 1 >= 0 && (expr[litStart - 1] == 'N' || expr[litStart - 1] == 'n')) litStart--;
            string prefix = expr.Substring(0, litStart).TrimEnd();
            if (prefix.Length == 0) return false; // the whole column IS the string literal — no alias
            char last = prefix[prefix.Length - 1];
            // an operator/open-paren/dot before the string means it's an operand, not an alias
            if ("+-*/%=<>&|^.,(".IndexOf(last) >= 0) return false;
            return char.IsLetterOrDigit(last) || last == '_' || last == ')' || last == ']' || last == '\'';
        }

        /// <summary>
        /// Returns true if <paramref name="expr"/> is a single self-contained string literal
        /// ('alias' or N'alias' with no trailing content) — the legacy string-alias LHS form.
        /// </summary>
        private static bool IsStringLiteralAlias(string expr)
        {
            if (string.IsNullOrEmpty(expr)) return false;
            int start = expr.StartsWith("N'") ? 2 : (expr[0] == '\'' ? 1 : -1);
            if (start < 0 || expr.Length < start + 1 || !expr.EndsWith("'")) return false;
            // Ensure the literal closes exactly at the end (embedded '' escapes allowed).
            int i = start;
            while (i < expr.Length)
            {
                if (expr[i] == '\'')
                {
                    if (i + 1 < expr.Length && expr[i + 1] == '\'') { i += 2; continue; } // escaped quote
                    return i == expr.Length - 1; // closing quote must be the final char
                }
                i++;
            }
            return false;
        }

        /// <summary>
        /// Returns true if <paramref name="expr"/> is a bare column reference:
        /// an identifier (optionally bracket-quoted, optionally table- or schema-qualified)
        /// with no parentheses, spaces, or arithmetic operators.
        /// </summary>
        private static bool IsSimpleColumnRef(string expr)
        {
            if (string.IsNullOrEmpty(expr)) return false;
            //literals are not column references: numbers must not become their own alias
            // (SELECT 1 -> '1 = 1' is invalid SQL), and quoted strings are not identifiers
            if (char.IsDigit(expr[0]) || expr[0] == '\'' || expr.StartsWith("N'"))
                return false;
            // Scan character by character; spaces and special chars inside bracket-quoted
            // segments (e.g. [Active Flag]) are valid and must not trigger a false negative.
            // A ]] inside brackets is an escaped ] and stays in-bracket ([Definition: [x]] y]).
            bool inBracket = false;
            for (int k = 0; k < expr.Length; k++)
            {
                char c = expr[k];
                if (inBracket)
                {
                    if (c == ']')
                    {
                        if (k + 1 < expr.Length && expr[k + 1] == ']') { k++; continue; } // escaped ]]
                        inBracket = false;
                    }
                    continue;
                }
                if (c == '[') { inBracket = true; continue; }
                if (c == '(' || c == ')' || c == ' ' || c == '\t' ||
                    c == '+' || c == '-' || c == '/' || c == '%' || c == '*')
                    return false;
            }
            // Must look like: word, word.word, [word], [word].[word], schema.table.col, etc.
            foreach (string segment in expr.Split('.'))
            {
                string s = segment.Trim('[', ']');
                if (string.IsNullOrEmpty(s)) return false;
            }
            return true;
        }

        /// <summary>
        /// Returns the base column name from a possibly schema/table-qualified reference.
        /// E.g. "dbo.Employees.FirstName" → "FirstName", "[MyCol]" → "MyCol".
        /// </summary>
        private static string ExtractColumnBaseName(string expr)
        {
            int dot = expr.LastIndexOf('.');
            string name = dot >= 0 ? expr.Substring(dot + 1) : expr;
            //if the source column was bracket-quoted, the derived alias stays
            // bracket-quoted: [Some] -> alias [Some], never bare Some (SOME is a
            // reserved word; stripping the brackets produces invalid SQL)
            if (name.StartsWith("["))
                return name;
            return name.Contains(' ') ? "[" + name + "]" : name;
        }


        /// Only handles lines that are between a SELECT keyword line and the next clause keyword.
        /// </summary>
        private string RewriteAliasesToEqualSign(string output)
        {
            var lines = SplitLinesPreservingEndings(output, out var lineEndings);
            bool inSelectList = false;
            int parenDepth = 0, caseDepth = 0; // multi-line expression continuation tracking
            bool[] insideStringOrComment = ComputeLinesInsideStringOrComment(lines);

            for (int i = 0; i < lines.Count; i++)
            {
                //never touch lines inside OR opening a multi-line string literal / block comment
                if (LineTouchesStringOrComment(insideStringOrComment, i)) { inSelectList = false; parenDepth = 0; caseDepth = 0; continue; }
                string line = lines[i];
                string trimmed = line.TrimStart();
                string trimmedUpper = trimmed.ToUpperInvariant();

                // Detect start of SELECT column list
                if (trimmedUpper.StartsWith("SELECT ") || trimmedUpper == "SELECT")
                {
                    inSelectList = true;
                    parenDepth = 0;
                    caseDepth = 0;
                    int selectEnd = trimmed.IndexOf(' ');
                    if (selectEnd >= 0)
                    {
                        string afterSelect = trimmed.Substring(selectEnd + 1).TrimStart();
                        // Strip SELECT modifiers (TOP N / DISTINCT) before rewriting
                        var (modPfx1, colPart1, modOnly1) = StripSelectModifierPrefix(afterSelect);
                        if (!modOnly1)
                        {
                            string indent = line.Substring(0, line.Length - trimmed.Length);
                            bool hadTrailingComma1 = colPart1.EndsWith(",");
                            string colExpr1 = hadTrailingComma1 ? colPart1.Substring(0, colPart1.Length - 1).TrimEnd() : colPart1;
                            // only rewrite a self-contained (balanced) column; a multi-line
                            // expression start would have its "AS alias" flipped onto a fragment
                            if (CountNetParens(colExpr1) <= 0 && CountNetCase(colExpr1) <= 0
                                && !EndsWithTrailingBinaryOperator(colExpr1))
                            {
                                string rewritten = TryRewriteColumnLine(colExpr1);
                                string finalCol1 = rewritten + (hadTrailingComma1 ? "," : "");
                                if (finalCol1 != colPart1)
                                    lines[i] = indent + "SELECT " + modPfx1 + finalCol1;
                            }
                            parenDepth += CountNetParens(colExpr1);
                            if (parenDepth < 0) parenDepth = 0;
                            caseDepth += CountNetCase(colExpr1);
                            if (caseDepth < 0) caseDepth = 0;
                        }
                    }
                    continue;
                }

                // Detect end of SELECT list
                if (inSelectList)
                {
                    //blank line = end of statement (see EnsureColumnAliases)
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        inSelectList = false;
                        parenDepth = 0;
                        caseDepth = 0;
                        continue;
                    }
                    bool startsWithComma = trimmed.StartsWith(",");
                    // A continuation of a wrapped column expression (inside open parens/CASE,
                    // after a line ending in an operator, or leading with AND/OR/THEN/operator)
                    // is a FRAGMENT: rewriting its "... AS alias" tail flips the alias onto an
                    // incomplete expression ("DatabaseItem = and right(...)"). Track depths, skip.
                    bool isContinuation =
                        parenDepth > 0 || caseDepth > 0
                        || (!startsWithComma && PrevColumnLineEndsOpen(lines, i))
                        || (!startsWithComma
                            && (trimmedUpper.StartsWith("AND ") || trimmedUpper.StartsWith("OR ")
                                || trimmedUpper.StartsWith("THEN ") || trimmedUpper.StartsWith("WHEN ")
                                || trimmedUpper.StartsWith("ELSE ") || trimmedUpper.StartsWith("AS ")
                                || (trimmed.Length > 0 && "+-*/%=<>&|^".IndexOf(trimmed[0]) >= 0)
                                || (trimmed.Length > 0 && trimmed[0] == '(' && !trimmedUpper.StartsWith("(SELECT"))));
                    if (!startsWithComma && !isContinuation && IsClauseStartLine(trimmedUpper))
                    {
                        inSelectList = false;
                        continue;
                    }

                    string colBody = startsWithComma ? trimmed.Substring(1).TrimStart() : trimmed;
                    if (isContinuation)
                    {
                        parenDepth += CountNetParens(colBody);
                        if (parenDepth < 0) parenDepth = 0;
                        caseDepth += CountNetCase(colBody);
                        if (caseDepth < 0) caseDepth = 0;
                        continue;
                    }

                    if (startsWithComma)
                    {
                        // Leading comma style: ,expr AS alias
                        string afterComma = trimmed.Substring(1).TrimStart();
                        if (CountNetParens(afterComma) <= 0 && CountNetCase(afterComma) <= 0
                            && !EndsWithTrailingBinaryOperator(afterComma))
                        {
                            string rewritten = TryRewriteColumnLine(afterComma);
                            if (rewritten != afterComma)
                            {
                                string indent = line.Substring(0, line.Length - trimmed.Length);
                                lines[i] = indent + "," + rewritten;
                            }
                        }
                        parenDepth += CountNetParens(afterComma);
                        if (parenDepth < 0) parenDepth = 0;
                        caseDepth += CountNetCase(afterComma);
                        if (caseDepth < 0) caseDepth = 0;
                    }
                    else if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        bool hadTrailingComma = trimmed.EndsWith(",");
                        var (modPfx2, colExpr2, modOnly2) = StripSelectModifierPrefix(trimmed.TrimEnd(','));
                        if (modOnly2)
                            continue; // pure TOP N or DISTINCT line — not a column, leave as-is

                        if (CountNetParens(colExpr2) <= 0 && CountNetCase(colExpr2) <= 0
                            && !EndsWithTrailingBinaryOperator(colExpr2))
                        {
                            string rewritten = TryRewriteColumnLine(colExpr2);
                            string indent2 = line.Substring(0, line.Length - trimmed.Length);
                            if (rewritten != colExpr2 || modPfx2.Length > 0)
                                lines[i] = indent2 + modPfx2 + rewritten + (hadTrailingComma ? "," : "");
                        }
                        parenDepth += CountNetParens(colExpr2);
                        if (parenDepth < 0) parenDepth = 0;
                        caseDepth += CountNetCase(colExpr2);
                        if (caseDepth < 0) caseDepth = 0;
                    }
                }
            }

            return JoinLinesPreservingEndings(lines, lineEndings);
        }

        private static bool IsClauseStartLine(string trimmedUpper)
        {
            return trimmedUpper.StartsWith("FROM ") || trimmedUpper == "FROM"
                // FROM glued to its source with no space: FROM(subquery), FROM::fn_trace_gettable(...)
                || trimmedUpper.StartsWith("FROM(") || trimmedUpper.StartsWith("FROM::")
                || trimmedUpper.StartsWith("WHERE ") || trimmedUpper == "WHERE"
                || trimmedUpper.StartsWith("GROUP ") || trimmedUpper == "GROUP"
                || trimmedUpper.StartsWith("ORDER ") || trimmedUpper == "ORDER"
                || trimmedUpper.StartsWith("HAVING ") || trimmedUpper == "HAVING"
                || trimmedUpper.StartsWith("UNION ") || trimmedUpper == "UNION"
                || trimmedUpper.StartsWith("INTERSECT ") || trimmedUpper == "INTERSECT"
                || trimmedUpper.StartsWith("EXCEPT ") || trimmedUpper == "EXCEPT"
                || trimmedUpper.StartsWith("INTO ")
                //select lists also end at cursor/query options and batch separators:
                // FOR (cursor FOR READ ONLY / FOR XML / FOR UPDATE), OPTION(...) hints,
                // GO, and INSERT's VALUES keyword
                || trimmedUpper.StartsWith("FOR ") || trimmedUpper == "FOR"
                || trimmedUpper.StartsWith("OPTION ") || trimmedUpper.StartsWith("OPTION(")
                || trimmedUpper.StartsWith("GO ") || trimmedUpper == "GO"
                || trimmedUpper.StartsWith("VALUES ") || trimmedUpper == "VALUES" || trimmedUpper.StartsWith("VALUES(")
                || trimmedUpper.StartsWith(")") // subquery end
                // CASE sub-expression keywords — not column expressions
                || trimmedUpper.StartsWith("WHEN ") || trimmedUpper == "WHEN"
                || trimmedUpper.StartsWith("THEN ") || trimmedUpper == "THEN"
                || trimmedUpper.StartsWith("ELSE ") || trimmedUpper == "ELSE"
                || trimmedUpper == "END" || trimmedUpper.StartsWith("END ")
                || trimmedUpper.StartsWith("END,") || trimmedUpper.StartsWith("END)")
                ;
        }

        /// <summary>
        /// Strips a TOP N or DISTINCT prefix from a SELECT column expression.
        /// Returns (prefix, remainder, modifierOnly) where:
        /// - prefix: the stripped modifier text (e.g. "TOP (1000) ") — empty if none
        /// - remainder: the column expression after the modifier
        /// - modifierOnly: true when the line contains only the modifier with no column following
        /// </summary>
        private static bool IsSelectModifierKeyword(string kwUpper) =>
            kwUpper is "DISTINCT" or "TOP" or "PERCENT" or "WITH" or "TIES";

        private static (string prefix, string remainder, bool modifierOnly) StripSelectModifierPrefix(string expr)
        {
            string trimmed = expr.TrimStart();
            int leadingSpaces = expr.Length - trimmed.Length;
            string accumulatedPrefix = expr.Substring(0, leadingSpaces);

            // Loop: keep stripping TOP and DISTINCT modifiers until neither remains.
            while (true)
            {
                string upper = trimmed.ToUpperInvariant();

                // DISTINCT
                if (upper.StartsWith("DISTINCT "))
                {
                    // Preserve original casing from the input string
                    string modToken = trimmed.Substring(0, "DISTINCT".Length);
                    string rest = trimmed.Substring("DISTINCT ".Length).TrimStart();
                    accumulatedPrefix += modToken + " ";
                    trimmed = rest;
                    continue;
                }
                if (upper == "DISTINCT")
                {
                    string modToken = trimmed.Substring(0, "DISTINCT".Length);
                    return (accumulatedPrefix + modToken, "", true);
                }

                // Bare "TOP" with no argument — modifier-only (incomplete TOP clause)
                if (upper == "TOP")
                    return (accumulatedPrefix + trimmed, "", true);

                // TOP (N) or TOP N
                if (upper.StartsWith("TOP ") || upper.StartsWith("TOP("))
                {
                    // Preserve original casing of "TOP"
                    string topWord = trimmed.Substring(0, 3); // "TOP" in original case
                    // Everything after "TOP" — do NOT assume a space exists ("TOP(@i)"
                    // reaches here with no space at all, e.g. from sp_WhoIsActive).
                    string afterTop = trimmed.Substring(3).TrimStart();
                    string rest;
                    string topToken;
                    if (afterTop.StartsWith("("))
                    {
                        // Find the MATCHING close paren — TOP arguments can nest calls,
                        // e.g. TOP (LEN(ISNULL(@s, N''))); taking the first ')' would cut
                        // the expression mid-call and alias the severed TOP as a column.
                        int depth = 0;
                        int close = -1;
                        for (int p = 0; p < afterTop.Length; p++)
                        {
                            if (afterTop[p] == '(') depth++;
                            else if (afterTop[p] == ')' && --depth == 0) { close = p; break; }
                        }
                        if (close < 0)
                            return (accumulatedPrefix + trimmed, "", true); //unterminated TOP(...) — treat as modifier-only
                        topToken = afterTop.Substring(0, close + 1);
                        rest = afterTop.Substring(close + 1).TrimStart();
                    }
                    else
                    {
                        int spaceAfterN = afterTop.IndexOf(' ');
                        topToken = spaceAfterN >= 0 ? afterTop.Substring(0, spaceAfterN) : afterTop;
                        rest = spaceAfterN >= 0 ? afterTop.Substring(spaceAfterN + 1).TrimStart() : "";
                    }
                    string topPrefix = topWord + " " + topToken + (string.IsNullOrWhiteSpace(rest) ? "" : " ");
                    // Handle PERCENT / WITH TIES (these are terminal — no further modifier stripping)
                    string restUpper = rest.ToUpperInvariant();
                    if (restUpper.StartsWith("PERCENT ")) { topPrefix += "PERCENT "; rest = rest.Substring("PERCENT ".Length).TrimStart(); }
                    else if (restUpper == "PERCENT") return (accumulatedPrefix + topPrefix + "PERCENT", "", true);
                    restUpper = rest.ToUpperInvariant();
                    if (restUpper.StartsWith("WITH TIES")) return (accumulatedPrefix + topPrefix + "WITH TIES", "", true);
                    accumulatedPrefix += topPrefix;
                    trimmed = rest;
                    continue;
                }

                // No more modifiers — done.
                break;
            }

            return (accumulatedPrefix, trimmed, string.IsNullOrWhiteSpace(trimmed));
        }

        /// <summary>
        /// Rewrites a single column expression "expr AS alias" to "alias = expr".
        /// Returns the original string if no " AS " is found at the top level.
        /// </summary>
        private static string TryRewriteColumnLine(string col)
        {
            //peel a trailing -- comment first: "expr AS alias -- note" must become
            // "alias = expr -- note"; splitting naively leaves the comment glued to the
            // alias and the comment then swallows " = expr" entirely.
            string original = col;
            string trailingComment = "";
            int commentStart = FindLineCommentStart(col);
            if (commentStart >= 0)
            {
                trailingComment = " " + col.Substring(commentStart);
                col = col.Substring(0, commentStart).TrimEnd();
            }
            else
            {
                //also peel a whole trailing block comment ("expr AS alias, /* note */"):
                // otherwise it glues to the alias and the alias becomes "alias, /* note */"
                int bc = col.LastIndexOf("/*", System.StringComparison.Ordinal);
                if (bc >= 0 && col.IndexOf("*/", bc, System.StringComparison.Ordinal) == col.TrimEnd().Length - 2)
                {
                    trailingComment = " " + col.Substring(bc).TrimEnd();
                    col = col.Substring(0, bc).TrimEnd();
                }
            }
            int asPos = FindAsOutsideParens(col);
            if (asPos < 0) return original;
            string expr = col.Substring(0, asPos).TrimEnd();
            string alias = col.Substring(asPos + 4).Trim();
            //a trailing statement terminator / column-separator comma belongs at the END of
            // the rewritten line, not glued to the alias ("x AS a;" -> "a = x;" not "a; = x";
            // "x AS a," -> "a = x," not "a, = x")
            string suffix = "";
            while (alias.EndsWith(";") || alias.EndsWith(","))
            {
                suffix = alias[alias.Length - 1] + suffix;
                alias = alias.Substring(0, alias.Length - 1).TrimEnd();
            }
            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(expr)) return original;
            // match the core's ";--comment" (no gap) so it doesn't drift on re-format
            if (suffix.EndsWith(";") && trailingComment.StartsWith(" --"))
                trailingComment = trailingComment.Substring(1);
            return alias + " = " + expr + suffix + trailingComment;
        }

        /// <summary>
        /// Finds the position of a top-level " AS " (case-insensitive) in a string,
        /// ignoring occurrences inside parentheses or string literals.
        /// Returns -1 if not found.
        /// </summary>
        private static int FindAsOutsideParens(string s)
        {
            int depth = 0;
            bool inString = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inString)
                {
                    if (c == '\'') inString = false;
                    continue;
                }
                if (c == '\'')
                {
                    inString = true;
                    continue;
                }
                //comments: '-- ...' ends the scan; '/* ... */' spans are skipped; an
                // unterminated '/*' means the rest of the line is comment content
                // (a comment like /* same drive as data */ must never match " AS ")
                if (c == '-' && i + 1 < s.Length && s[i + 1] == '-') break;
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0) break;
                    i = close + 1;
                    continue;
                }
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth--; continue; }
                if (depth == 0 && i + 4 <= s.Length)
                {
                    string sub = s.Substring(i, 4);
                    if (sub.Equals(" AS ", StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Finds the position of a top-level standalone '=' outside parentheses, string
        /// literals, and bracket-quoted identifiers (e.g. [Odd=Name]).
        /// Excludes !=, >=, &lt;= compound operators. Returns -1 if not found.
        /// </summary>
        private static int FindEqualSignOutsideParens(string s)
        {
            int depth = 0;
            bool inString = false;
            bool inBracket = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inString) { if (c == '\'') inString = false; continue; }
                if (inBracket) { if (c == ']') inBracket = false; continue; }
                if (c == '\'') { inString = true; continue; }
                if (c == '[') { inBracket = true; continue; }
                if (c == '-' && i + 1 < s.Length && s[i + 1] == '-') break; //-- comment
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0) break; //comment continues past end of line
                    i = close + 1;
                    continue;
                }
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth--; continue; }
                if (depth == 0 && c == '=')
                {
                    // exclude comparison !=/>=/<= and compound-assignment +=/-=/*=//=/%=/&=/|=/^=
                    // (SELECT @v += (...) must not be split into "@v + = (...)")
                    if (i > 0 && "!<>+-*/%&|^".IndexOf(s[i - 1]) >= 0) continue;
                    if (i + 1 < s.Length && s[i + 1] == '=') continue;
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Pads column expressions in SELECT lists so that AS keywords align vertically.
        /// Works on the full formatted output, only affecting SELECT column list lines.
        /// </summary>
        private string AlignSelectColumns(string output)
        {
            var lines = SplitLinesPreservingEndings(output, out var lineEndings);
            bool[] insideStringOrComment = ComputeLinesInsideStringOrComment(lines);

            // Find runs of SELECT column lines and align each run.
            int i = 0;
            while (i < lines.Count)
            {
                //never treat lines inside or opening a multi-line string literal / block comment as SQL
                if (LineTouchesStringOrComment(insideStringOrComment, i)) { i++; continue; }
                string trimmed = lines[i].TrimStart().ToUpperInvariant();
                if (trimmed.StartsWith("SELECT ") || trimmed == "SELECT")
                {
                    // Collect the range of lines that are SELECT column list lines.
                    int start = i;
                    // The SELECT line itself may have the first column inline.
                    // Column lines continue until we hit a non-column line.
                    while (i < lines.Count)
                    {
                        if (LineTouchesStringOrComment(insideStringOrComment, i))
                            break;
                        string t = lines[i].TrimStart();
                        if (i > start && string.IsNullOrWhiteSpace(t))
                            break; //blank line = end of statement
                        string tu = t.ToUpperInvariant();
                        if (i > start && !t.StartsWith(",") && IsClauseStartLine(tu))
                            break;
                        i++;
                    }
                    // lines[start..i) is the SELECT block.
                    // Dispatch based on ColumnAliasStyle
                    if (Options.ColumnAliasStyle == ColumnAliasStyle.EqualSign)
                        AlignBlockEqualSign(lines, start, i);
                    else
                        AlignBlockAsKeywords(lines, start, i);
                    continue; // don't increment i again
                }
                i++;
            }

            return JoinLinesPreservingEndings(lines, lineEndings);
        }

        /// <summary>
        /// Marks each line in a SELECT block [start,end) that is a CONTINUATION of a
        /// wrapped column expression rather than a column of its own: inside parens/CASE
        /// opened on an earlier line, following a line that ended with a trailing operator
        /// (=, +, AS, ...), or leading with a continuation token (AND/OR/THEN/operator).
        /// The align passes must not treat these as "alias = expr" / "expr AS alias" — doing
        /// so splices a spurious alias into the middle of a wrapped CASE/concat (invalid SQL).
        /// Returned array is indexed 0..(end-start).
        /// </summary>
        private static bool[] ComputeSelectBlockContinuationMask(IList<string> lines, int start, int end)
        {
            var mask = new bool[end - start];
            int parenDepth = 0, caseDepth = 0;
            for (int i = start; i < end; i++)
            {
                string trimmed = lines[i].TrimStart();
                string tu = trimmed.ToUpperInvariant();
                // strip the SELECT / leading-comma marker to inspect the column expression
                string content = tu.StartsWith("SELECT ") ? trimmed.Substring(7).TrimStart()
                               : trimmed.StartsWith(",") ? trimmed.Substring(1).TrimStart()
                               : trimmed;
                bool leadingContinuation =
                    !trimmed.StartsWith(",")
                    && (tu.StartsWith("AND ") || tu.StartsWith("OR ") || tu.StartsWith("THEN ")
                        || tu.StartsWith("WHEN ") || tu.StartsWith("ELSE ") || tu.StartsWith("AS ")
                        || tu == "AS" || (trimmed.Length > 0 && "+-/%=<>&|^".IndexOf(trimmed[0]) >= 0)
                        || (trimmed.Length > 0 && trimmed[0] == '(' && !tu.StartsWith("(SELECT")));
                bool prevOpen = i > start && PrevColumnLineEndsOpen(lines, i);
                mask[i - start] = parenDepth > 0 || caseDepth > 0 || prevOpen || leadingContinuation;
                parenDepth += CountNetParens(content);
                if (parenDepth < 0) parenDepth = 0;
                caseDepth += CountNetCase(content);
                if (caseDepth < 0) caseDepth = 0;
            }
            return mask;
        }

        private void AlignBlockAsKeywords(IList<string> lines, int start, int end)
        {
            bool[] continuation = ComputeSelectBlockContinuationMask(lines, start, end);
            // For each line in the block, find the position of " AS " (outside parens).
            // Measure the max expression width and pad.
            // Lines using EqualSign-style aliases (alias = expr) are preserved as-is and
            // aligned as their own group (their '=' signs line up with each other).
            var items = new List<(int lineIdx, string indent, string comma, string expr, string alias)>();
            var equalSignItems = new List<(int lineIdx, string prefix, string alias, string expr)>();

            for (int i = start; i < end; i++)
            {
                if (continuation[i - start]) continue; // wrapped-expression continuation, not a column
                string line = lines[i];
                string trimmed = line.TrimStart();
                string indent = line.Substring(0, line.Length - trimmed.Length);

                // Handle SELECT keyword on first line
                string content;
                string prefix;
                if (trimmed.ToUpperInvariant().StartsWith("SELECT "))
                {
                    content = trimmed.Substring(7).TrimStart(); // after "SELECT "
                    prefix = indent + "SELECT ";
                }
                else if (trimmed.StartsWith(","))
                {
                    content = trimmed.Substring(1).TrimStart();
                    prefix = indent + ",";
                }
                else
                {
                    content = trimmed;
                    prefix = indent;
                }

                int asPos = FindAsOutsideParens(content);
                if (asPos >= 0)
                {
                    string expr = content.Substring(0, asPos).TrimEnd();
                    string alias = content.Substring(asPos + 4).Trim();
                    items.Add((i, prefix, "", expr, alias));
                    continue;
                }

                // EqualSign-style alias line: alias = expr (LHS must be a simple column
                // reference; @variable assignments are not aliases).
                if (!content.StartsWith("@"))
                {
                    int eqPos = FindEqualSignOutsideParens(content);
                    if (eqPos > 0)
                    {
                        string alias = content.Substring(0, eqPos).TrimEnd();
                        string expr = content.Substring(eqPos + 1).TrimStart();
                        if (IsSimpleColumnRef(alias) && expr.Length > 0)
                            equalSignItems.Add((i, prefix, alias, expr));
                    }
                }
            }

            if (items.Count >= 2)
            {
                int maxExprLen = items.Max(it => it.expr.Length);
                foreach (var (lineIdx, prefix, _, expr, alias) in items)
                {
                    string padding = new string(' ', maxExprLen - expr.Length);
                    lines[lineIdx] = prefix + expr + padding + " AS " + alias;
                }
            }

            if (equalSignItems.Count >= 2)
            {
                int maxAliasLen = equalSignItems.Max(it => it.alias.Length);
                foreach (var (lineIdx, prefix, alias, expr) in equalSignItems)
                {
                    string padding = new string(' ', maxAliasLen - alias.Length);
                    lines[lineIdx] = prefix + alias + padding + " = " + expr;
                }
            }
        }

        private void AlignBlockEqualSign(IList<string> lines, int start, int end)
        {
            // For lines in alias = expr format, align the = signs to a tab stop column.
            // Lines look like: indent + [SELECT | ,] + alias + = + expr
            var items = new List<(int lineIdx, string prefix, string alias, string expr)>();
            bool[] continuation = ComputeSelectBlockContinuationMask(lines, start, end);

            for (int i = start; i < end; i++)
            {
                if (continuation[i - start]) continue; // wrapped-expression continuation, not a column
                string line = lines[i];
                string trimmed = line.TrimStart();
                string indent = line.Substring(0, line.Length - trimmed.Length);

                // Handle SELECT keyword on first line
                string content;
                string prefix;
                if (trimmed.ToUpperInvariant().StartsWith("SELECT "))
                {
                    content = trimmed.Substring(7).TrimStart(); // after "SELECT "
                    prefix = indent + "SELECT ";
                }
                else if (trimmed.StartsWith(","))
                {
                    content = trimmed.Substring(1).TrimStart();
                    prefix = indent + ",";
                }
                else
                {
                    content = trimmed;
                    prefix = indent;
                }

                int eqPos = FindEqualSignOutsideParens(content);
                if (eqPos >= 0)
                {
                    string alias = content.Substring(0, eqPos).TrimEnd();
                    string expr = content.Substring(eqPos + 1).TrimStart();
                    items.Add((i, prefix, alias, expr));
                }
            }

            if (items.Count < 2) return;

            int maxAliasLen = items.Max(it => it.alias.Length);
            int targetCol = ((maxAliasLen / Options.SpacesPerTab) + 1) * Options.SpacesPerTab;

            foreach (var (lineIdx, prefix, alias, expr) in items)
            {
                int padding = targetCol - alias.Length;
                // An empty expr means the value wrapped to the next line (WrapOverflowingAliasLiterals
                // or a core width break). Emit a bare "=" — a "= " here would strand a trailing
                // space that the core strips on the next pass (trailing-space non-idempotency).
                string eq = expr.Length == 0 ? "=" : "= " + expr;
                lines[lineIdx] = prefix + alias + new string(' ', padding) + eq;
            }
        }
    }
}
