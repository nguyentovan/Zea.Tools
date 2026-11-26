using System.Text;

namespace Zea.SqlFormat;

internal static class Program
{
    private const string Usage = "Usage: Zea.SqlFormat --dialect tsql | --dialect <name> [--preserve-tokens [--validate | --require-right-way]] | --dialects | --help";

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "-h" or "--help")
        {
            Console.WriteLine(Usage);
            Console.WriteLine("Send SQL to stdin. The formatted SQL is written to stdout.");
            Console.WriteLine("Use --dialects to list the supported dialect names.");
            Console.WriteLine("PostgreSQL validation checks lexical structure, SQL delimiters and output integrity, not the complete SQL grammar.");
            return 0;
        }

        bool listDialects = args.Length == 1 && args[0] == "--dialects";
        bool validate = args.Length == 4 && args[2] == "--preserve-tokens" && args[3] == "--validate";
        bool requireRightWay = args.Length == 4 && args[2] == "--preserve-tokens" && args[3] == "--require-right-way";
        bool preserveTokens = validate || requireRightWay || args.Length == 3 && args[2] == "--preserve-tokens";
        if (!listDialects && (!(args.Length == 2 || preserveTokens) || args[0] != "--dialect"))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        try
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;

            if (listDialects)
            {
                var dialects = SqlFormatter.GetDialects();
                foreach (var label in dialects)
                {
                    Console.WriteLine(label);
                }

                return 0;
            }

            var dialect = args[1].ToLowerInvariant();
            if (dialect == "postgresql")
            {
                dialect = "postgres";
            }

            var sql = Console.In.ReadToEnd();
            var formattedSql = preserveTokens
                ? SqlFormatter.FormatPreservingTokens(sql, dialect, validate, requireRightWay)
                : SqlFormatter.FormatSql(sql, dialect);

            Console.Write(formattedSql);
            return 0;
        }
        catch (UnsupportedSqlDialectException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
