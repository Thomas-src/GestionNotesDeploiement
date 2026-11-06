using System.Diagnostics;
using System.IO.Compression;

namespace Updater;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 8)
        {
            Console.Error.WriteLine("Arguments invalides.");
            return 2;
        }

        int pid = int.Parse(Get(args, "--pid"));
        string zipPath = Get(args, "--zip");
        string installDir = Get(args, "--install-dir");
        string exeName = Get(args, "--exe");

        string extractDir = Path.Combine(Path.GetTempPath(), "GestionNotes", "updater-extract");
        bool success = true;

        try
        {
            WaitForExit(pid);

            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }

            ZipFile.ExtractToDirectory(zipPath, extractDir);

            string sourceDir = GetSourceRoot(extractDir);
            foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, file);
                if (!relative.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
                {
                    string target = Path.Combine(installDir, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: true);
                }       
            }
        }
        catch (Exception)
        {
            Console.Error.WriteLine("La mise à jour a échoué.");
            success = false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(installDir, exeName),
            WorkingDirectory = installDir,
            UseShellExecute = true
        });

        try
        {
            Directory.Delete(extractDir, recursive: true);
        }
        catch (Exception)
        {
        }

        return success ? 0 : 1;
    }

    private static string Get(string[] args, string key)
    {
        int index = Array.IndexOf(args, key);
        return args[index + 1];
    }

    private static void WaitForExit(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit();
        }
        catch (ArgumentException)
        {
        }
    }

    private static string GetSourceRoot(string extractDir)
    {
        var entries = Directory.GetFileSystemEntries(extractDir);
        return entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : extractDir;
    }
}