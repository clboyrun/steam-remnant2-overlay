using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Remnant2Overlay.Icons;

/// <summary>Finds the Remnant 2 Paks folder through Steam's library list.</summary>
public static partial class GameLocator
{
    private const string GameFolder = "Remnant2";
    private static readonly string[] PaksSubPath = ["Remnant2", "Content", "Paks"];

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPathRegex();

    public static string? FindPaksFolder()
    {
        foreach (string library in SteamLibraries())
        {
            string paks = Path.Combine([library, "steamapps", "common", GameFolder, .. PaksSubPath]);
            if (Directory.Exists(paks)) return paks;
        }
        return null;
    }

    /// <summary>Accepts the game folder, its Content folder, or Paks itself, and returns the Paks folder.</summary>
    public static string? NormalizePaksFolder(string folder)
    {
        string[] candidates =
        [
            folder,
            Path.Combine(folder, "Paks"),
            Path.Combine(folder, "Content", "Paks"),
            Path.Combine([folder, .. PaksSubPath]),
        ];
        return candidates.FirstOrDefault(c => Directory.Exists(c) && Directory.EnumerateFiles(c, "*.pak").Any());
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string userPath)
                roots.Add(userPath.Replace('/', '\\'));
            if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) is string machinePath)
                roots.Add(machinePath);
        }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Where(Directory.Exists))
        {
            if (seen.Add(root)) yield return root;

            // Extra libraries on other drives are listed in libraryfolders.vdf.
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in LibraryPathRegex().Matches(File.ReadAllText(vdf)))
            {
                string library = m.Groups[1].Value.Replace(@"\\", @"\");
                if (seen.Add(library)) yield return library;
            }
        }
    }
}
