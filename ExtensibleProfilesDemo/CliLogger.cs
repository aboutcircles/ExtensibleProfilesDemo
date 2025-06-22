using System.Text.Json;

namespace ExtensibleProfilesDemo;

/// <summary>Appends ND-JSON log lines to ~/Desktop/circles_cli.log.</summary>
internal static class CliLogger
{
    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "circles_cli.log");

    public static void Log(object record)
    {
        string json = JsonSerializer.Serialize(record);
        File.AppendAllText(LogPath, json + Environment.NewLine);
    }
}