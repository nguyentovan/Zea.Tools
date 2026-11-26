namespace Zea.SqlFormat.Postgres;

internal static partial class PostgresFormatter
{
    private static Document Read(string source, bool backslashStrings)
    {
        var atoms = new List<Atom>();
        bool ordinaryBackslash = false;
        bool preserveSource = false;
        int position = 0;
        int statementStart = 0;
        int depth = 0;
        while (position < source.Length)
        {
            if (IsSpace(source[position]))
            {
                position++;
                continue;
            }

            int start = position;
            char value = source[position];
            Kind kind;
            if (value == '-' && position + 1 < source.Length && source[position + 1] == '-')
            {
                kind = Kind.LineComment;
                position += 2;
                while (position < source.Length && source[position] is not '\r' and not '\n')
                {
                    position++;
                }
            }
            else if (value == '/' && position + 1 < source.Length && source[position + 1] == '*')
            {
                kind = Kind.BlockComment;
                position = BlockCommentEnd(source, start);
            }
            else if (value == '\'' || (value is 'e' or 'E' or 'n' or 'N' or 'b' or 'B' or 'x' or 'X'
                && position + 1 < source.Length && source[position + 1] == '\''))
            {
                kind = Kind.String;
                char prefix = value == '\'' ? '\0' : char.ToUpperInvariant(value);
                int quote = prefix == '\0' ? position : position + 1;
                bool ordinary = prefix is '\0' or 'N';
                position = StringEnd(source, start, quote, prefix == 'E' || ordinary && backslashStrings,
                    doubledQuotes: prefix is not 'B' and not 'X', ordinary, ref ordinaryBackslash);
            }
            else if (value is 'u' or 'U' && position + 2 < source.Length && source[position + 1] == '&'
                && source[position + 2] is '\'' or '"')
            {
                if (source[position + 2] == '"')
                {
                    kind = Kind.QuotedName;
                    position = QuotedNameEnd(source, position + 2, start);
                }
                else
                {
                    kind = Kind.String;
                    position = StringEnd(source, start, position + 2, escapes: false, doubledQuotes: true,
                        ordinary: false, ref ordinaryBackslash);
                }
            }
            else if (value == '"')
            {
                kind = Kind.QuotedName;
                position = QuotedNameEnd(source, position, start);
            }
            else if (IsIdentifierStart(value))
            {
                kind = Kind.Word;
                while (++position < source.Length && IsIdentifierPart(source[position])) { }
            }
            else if (value == '$' && position + 1 < source.Length && IsDigit(source[position + 1]))
            {
                kind = Kind.Parameter;
                position += 2;
                while (position < source.Length && IsDigit(source[position]))
                {
                    position++;
                }

                if (position < source.Length && IsIdentifierStart(source[position]))
                {
                    throw Error(source, start, "Trailing junk after a parameter.");
                }
            }
            else if (value == '$' && DollarDelimiterEnd(source, position) is int delimiterEnd && delimiterEnd > position)
            {
                kind = Kind.DollarString;
                string delimiter = source.Substring(position, delimiterEnd - position);
                int close = source.IndexOf(delimiter, delimiterEnd, StringComparison.Ordinal);
                if (close < 0)
                {
                    throw Error(source, start, "Unterminated dollar-quoted string.");
                }

                position = close + delimiter.Length;
            }
            else if (value == ':' && position + 2 < source.Length && source[position + 1] == '{' && source[position + 2] == '?')
            {
                int close = source.IndexOf('}', position + 3);
                if (close < 0)
                {
                    throw Error(source, start, "Unterminated client variable envelope.");
                }
                kind = Kind.ClientVariable;
                position = close + 1;
            }
            else if (IsDigit(value) || value == '.' && position + 1 < source.Length && IsDigit(source[position + 1]))
            {
                kind = Kind.Number;
                position = NumberEnd(source, position);
            }
            else if (IsOperator(value))
            {
                kind = Kind.Operator;
                position++;
                while (position < source.Length && IsOperator(source[position]))
                {
                    if (position + 1 < source.Length && (source[position] == '/' && source[position + 1] == '*'
                        || source[position] == '-' && source[position + 1] == '-'))
                    {
                        break;
                    }

                    position++;
                }

                if (position - start > 1 && source[position - 1] is '+' or '-')
                {
                    bool custom = false;
                    for (int index = start; index < position - 1; index++)
                    {
                        custom |= source[index] is '~' or '!' or '@' or '#' or '^' or '&' or '|' or '`' or '?' or '%';
                    }

                    if (!custom)
                    {
                        while (position - start > 1 && source[position - 1] is '+' or '-')
                        {
                            position--;
                        }
                    }
                }
            }
            else
            {
                kind = Kind.Punctuation;
                position++;
                if (position < source.Length && (value == ':' && source[position] is ':' or '='
                    || value == '.' && source[position] == '.'))
                {
                    kind = Kind.Operator;
                    position++;
                }

                if (value == '\\')
                {
                    throw Error(source, start, "psql commands are not supported.");
                }
            }

            string key = kind is Kind.Word or Kind.Operator or Kind.Punctuation
                ? source.Substring(start, position - start).ToUpperInvariant()
                : string.Empty;
            atoms.Add(new Atom(kind, new SourceSpan(start, position - start), key));

            if (kind == Kind.Punctuation)
            {
                if (value is '(' or '[')
                {
                    depth++;
                }
                else if (value is ')' or ']')
                {
                    depth--;
                }
                else if (value == ';' && depth == 0)
                {
                    preserveSource |= RejectCopyPayload(source, atoms, statementStart, position);
                    statementStart = atoms.Count;
                }
            }
        }

        preserveSource |= RejectCopyPayload(source, atoms, statementStart, source.Length);

        Atom[] tape = atoms.ToArray();
        var matches = new int[tape.Length];
        Array.Fill(matches, -1);
        var delimiters = new Stack<int>();
        for (int index = 0; index < tape.Length; index++)
        {
            string key = tape[index].Key;
            if (key is "(" or "[")
            {
                delimiters.Push(index);
            }
            else if (key is ")" or "]")
            {
                if (delimiters.Count == 0)
                {
                    throw Error(source, tape[index].Source.Start, "Unmatched closing delimiter.");
                }

                int open = delimiters.Pop();
                if (tape[open].Key != (key == ")" ? "(" : "["))
                {
                    throw Error(source, tape[index].Source.Start, "Mismatched SQL delimiters.");
                }

                matches[open] = index;
                matches[index] = open;
            }
        }

        if (delimiters.Count > 0)
        {
            throw Error(source, tape[delimiters.Peek()].Source.Start, "Unterminated SQL delimiter.");
        }

        var nextCode = new int[tape.Length + 1];
        int next = tape.Length;
        nextCode[tape.Length] = next;
        for (int index = tape.Length - 1; index >= 0; index--)
        {
            nextCode[index] = next;
            if (!tape[index].IsComment)
            {
                next = index;
            }
        }

        var previousCode = new int[tape.Length];
        int previous = -1;
        for (int index = 0; index < tape.Length; index++)
        {
            previousCode[index] = previous;
            if (!tape[index].IsComment)
            {
                previous = index;
            }
        }

        var gaps = new Gap[tape.Length + 1];
        string newline = "\n";
        bool foundNewline = false;
        bool noformat = false;
        bool compact = false;
        for (int index = 0; index <= tape.Length; index++)
        {
            int gapStart = index == 0 ? 0 : tape[index - 1].Source.End;
            int gapEnd = index == tape.Length ? source.Length : tape[index].Source.Start;
            var span = new SourceSpan(gapStart, gapEnd - gapStart);
            var gapText = source.AsSpan(span.Start, span.Length);
            if (!foundNewline)
            {
                int lineBreak = gapText.IndexOfAny('\r', '\n');
                if (lineBreak >= 0)
                {
                    newline = gapText[lineBreak] == '\r' ? lineBreak + 1 < gapText.Length && gapText[lineBreak + 1] == '\n' ? "\r\n" : "\r" : "\n";
                    foundNewline = true;
                }
            }

            GapRule rule = GapRule.Flexible;
            if (index == 0 || index == tape.Length || noformat)
            {
                rule = GapRule.Exact;
            }
            else
            {
                Atom left = tape[index - 1];
                Atom right = tape[index];
                if (left.IsComment || right.IsComment || left.Kind == Kind.ClientVariable || right.Kind == Kind.ClientVariable
                    || left.Key == ":" || right.Key == ":"
                    || IsOperatorBoundary(left) && IsOperatorBoundary(right)
                    || left.Key is "." or ".." && right.Kind == Kind.Number
                    || left.Kind == Kind.Number && right.Key is "." or ".."
                    || IsOther(left) || IsOther(right)
                    || left.Key == "U" && right.Key == "&" || left.Key == "&" && index > 1 && tape[index - 2].Key == "U")
                {
                    rule = GapRule.Exact;
                }
                else if (NeedsSeparator(left, right))
                {
                    rule = GapRule.Separator;
                }
            }

            gaps[index] = new Gap(span, rule, compact);
            if (index < tape.Length && tape[index].IsComment)
            {
                var comment = source.AsSpan(tape[index].Source.Start, tape[index].Source.Length);
                if (comment.Contains("[noformat]", StringComparison.OrdinalIgnoreCase))
                {
                    noformat = true;
                }
                if (comment.Contains("[/noformat]", StringComparison.OrdinalIgnoreCase))
                {
                    noformat = false;
                }
                if (comment.Contains("[minify]", StringComparison.OrdinalIgnoreCase))
                {
                    compact = true;
                }
                if (comment.Contains("[/minify]", StringComparison.OrdinalIgnoreCase))
                {
                    compact = false;
                }
            }
        }

        // A newline between any neighboring literal pieces can change concatenation.
        int lastLiteral = -1;
        for (int index = 0; index < tape.Length; index++)
        {
            if (tape[index].IsComment)
            {
                continue;
            }

            if (tape[index].Kind is Kind.String or Kind.DollarString && lastLiteral >= 0)
            {
                for (int gap = lastLiteral + 1; gap <= index; gap++)
                {
                    gaps[gap] = gaps[gap] with { Rule = GapRule.Exact };
                }
            }

            lastLiteral = tape[index].Kind is Kind.String or Kind.DollarString ? index : -1;
        }

        return new Document(source, tape, gaps, matches, nextCode, previousCode, ordinaryBackslash, preserveSource, newline);
    }

    private static bool IsOther(Atom atom) => atom.Kind == Kind.Punctuation && atom.Key is not ("," or ";" or "(" or ")" or "[" or "]" or "." or ":");
    private static bool IsOperatorBoundary(Atom atom) => atom.Kind == Kind.Operator || atom.Key is ":" or ".";

    private static bool NeedsSeparator(Atom left, Atom right)
    {
        if (left.Kind is Kind.Word or Kind.Number or Kind.Parameter)
        {
            return right.Kind is Kind.Word or Kind.Number or Kind.Parameter or Kind.DollarString
                || left.Kind == Kind.Word && right.Kind == Kind.String;
        }

        return left.Kind == Kind.QuotedName && right.Kind == Kind.QuotedName;
    }

    private static int BlockCommentEnd(string source, int start)
    {
        int depth = 1;
        int position = start + 2;
        while (position < source.Length)
        {
            if (position + 1 < source.Length && source[position] == '/' && source[position + 1] == '*')
            {
                depth++;
                position += 2;
            }
            else if (position + 1 < source.Length && source[position] == '*' && source[position + 1] == '/')
            {
                position += 2;
                if (--depth == 0)
                {
                    return position;
                }
            }
            else
            {
                position++;
            }
        }

        throw Error(source, start, "Unterminated block comment.");
    }

    private static int QuotedNameEnd(string source, int quote, int start)
    {
        int position = quote + 1;
        while (position < source.Length)
        {
            if (source[position++] != '"')
            {
                continue;
            }

            if (position < source.Length && source[position] == '"')
            {
                position++;
                continue;
            }

            if (position == quote + 2)
            {
                throw Error(source, start, "Empty quoted identifier.");
            }
            return position;
        }

        throw Error(source, start, "Unterminated quoted identifier.");
    }

    private static int StringEnd(string source, int start, int quote, bool escapes, bool doubledQuotes, bool ordinary, ref bool ordinaryBackslash)
    {
        int position = quote + 1;
        while (position < source.Length)
        {
            char value = source[position++];
            if (value == '\\')
            {
                ordinaryBackslash |= ordinary;
                if (escapes && position < source.Length)
                {
                    position++;
                }
            }
            else if (value == '\'')
            {
                if (doubledQuotes && position < source.Length && source[position] == '\'')
                {
                    position++;
                    continue;
                }

                int continuation = position;
                bool newline = false;
                while (continuation < source.Length)
                {
                    if (IsSpace(source[continuation]))
                    {
                        newline |= source[continuation] is '\r' or '\n';
                        continuation++;
                    }
                    else if (continuation + 1 < source.Length && source[continuation] == '-' && source[continuation + 1] == '-')
                    {
                        continuation += 2;
                        while (continuation < source.Length && source[continuation] is not '\r' and not '\n')
                        {
                            continuation++;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                if (newline && continuation < source.Length && source[continuation] == '\'')
                {
                    position = continuation + 1;
                    continue;
                }

                return position;
            }
        }

        throw Error(source, start, "Unterminated quoted string.");
    }

    private static int DollarDelimiterEnd(string source, int start)
    {
        int position = start + 1;
        if (position < source.Length && source[position] == '$')
        {
            return position + 1;
        }
        if (position >= source.Length || !IsIdentifierStart(source[position]))
        {
            return -1;
        }
        do
        {
            position++;
        } while (position < source.Length && (IsIdentifierStart(source[position]) || IsDigit(source[position])));
        return position < source.Length && source[position] == '$' ? position + 1 : -1;
    }

    private static int NumberEnd(string source, int start)
    {
        int position = start;
        if (source[start] == '0' && start + 1 < source.Length && source[start + 1] is 'x' or 'X' or 'o' or 'O' or 'b' or 'B')
        {
            char radix = char.ToUpperInvariant(source[start + 1]);
            position += 2;
            int digits = 0;
            while (position < source.Length)
            {
                if (source[position] == '_' && position + 1 < source.Length && RadixDigit(source[position + 1], radix))
                {
                    position++;
                }
                if (position < source.Length && RadixDigit(source[position], radix))
                {
                    digits++;
                    position++;
                }
                else
                {
                    break;
                }
            }
            if (digits == 0 || position < source.Length && (IsIdentifierPart(source[position]) || IsDigit(source[position])))
            {
                throw Error(source, start, "Invalid radix integer.");
            }
            return position;
        }

        position = DecimalDigits(source, position);
        if (position < source.Length && source[position] == '.' && !(position + 1 < source.Length && source[position + 1] == '.'))
        {
            position = DecimalDigits(source, position + 1);
        }
        if (position < source.Length && source[position] is 'e' or 'E')
        {
            position++;
            if (position < source.Length && source[position] is '+' or '-')
            {
                position++;
            }
            int exponent = position;
            position = DecimalDigits(source, position);
            if (position == exponent)
            {
                throw Error(source, start, "Invalid numeric exponent.");
            }
        }
        if (position < source.Length && IsIdentifierStart(source[position]))
        {
            throw Error(source, start, "Trailing junk after a numeric literal.");
        }
        return position;
    }

    private static int DecimalDigits(string source, int position)
    {
        while (position < source.Length && IsDigit(source[position]))
        {
            position++;
            if (position + 1 < source.Length && source[position] == '_' && IsDigit(source[position + 1]))
            {
                position++;
            }
        }
        return position;
    }

    private static bool RadixDigit(char value, char radix) => radix switch
    {
        'B' => value is '0' or '1',
        'O' => value is >= '0' and <= '7',
        _ => IsDigit(value) || value is >= 'a' and <= 'f' or >= 'A' and <= 'F'
    };

    private static bool RejectCopyPayload(string source, List<Atom> atoms, int first, int headerEnd)
    {
        int position = first;
        while (position < atoms.Count && atoms[position].IsComment)
        {
            position++;
        }
        if (position == atoms.Count || atoms[position].Key != "COPY")
        {
            return false;
        }

        int depth = 0;
        bool from = false;
        bool input = false;
        for (; position < atoms.Count; position++)
        {
            Atom atom = atoms[position];
            if (atom.IsComment)
            {
                continue;
            }
            if (atom.Key is "(" or "[")
            {
                depth++;
            }
            else if (atom.Key is ")" or "]")
            {
                depth--;
            }
            else if (depth == 0)
            {
                if (from && atom.Key == "STDIN")
                {
                    input = true;
                    for (int payload = headerEnd; payload < source.Length; payload++)
                    {
                        if (!IsSpace(source[payload]))
                        {
                            throw Error(source, atom.Source.Start, "Inline COPY FROM STDIN is not supported.");
                        }
                    }
                }
                from = atom.Key == "FROM";
            }
        }
        return input;
    }
}
