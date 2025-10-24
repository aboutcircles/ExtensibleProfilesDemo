namespace Circles.Profiles.Tool;

internal partial class Program
{
    private static async Task<int> Main(string[] args)
    {
        bool noArgs = args.Length == 0;
        bool helpRequested = !noArgs && (args[0] is "-h" or "--help");

        if (noArgs || helpRequested)
        {
            PrintUsage();
            return 1;
        }

        string cmd = args[0].ToLowerInvariant();
        string[] tail = args.Skip(1).ToArray();

        try
        {
            return cmd switch
            {
                "init-catalog"     => await CmdInitCatalogAsync(tail),
                "tombstone"        => await CmdTombstoneAsync(tail),
                "add-bad-product"  => await CmdAddBadProductAsync(tail),
                _                  => UnknownCommand(cmd)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        PrintUsage();
        return 1;
    }
}