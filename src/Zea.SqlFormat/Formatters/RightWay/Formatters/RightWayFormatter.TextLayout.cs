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
        private string ApplyTextLayout(string output)
        {
            if (!Options.HTMLColoring)
            {
                if (Options.ColumnAlwaysHasAlias)
                    output = EnsureColumnAliases(output);
                if (Options.ColumnAliasStyle == ColumnAliasStyle.EqualSign)
                    output = RewriteAliasesToEqualSign(output);
                if (Options.AlignColumnDefinitions)
                    output = AlignSelectColumns(output);
                if (Options.AlignTableJoins)
                    output = AlignFromJoinClauses(output);
                output = WrapOverflowingAliasLiterals(output);
            }

            return output;
        }

        /// <summary>
        /// For each output line, true when the line STARTS inside an open single-quoted
        /// string literal (multi-line dynamic SQL) or an open /* block comment */.
        /// The text-based post-processing passes must leave such lines untouched:
        /// "SELECT ..." inside a dynamic-SQL string is DATA, and rewriting it corrupts
        /// the SQL's meaning. Bracket-quoted identifiers and -- comments are handled
        /// within each line; block comments nest, per T-SQL rules.
        /// </summary>
        private static bool[] ComputeLinesInsideStringOrComment(IList<string> lines)
        {
            bool[] mask = new bool[lines.Count];
            bool inString = false;
            int commentDepth = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                mask[i] = inString || commentDepth > 0;
                string line = lines[i];
                for (int j = 0; j < line.Length; j++)
                {
                    char c = line[j];
                    char next = j + 1 < line.Length ? line[j + 1] : '\0';
                    if (inString)
                    {
                        if (c == '\'' && next == '\'') { j++; continue; } //escaped quote
                        if (c == '\'') inString = false;
                    }
                    else if (commentDepth > 0)
                    {
                        if (c == '*' && next == '/') { commentDepth--; j++; }
                        else if (c == '/' && next == '*') { commentDepth++; j++; }
                    }
                    else
                    {
                        if (c == '\'') inString = true;
                        else if (c == '[') { while (j + 1 < line.Length && line[j + 1] != ']') j++; if (j + 1 < line.Length) j++; }
                        else if (c == '-' && next == '-') break; //rest of line is comment
                        else if (c == '/' && next == '*') { commentDepth++; j++; }
                    }
                }
            }
            return mask;
        }

        /// <summary>
        /// A line is untouchable by text passes when it STARTS inside an open string/comment
        /// (interior of multi-line dynamic SQL) or ENDS inside one (the boundary line that
        /// OPENS the literal): restructuring either moves quote/comment delimiters across
        /// lines and corrupts the SQL.
        /// </summary>
        private static bool LineTouchesStringOrComment(bool[] insideMask, int i)
        {
            return insideMask[i] || (i + 1 < insideMask.Length && insideMask[i + 1]);
        }

        /// <summary>
        /// Splits into lines while remembering each line's ORIGINAL terminator
        /// (index-aligned with the returned list; final line gets ""). The text
        /// post-passes only rewrite structural (SQL) lines, never lines inside a
        /// multi-line string literal - but the old Split(...).Join(Environment.NewLine)
        /// silently rewrote the literal's interior \r\n to \n, which both altered the
        /// literal's value (dynamic-SQL text the user pastes back) and shifted its
        /// measured width, oscillating across idempotency passes. Pair with
        /// JoinLinesPreservingEndings so untouched lines keep their bytes verbatim.
        /// </summary>
        private static List<string> SplitLinesPreservingEndings(string text, out List<string> endings)
        {
            var lines = new List<string>();
            endings = new List<string>();
            // Separators match Split(new[]{"\r\n","\n"}) exactly: \r\n and lone \n,
            // but NOT a lone \r (kept as an ordinary char, as the old code did).
            int start = 0, i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\n')
                {
                    lines.Add(text.Substring(start, i - start));
                    endings.Add("\n"); i++; start = i;
                }
                else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    lines.Add(text.Substring(start, i - start));
                    endings.Add("\r\n"); i += 2; start = i;
                }
                else i++;
            }
            lines.Add(text.Substring(start));
            endings.Add("");
            return lines;
        }

        private static string JoinLinesPreservingEndings(IList<string> lines, IList<string> endings)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                sb.Append(lines[i]);
                // A pass that changed the line count (only EnsureColumnAliases does, and
                // it keeps `endings` in sync) still has a matching terminator; the guard
                // is a belt-and-braces fallback to the platform newline.
                sb.Append(i < endings.Count ? endings[i] : Environment.NewLine);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Aligns DDL column definitions so that column names, data types, and nullability
        /// are in vertical columns.  Input is the formatted content of a DDLDETAIL_PARENS block
        /// (without the surrounding parens).
        /// Each row: indent + [comma] + colname + datatype + [constraint...]
        /// We align: colname and datatype into two columns.
        /// </summary>
        private string AlignDdlColumns(string ddlContent)
        {
            var lines = SplitLinesPreservingEndings(ddlContent, out var lineEndings);
            // interior lines of a multi-line /* ... */ comment must never be treated as
            // column definitions or padded (their content would ratchet rightward each pass)
            bool[] insideStringOrComment = ComputeLinesInsideStringOrComment(lines);

            // Each "column definition" line has: indent + optional-comma + name + type + rest
            // We identify lines that look like a column definition (first token is an identifier, second is a data type).
            // For simplicity: lines that aren't blank and don't start with a keyword that's a constraint.
            var colLines = new List<int>();
            var colNames = new List<string>();
            var afterNames = new List<string>();

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (LineTouchesStringOrComment(insideStringOrComment, i)) continue;

                // Strip indent and optional comma
                int pos = 0;
                while (pos < line.Length && (line[pos] == '\t' || line[pos] == ' ')) pos++;
                if (pos >= line.Length) continue;

                // comment-only lines (/* ... */, -- ...) are not column definitions and must
                // never be padded — otherwise their content ratchets rightward every pass
                if (line[pos] == '/' && pos + 1 < line.Length && line[pos + 1] == '*') continue;
                if (line[pos] == '-' && pos + 1 < line.Length && line[pos + 1] == '-') continue;

                bool hasComma = line[pos] == ',';
                if (hasComma) { pos++; while (pos < line.Length && line[pos] == ' ') pos++; }

                // First token: identifier (column name) or bracket-quoted
                string firstToken = ExtractFirstToken(line, pos, out int afterFirst);
                if (string.IsNullOrEmpty(firstToken)) continue;

                // A real column name never contains a string-literal quote. When a computed
                // column's expression wraps, a continuation line can BEGIN with a string
                // literal (e.g. `N'EXEC dbo...'`); ExtractFirstToken splits it at the space
                // INSIDE the literal, so padding this "column" injects spaces inside the
                // string - corrupting its value and ratcheting it rightward on every pass
                // (unbounded, non-convergent). Such lines are expression continuations, not
                // column definitions: skip them.
                if (firstToken.IndexOf('\'') >= 0) continue;

                // Skip constraint lines (CONSTRAINT, PRIMARY, UNIQUE, etc.)
                string upper = firstToken.TrimStart('[').TrimEnd(']').ToUpperInvariant();
                if (upper == "CONSTRAINT" || upper == "PRIMARY" || upper == "UNIQUE"
                    || upper == "CHECK" || upper == "FOREIGN" || upper == "DEFAULT"
                    || upper == ")") continue;

                colLines.Add(i);
                int indentAndComma = pos;
                colNames.Add(line.Substring(0, afterFirst)); // full prefix including indent+comma+name
                afterNames.Add(line.Substring(afterFirst));
            }

            if (colLines.Count < 2) return ddlContent;

            // Measure max name length (just the name token width, excluding indent+comma)
            int maxNameLen = 0;
            for (int i = 0; i < colLines.Count; i++)
            {
                string fullPrefix = colNames[i];
                // Find just the name part (after last space before the name)
                int nameStart = fullPrefix.Length - 1;
                while (nameStart > 0 && fullPrefix[nameStart - 1] != ' ' && fullPrefix[nameStart - 1] != ','
                    && fullPrefix[nameStart - 1] != '\t')
                    nameStart--;
                string namePart = fullPrefix.Substring(nameStart);
                maxNameLen = Math.Max(maxNameLen, namePart.TrimEnd().Length);
            }

            for (int i = 0; i < colLines.Count; i++)
            {
                string fullPrefix = colNames[i];
                int nameStart = fullPrefix.Length - 1;
                while (nameStart > 0 && fullPrefix[nameStart - 1] != ' ' && fullPrefix[nameStart - 1] != ','
                    && fullPrefix[nameStart - 1] != '\t')
                    nameStart--;
                string beforeName = fullPrefix.Substring(0, nameStart);
                string namePart = fullPrefix.Substring(nameStart).TrimEnd();
                string padding = new string(' ', Math.Max(1, maxNameLen - namePart.Length + 1));
                lines[colLines[i]] = beforeName + namePart + padding + afterNames[i].TrimStart();
            }

            return JoinLinesPreservingEndings(lines, lineEndings);
        }

        private static string ExtractFirstToken(string line, int startPos, out int endPos)
        {
            endPos = startPos;
            if (startPos >= line.Length) return string.Empty;

            if (line[startPos] == '[')
            {
                int end = line.IndexOf(']', startPos);
                if (end < 0) { endPos = line.Length; return line.Substring(startPos); }
                endPos = end + 1;
                while (endPos < line.Length && line[endPos] == ' ') endPos++;
                return line.Substring(startPos, end - startPos + 1);
            }

            int tokenEnd = startPos;
            while (tokenEnd < line.Length && line[tokenEnd] != ' ' && line[tokenEnd] != '\t')
                tokenEnd++;
            string token = line.Substring(startPos, tokenEnd - startPos);
            endPos = tokenEnd;
            while (endPos < line.Length && line[endPos] == ' ') endPos++;
            return token;
        }
    }
}
