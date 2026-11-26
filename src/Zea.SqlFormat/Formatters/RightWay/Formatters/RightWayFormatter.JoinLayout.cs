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

namespace Zea.SqlFormat.Formatters.RightWay
{
    internal sealed partial class RightWayFormatter
    {
        /// <summary>
        /// Scans the formatted output for FROM/JOIN blocks and rewrites them so:
        ///   1. All table names (keyword + tablename) end at the same tab stop.
        ///   2. Every line has an explicit AS alias (uses table base-name if none).
        ///   3. AS and alias are vertically aligned across all FROM/JOIN lines.
        ///   4. ON is vertically aligned at the next tab stop past the longest alias.
        ///   5. Multi-condition ON clauses that exceed 100 chars wrap to the next line,
        ///      aligned under the ON keyword.
        /// </summary>
        private string AlignFromJoinClauses(string output)
        {
            var lines = SplitLinesPreservingEndings(output, out var lineEndings);
            int indentSize = Options.SpacesPerTab > 0 ? Options.SpacesPerTab : 4;
            bool[] insideStringOrComment = ComputeLinesInsideStringOrComment(lines);
            string[] statementContexts = ComputeStatementContexts(lines, insideStringOrComment);

            // Find contiguous FROM/JOIN blocks and align each one.
            int i = 0;
            while (i < lines.Count)
            {
                if (LineTouchesStringOrComment(insideStringOrComment, i)) { i++; continue; }
                string trimmed = lines[i].TrimStart();
                string upper = trimmed.ToUpperInvariant();
                // FROM keywords appear in many statements that are NOT table-source clauses
                // (FETCH ... FROM cursor, BULK INSERT ... FROM 'file', BEGIN DIALOG ... FROM
                // SERVICE, REVOKE ... FROM principal, BACKUP/RESTORE ... FROM DISK, ...).
                // Rewriting those corrupts or even DELETES content — skip such statements
                // entirely. DELETE is special: its target FROM cannot take an AS alias, so
                // the block may be aligned but never gains invented aliases.
                string stmtContext = statementContexts[i];
                if (IsNonTableSourceStatementContext(stmtContext)) { i++; continue; }
                // DELETE's target FROM cannot take an AS alias. The statement context
                // catches "DELETE ..." at statement start, but a CTE-prefixed
                // "WITH x AS (...) DELETE FROM x" hides it — so also check whether the
                // immediately preceding content line is a bare DELETE [TOP (n)].
                bool allowAddAliases = !stmtContext.StartsWith("DELETE")
                    && !PrecedingLineIsBareDelete(lines, insideStringOrComment, i);
                if (IsFromOrJoinLine(upper))
                {
                    int blockStart = i;
                    bool expectFirstCond = false;
                    while (i < lines.Count)
                    {
                        if (LineTouchesStringOrComment(insideStringOrComment, i))
                            break;
                        string t = lines[i].TrimStart().ToUpperInvariant();
                        bool isFromJoin = IsFromOrJoinLine(t);
                        bool isContinuation = IsJoinContinuationLine(t);

                        if (i > blockStart && !isFromJoin && !isContinuation && !expectFirstCond)
                            break;

                        if (expectFirstCond)
                            expectFirstCond = false;
                        else if (isFromJoin)
                            expectFirstCond = t.TrimEnd().EndsWith(" ON");

                        i++;
                    }
                    var blockLines = lines.GetRange(blockStart, i - blockStart);
                    var newBlock = AlignFromJoinBlock(blockLines, indentSize, allowAddAliases);
                    // keep lineEndings index-aligned: FROM/JOIN blocks are structural
                    // (never inside a literal), so new interior lines get the platform
                    // newline; the block's terminal ending is preserved (handles EOF).
                    string blockTailEnding = lineEndings[i - 1];
                    lines.RemoveRange(blockStart, i - blockStart);
                    lineEndings.RemoveRange(blockStart, i - blockStart);
                    lines.InsertRange(blockStart, newBlock);
                    var newEndings = new List<string>();
                    for (int k = 0; k < newBlock.Count; k++)
                        newEndings.Add(k == newBlock.Count - 1 ? blockTailEnding : Environment.NewLine);
                    lineEndings.InsertRange(blockStart, newEndings);
                    i = blockStart + newBlock.Count;
                    //line indices shifted; keep the in-string/in-comment mask and the
                    // statement-context map in sync
                    if (newBlock.Count != blockLines.Count)
                    {
                        insideStringOrComment = ComputeLinesInsideStringOrComment(lines);
                        statementContexts = ComputeStatementContexts(lines, insideStringOrComment);
                    }
                    continue;
                }
                i++;
            }

            return JoinLinesPreservingEndings(lines, lineEndings);
        }

        /// <summary>
        /// Computes, for each line, the leading keyword pair of the statement it belongs to
        /// (e.g. "SELECT", "BULK INSERT", "BEGIN DIALOG"). Statement boundaries follow the
        /// same text-level conventions as the other post-passes: blank lines, GO, and a
        /// line-terminating semicolon end a statement.
        /// </summary>
        private static string[] ComputeStatementContexts(List<string> lines, bool[] insideStringOrComment)
        {
            var contexts = new string[lines.Count];
            string current = "";
            bool newStatement = true;
            for (int i = 0; i < lines.Count; i++)
            {
                if (LineTouchesStringOrComment(insideStringOrComment, i)) { contexts[i] = current; continue; }
                string t = lines[i].Trim();
                if (t.Length == 0) { current = ""; newStatement = true; contexts[i] = current; continue; }
                string up = t.ToUpperInvariant();
                if (up == "GO" || up.StartsWith("GO ") || up.StartsWith("GO;"))
                {
                    current = ""; newStatement = true; contexts[i] = current; continue;
                }
                if (newStatement && !up.StartsWith("--") && !up.StartsWith("/*"))
                {
                    var words = up.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    current = words.Length >= 2 ? words[0] + " " + words[1] : (words.Length == 1 ? words[0] : "");
                    newStatement = false;
                }
                contexts[i] = current;
                if (up.EndsWith(";"))
                    newStatement = true;
            }
            return contexts;
        }

        /// <summary>
        /// Returns true when the nearest preceding content line is a bare DELETE
        /// (optionally with TOP (n)) — meaning the current FROM introduces the DELETE
        /// target, which cannot take an invented alias.
        /// </summary>
        private static bool PrecedingLineIsBareDelete(List<string> lines, bool[] insideStringOrComment, int i)
        {
            for (int p = i - 1; p >= 0; p--)
            {
                if (LineTouchesStringOrComment(insideStringOrComment, p)) return false;
                string t = lines[p].Trim();
                if (t.Length == 0) return false; // blank = statement boundary
                string up = t.ToUpperInvariant();
                if (up.StartsWith("--")) continue;
                return up == "DELETE" || (up.StartsWith("DELETE TOP") && !up.Contains(" FROM "));
            }
            return false;
        }

        /// <summary>
        /// Statements whose FROM keyword introduces something other than a query table
        /// source; their FROM lines must never be align-rewritten or given aliases.
        /// </summary>
        private static bool IsNonTableSourceStatementContext(string context)
        {
            if (string.IsNullOrEmpty(context)) return false;
            int sp = context.IndexOf(' ');
            string first = sp >= 0 ? context.Substring(0, sp) : context;
            switch (first)
            {
                case "FETCH":       // FETCH NEXT FROM cursor
                case "BULK":        // BULK INSERT ... FROM 'file'
                case "BACKUP":      // BACKUP ... FROM/TO DISK
                case "RESTORE":     // RESTORE ... FROM DISK
                case "GRANT":
                case "REVOKE":      // REVOKE ... FROM principal [CASCADE]
                case "DENY":
                case "OPEN":
                case "CLOSE":
                case "DEALLOCATE":
                case "RECEIVE":     // RECEIVE ... FROM queue (Service Broker)
                case "SEND":
                case "GET":         // GET CONVERSATION GROUP ... FROM queue
                    return true;
                case "BEGIN":
                    return context.StartsWith("BEGIN DIALOG"); // BEGIN DIALOG ... FROM SERVICE
                case "CREATE":
                case "ALTER":
                    // CREATE/ALTER USER ... FROM LOGIN, CREATE LOGIN ... FROM CERTIFICATE/
                    // WINDOWS/ASYMMETRIC KEY, CREATE ASSEMBLY ... FROM <bits/path>: the FROM
                    // is a DDL source, not a table source (never gets an AS alias). CREATE
                    // VIEW/PROCEDURE/FUNCTION bodies DO contain real SELECT...FROM, so only
                    // these specific principal/assembly subtypes are excluded.
                    return context.StartsWith(first + " USER")
                        || context.StartsWith(first + " LOGIN")
                        || context.StartsWith(first + " ASSEMBLY");
                default:
                    return false;
            }
        }

        private static bool IsFromOrJoinLine(string trimmedUpper)
        {
            return trimmedUpper.StartsWith("FROM ") || trimmedUpper == "FROM"
                || trimmedUpper.StartsWith("INNER JOIN ") || trimmedUpper.StartsWith("LEFT JOIN ")
                || trimmedUpper.StartsWith("RIGHT JOIN ") || trimmedUpper.StartsWith("FULL JOIN ")
                || trimmedUpper.StartsWith("FULL OUTER JOIN ") || trimmedUpper.StartsWith("LEFT OUTER JOIN ")
                || trimmedUpper.StartsWith("RIGHT OUTER JOIN ") || trimmedUpper.StartsWith("CROSS JOIN ")
                || trimmedUpper.StartsWith("JOIN ");
        }

        private static bool IsJoinContinuationLine(string trimmedUpper)
        {
            // Lines that are AND/OR continuation of a multi-condition ON clause.
            return trimmedUpper.StartsWith("AND ") || trimmedUpper.StartsWith("OR ");
        }

        /// <summary>
        /// Rewrites a FROM/JOIN block with aligned table names, AS aliases, ON conditions, and = signs.
        /// When a JOIN line ends with a trailing ON (IndentJoinOnClause mode), the first condition
        /// is pulled up to be inline with ON; subsequent AND/OR conditions are aligned under ON.
        /// Returns the new lines for the block (may be fewer than input due to pulled-up conditions).
        /// </summary>
        private List<string> AlignFromJoinBlock(List<string> blockLines, int indentSize, bool allowAddAliases = true)
        {
            // Keyword tokens that we inject — respect UppercaseKeywords setting.
            bool uc = Options.UppercaseKeywords;
            string KW_AS  = uc ? "AS"  : "as";
            string KW_ON  = uc ? "ON"  : "on";
            string KW_AND = uc ? "AND" : "and";

            // ---- Pass 1: parse each FROM/JOIN line and collect ON conditions ----
            var items = new List<JoinLineItem>();
            bool captureFirstCond = false;
            bool captureAndOrConds = false;

            for (int i = 0; i < blockLines.Count; i++)
            {
                string line = blockLines[i];
                string trimmed = line.TrimStart();
                string trimmedUpper = trimmed.ToUpperInvariant();

                if (IsJoinContinuationLine(trimmedUpper))
                {
                    // Collect AND/OR conditions for the current HasTrailingOn item.
                    if (captureAndOrConds && items.Count > 0)
                    {
                        int sp = trimmed.IndexOf(' ');
                        if (sp >= 0) items[items.Count - 1].Conditions.Add(trimmed.Substring(sp + 1));
                    }
                    continue;
                }

                // Capture first condition after a HasTrailingOn JOIN.
                if (captureFirstCond && !IsFromOrJoinLine(trimmedUpper))
                {
                    var last = items[items.Count - 1];
                    last.FirstCondition = trimmed;
                    last.Conditions.Add(trimmed);
                    captureFirstCond = false;
                    captureAndOrConds = true;
                    continue;
                }
                captureFirstCond = false;
                captureAndOrConds = false;

                string indent = line.Substring(0, line.Length - trimmed.Length);
                ParseFromJoinLine(trimmed, out string keyword, out string table, out string alias, out string onClause, out bool hasTrailingOn);

                var newItem = new JoinLineItem
                {
                    Indent = indent,
                    Keyword = keyword,
                    Table = table,
                    Alias = alias,
                    OnClause = onClause,
                    HasTrailingOn = hasTrailingOn,
                };

                // For inline ON, collect individual conditions from the clause.
                if (!hasTrailingOn && !string.IsNullOrEmpty(onClause))
                    newItem.Conditions.AddRange(SplitOnConditions(onClause));

                items.Add(newItem);
                captureFirstCond = hasTrailingOn;
            }

            if (items.Count == 0) return blockLines;

            // ---- Pass 2: ensure every item has an alias ---------------------
            if (Options.AlignTableJoinsAddAliases && allowAddAliases)
            {
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.Alias)) continue;
                    string tbl = item.Table;
                    // Only invent an alias for plain (possibly schema-qualified) table names.
                    // Skip: derived tables "(", string sources ('file', N'svc'), table
                    // variables (@t — the derived alias "@t" would be illegal), names with
                    // parens (TVF calls, legacy "(nolock)" hints), bracket-quoted parts
                    // (derivation is unsafe), and names carrying a statement terminator.
                    if (string.IsNullOrEmpty(tbl)) continue;
                    char c0 = tbl[0];
                    if (c0 == '(' || c0 == '\'' || c0 == '@' || tbl.StartsWith("N'")
                        || tbl.IndexOf('(') >= 0 || tbl.IndexOf('[') >= 0
                        || tbl.EndsWith(";"))
                        continue;
                    string baseName = BaseTableName(tbl);
                    // Reserved words / keyword-list names would need brackets as an alias —
                    // not worth inventing; leave the table unaliased instead.
                    if (!IsHarmlessUnbracketableName(baseName)) continue;
                    item.Alias = baseName;
                }
            }

            // ---- Pass 3: compute column positions --------------------------
            int maxKwTableLen = items.Max(it => it.Keyword.Length + 1 + it.Table.Length);
            int targetTableCol = ((maxKwTableLen / indentSize) + 1) * indentSize;

            int maxAliasLen = items.Max(it => it.Alias.Length);
            int asAliasLen = 3 + maxAliasLen; // "AS " + alias
            int targetOnCol = targetTableCol + ((asAliasLen / indentSize) + 1) * indentSize;
            // Padding before ON so that ON starts at exactly targetOnCol for every JOIN line.
            // Guaranteed >= 1 because targetOnCol rounds up past targetTableCol + asAliasLen.
            int onPad = targetOnCol - targetTableCol - asAliasLen;

            // Compute = alignment: tab-stop after the longest LHS across all conditions in the block.
            int maxCondLHSLen = 0;
            foreach (var item in items)
                foreach (var cond in item.Conditions)
                    if (TrySplitAtEq(cond, out string lhs, out _))
                        maxCondLHSLen = Math.Max(maxCondLHSLen, lhs.Length);
            int targetEqOffset = maxCondLHSLen > 0 ? ((maxCondLHSLen / indentSize) + 1) * indentSize : 0;

            // ---- Pass 4: produce new lines ---------------------------------
            var result = new List<string>();
            int itemIdx = 0;
            int condIdx = 0;
            bool insideIndentedOn = false;
            bool firstCondConsumed = false;

            for (int i = 0; i < blockLines.Count; i++)
            {
                string trimmedUpper = blockLines[i].TrimStart().ToUpperInvariant();
                bool isFromJoin = IsFromOrJoinLine(trimmedUpper);

                if (insideIndentedOn)
                {
                    if (isFromJoin)
                    {
                        // Next JOIN/FROM line — exit indented-ON mode and fall through.
                        insideIndentedOn = false;
                        firstCondConsumed = false;
                    }
                    else if (!firstCondConsumed)
                    {
                        // Original first-condition line — already pulled up to the JOIN line above; skip it.
                        firstCondConsumed = true;
                        continue;
                    }
                    else if (IsJoinContinuationLine(trimmedUpper))
                    {
                        // AND/OR continuation: align under ON keyword with = alignment.
                        var prevItem = items[itemIdx - 1];
                        string andIndent = prevItem.Indent + new string(' ', targetOnCol);
                        string condText = condIdx < prevItem.Conditions.Count
                            ? ApplyEqPad(prevItem.Conditions[condIdx++], targetEqOffset)
                            : blockLines[i].TrimStart().Substring(blockLines[i].TrimStart().IndexOf(' ') + 1);
                        result.Add(andIndent + KW_AND + " " + condText);
                        continue;
                    }
                    else
                    {
                        result.Add(blockLines[i]);
                        continue;
                    }
                }

                if (!isFromJoin && IsJoinContinuationLine(trimmedUpper))
                {
                    // Inline-ON overflow continuation (non-IndentJoinOnClause wrapping).
                    string contIndent = items[itemIdx > 0 ? itemIdx - 1 : 0].Indent + new string(' ', targetOnCol);
                    result.Add(contIndent + blockLines[i].TrimStart());
                    continue;
                }

                if (!isFromJoin)
                {
                    result.Add(blockLines[i]);
                    continue;
                }

                var curItem = items[itemIdx++];
                condIdx = 0;
                string kwTable = curItem.Keyword + " " + curItem.Table;
                string tablepad = new string(' ', targetTableCol - kwTable.Length);
                string aliasPad = new string(' ', maxAliasLen - curItem.Alias.Length);
                //no alias (AlignTableJoinsAddAliases=false): pad so ON stays aligned
                string asPart = string.IsNullOrEmpty(curItem.Alias)
                    ? new string(' ', KW_AS.Length + 1 + maxAliasLen)
                    : KW_AS + " " + curItem.Alias + aliasPad;
                string onPrefix = new string(' ', onPad) + KW_ON + "  ";

                if (curItem.HasTrailingOn)
                {
                    // Pull first condition inline with = alignment.
                    string cond0 = curItem.Conditions.Count > 0
                        ? ApplyEqPad(curItem.Conditions[0], targetEqOffset)
                        : curItem.FirstCondition;
                    result.Add(curItem.Indent + kwTable + tablepad + asPart + onPrefix + cond0);
                    condIdx = 1;
                    insideIndentedOn = true;
                    firstCondConsumed = false;
                }
                else if (string.IsNullOrEmpty(curItem.OnClause))
                {
                    // No ON follows on this line — the alignment padding (and empty alias
                    // slot) would become trailing whitespace; trim it.
                    result.Add((curItem.Indent + kwTable + tablepad + asPart).TrimEnd());
                }
                else
                {
                    string onBase = curItem.Indent + kwTable + tablepad + asPart + onPrefix;
                    string onFull = onBase + curItem.OnClause;
                    if (onFull.Length > 100 && curItem.Conditions.Count > 1)
                    {
                        // Wrap onto separate lines with = alignment.
                        string onIndent = new string(' ', targetOnCol);
                        result.Add(onBase + ApplyEqPad(curItem.Conditions[0], targetEqOffset));
                        for (int c = 1; c < curItem.Conditions.Count; c++)
                            result.Add(curItem.Indent + onIndent + KW_AND + " " + ApplyEqPad(curItem.Conditions[c], targetEqOffset));
                    }
                    else
                    {
                        // Single condition or short line — apply = alignment inline.
                        string aligned = curItem.Conditions.Count > 0
                            ? ApplyEqPad(curItem.Conditions[0], targetEqOffset)
                            : curItem.OnClause;
                        result.Add(onBase + aligned);
                    }
                }
            }

            return result;
        }

        private sealed class JoinLineItem
        {
            public string Indent { get; set; } = "";
            public string Keyword { get; set; } = "";
            public string Table { get; set; } = "";
            public string Alias { get; set; } = "";
            public string OnClause { get; set; } = "";
            public string FirstCondition { get; set; } = "";
            public List<string> Conditions { get; set; } = new List<string>();
            public bool HasTrailingOn { get; set; }
        }

        /// <summary>
        /// Splits "lhs = rhs" at the first standalone = (not part of !=, &lt;=, &gt;=, &lt;&gt;).
        /// Returns false if no such = is found.
        /// </summary>
        private static bool TrySplitAtEq(string condition, out string lhs, out string rhs)
        {
            for (int i = 1; i < condition.Length - 2; i++)
            {
                if (condition[i] == ' ' && condition[i + 1] == '=' && condition[i + 2] == ' ')
                {
                    char prev = condition[i - 1];
                    if (prev != '!' && prev != '<' && prev != '>' && prev != '=')
                    {
                        lhs = condition.Substring(0, i);
                        rhs = condition.Substring(i + 3);
                        return true;
                    }
                }
            }
            lhs = condition;
            rhs = "";
            return false;
        }

        /// <summary>
        /// Rewrites "lhs = rhs" so that = starts at targetOffset characters from the start of the expression.
        /// Conditions without = are returned unchanged.
        /// </summary>
        private static string ApplyEqPad(string condition, int targetOffset)
        {
            if (targetOffset <= 0 || !TrySplitAtEq(condition, out string lhs, out string rhs))
                return condition;
            int pad = Math.Max(1, targetOffset - lhs.Length);
            return lhs + new string(' ', pad) + "= " + rhs;
        }

        /// <summary>
        /// Parses a single FROM/JOIN trimmed line into its components.
        /// hasTrailingOn is true when IndentJoinOnClause mode emits "JOIN table alias ON" with no condition on this line.
        /// </summary>
        private static void ParseFromJoinLine(string trimmed, out string keyword, out string table, out string alias, out string onClause, out bool hasTrailingOn)
        {
            // Keywords: FROM, JOIN, INNER JOIN, LEFT JOIN, LEFT OUTER JOIN, RIGHT JOIN,
            //           RIGHT OUTER JOIN, FULL JOIN, FULL OUTER JOIN, CROSS JOIN
            string[] keywords = new string[] {
                "LEFT OUTER JOIN", "RIGHT OUTER JOIN", "FULL OUTER JOIN",
                "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN", "CROSS JOIN",
                "JOIN", "FROM"
            };

            keyword = "";
            string rest = trimmed;
            string upper = trimmed.ToUpperInvariant();
            foreach (string kw in keywords)
            {
                if (upper.StartsWith(kw + " ") || upper == kw)
                {
                    keyword = trimmed.Substring(0, kw.Length); // preserve casing
                    rest = trimmed.Substring(kw.Length).TrimStart();
                    break;
                }
            }

            if (string.IsNullOrEmpty(keyword))
            {
                // Fallback: take first word as keyword
                int sp = trimmed.IndexOf(' ');
                keyword = sp >= 0 ? trimmed.Substring(0, sp) : trimmed;
                rest = sp >= 0 ? trimmed.Substring(sp + 1).TrimStart() : "";
            }

            // Detect trailing " ON" with no condition (IndentJoinOnClause mode).
            string restTrimmed = rest.TrimEnd();
            string restUpper = restTrimmed.ToUpperInvariant();
            if (restUpper.EndsWith(" ON"))
            {
                hasTrailingOn = true;
                onClause = "";
                string tableAndAlias = restTrimmed.Substring(0, restTrimmed.Length - 3).TrimEnd();
                ParseTableAndAlias(tableAndAlias, out table, out alias);
                return;
            }

            hasTrailingOn = false;
            // Now rest = "tableName [AS] [alias] [ON ...]" or "tableName [AS] [alias]"
            // Split on " ON " (case-insensitive, top-level)
            int onPos = FindTokenOutsideParens(rest, " ON ");
            onClause = onPos >= 0 ? rest.Substring(onPos + 4).Trim() : "";
            string tableAndAlias2 = onPos >= 0 ? rest.Substring(0, onPos).TrimEnd() : rest.TrimEnd();

            // Parse table [AS] alias from tableAndAlias
            ParseTableAndAlias(tableAndAlias2, out table, out alias);
        }

        private static void ParseTableAndAlias(string tableAndAlias, out string table, out string alias)
        {
            // Tokens: tableName [AS] [alias]
            // Could be: schema.Table, [schema].[Table], tableName, tableName alias, tableName AS alias,
            // or a table-valued function call: dbo.f(@a, @b) alias — the call's argument list
            // contains spaces and commas, so all splitting must be paren-aware or content
            // between the first and last token would be silently DROPPED.
            tableAndAlias = tableAndAlias.Trim();

            // Look for " AS " (with spaces) outside any parens (CAST(x AS y) can appear in TVF args)
            int tryAs = FindTokenOutsideParens(tableAndAlias, " AS ");
            if (tryAs >= 0)
            {
                table = tableAndAlias.Substring(0, tryAs).Trim();
                alias = tableAndAlias.Substring(tryAs + 4).Trim();
                return;
            }
            // No AS keyword — split at the LAST whitespace that sits at paren depth 0;
            // everything before it is the table expression, the final token is the alias.
            int depth = 0;
            int lastTopLevelSpace = -1;
            for (int i = 0; i < tableAndAlias.Length; i++)
            {
                char c = tableAndAlias[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (depth == 0 && (c == ' ' || c == '\t')) lastTopLevelSpace = i;
            }
            if (lastTopLevelSpace > 0 && lastTopLevelSpace < tableAndAlias.Length - 1)
            {
                table = tableAndAlias.Substring(0, lastTopLevelSpace).TrimEnd();
                alias = tableAndAlias.Substring(lastTopLevelSpace + 1).Trim();
                // "t (nolock)" — a legacy hint is not an alias; keep it glued to the table
                // (writing "t as (nolock)" would be invalid SQL).
                if (alias.StartsWith("("))
                {
                    table = tableAndAlias;
                    alias = "";
                }
            }
            else
            {
                table = tableAndAlias;
                alias = "";
            }
        }

        /// <summary>
        /// Returns the base name of a (possibly schema-qualified) table name.
        /// e.g. "dbo.Employees" → "Employees", "[dbo].[Employees]" → "Employees"
        /// </summary>
        private static string BaseTableName(string table)
        {
            // Strip schema prefix: take the last dot-segment, remove brackets
            int dot = table.LastIndexOf('.');
            string name = dot >= 0 ? table.Substring(dot + 1) : table;
            return name.Trim('[', ']');
        }

        /// <summary>
        /// Finds the position of a multi-char token in <paramref name="s"/> outside parentheses.
        /// Case-insensitive.
        /// </summary>
        private static int FindTokenOutsideParens(string s, string token)
        {
            int depth = 0;
            string upper = s.ToUpperInvariant();
            string tokenUpper = token.ToUpperInvariant();
            for (int i = 0; i <= s.Length - token.Length; i++)
            {
                char c = s[i];
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth--; continue; }
                if (depth == 0 && upper.Substring(i, token.Length) == tokenUpper)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Splits an ON clause on top-level " AND " separators.
        /// </summary>
        private static List<string> SplitOnConditions(string onClause)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;
            string upper = onClause.ToUpperInvariant();
            for (int i = 0; i <= onClause.Length - 5; i++)
            {
                if (onClause[i] == '(') { depth++; continue; }
                if (onClause[i] == ')') { depth--; continue; }
                if (depth == 0 && upper.Substring(i, 5) == " AND ")
                {
                    result.Add(onClause.Substring(start, i - start).Trim());
                    start = i + 5;
                    i += 4; // skip " AND "
                }
            }
            result.Add(onClause.Substring(start).Trim());
            return result;
        }
    }
}
