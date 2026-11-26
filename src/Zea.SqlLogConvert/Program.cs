using System.Text;

namespace Zea.SqlLogConvert;

internal static class Program
{
    private const string Usage = "Usage: Zea.SqlLogConvert [--help]";

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "-h" or "--help")
        {
            Console.WriteLine(Usage);
            Console.WriteLine("Send one SQL log block to stdin. The converted SQL is written to stdout.");
            return 0;
        }

        if (args.Length > 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            var log = Console.In.ReadToEnd();
            var sql = SqlLogConverter.ConvertLog(log);
            Console.Write(sql);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
