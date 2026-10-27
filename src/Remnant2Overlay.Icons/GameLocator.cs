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

    public static string? FindPaksFolder(Action<string>? log = null)
    {
        foreach (string library in SteamLibraries())
        {
            string paks = Path.Combine([library, "steamapps", "common", GameFolder, .. PaksSubPath]);
            bool found = Directory.Exists(paks);
            log?.Invoke($"{(found ? "found" : "not at")} {paks}");
            if (found) return paks;
        }
        return null;
    }

    /// <summary>Accepts the game folder, its Content folder, or Paks itself, and returns the Paks folder.</summary>
    public static string? NormalizePaksFolder(string folder, Action<string>? log = null)
    {
        string[] candidates =
        [
            folder,
            Path.Combine(folder, "Paks"),
            Path.Combine(folder, "Content", "Paks"),
            Path.Combine([folder, .. PaksSubPath]),
        ];
        string? paks = candidates.FirstOrDefault(c => Directory.Exists(c) && Directory.EnumerateFiles(c, "*.pak").Any());
        log?.Invoke(paks is null ? $"no .pak files under {folder}" : $"found {paks}");
        return paks;
    }

    /// <summary>
    /// Looks for a .usmap mappings file: next to the game executable (where UE4SS writes Mappings.usmap),
    /// then in <paramref name="extraFolders"/>. Newest file wins.
    /// </summary>
    public static string? FindMappingsFile(string paksDir, params string[] extraFolders)
    {
        // Paks is <game>/Remnant2/Content/Paks; the executable lives in <game>/Remnant2/Binaries/Win64.
        string? gameRoot = Directory.GetParent(paksDir)?.Parent?.FullName;
        var folders = new List<string>(extraFolders);
        if (gameRoot is not null)
        {
            folders.Insert(0, Path.Combine(gameRoot, "Binaries", "Win64"));
            folders.Insert(1, Path.Combine(gameRoot, "Binaries", "Win64", "ue4ss"));
        }

        return folders.Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*.usmap", SearchOption.TopDirectoryOnly))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
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
