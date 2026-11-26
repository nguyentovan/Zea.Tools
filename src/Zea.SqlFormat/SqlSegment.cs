namespace Zea.SqlFormat;

internal sealed class UnsupportedSqlDialectException(string message) : Exception(message);

internal sealed record SqlSegment(
    string Kind,
    string? Text,
    SqlSegment[]? Children)
{
    public IReadOnlyList<SqlSegment> Parts
    {
        get
        {
            var parts = Children ?? Array.Empty<SqlSegment>();
            return parts;
        }
    }

    internal static void AppendWhitespace(List<SqlSegment> destination, string text)
    {
        int start = 0;
        while (start < text.Length)
        {
            int end = start + 1;
            string kind = "whitespace";
            if (text[start] is '\r' or '\n')
            {
                kind = "newline";
                if (text[start] == '\r' && end < text.Length && text[end] == '\n')
                {
                    end++;
                }
            }
            else
            {
                while (end < text.Length && text[end] is not ('\r' or '\n'))
                {
                    end++;
                }
            }

            var piece = new SqlSegment(kind, text[start..end], null);
            destination.Add(piece);
            start = end;
        }
    }

    public string Raw()
    {
        if (Text != null)
        {
            return Text;
        }

        var raw = string.Concat(Parts.Select(part => part.Raw()));
        return raw;
    }
}
