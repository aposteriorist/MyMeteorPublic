using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace MyMeteor.IO;

public class Backup
{
    internal const string DefaultExtension = ".old";

    /// <summary>
    /// Backup the file with automatic numbering.
    /// </summary>
    /// <remarks>A maximum of 1,000 automatic backups can exist in one location, numbered 000 to 999.</remarks>
    static public bool NewTop(string filepath, string extension = "")
    {
        if (!FileExistsWithMessage(filepath)) return false;

        if (string.IsNullOrEmpty(extension)) extension = DefaultExtension;

        string dir = Path.GetDirectoryName(filepath);
        string filename = Path.GetFileName(filepath);

        var prevFiles = Directory.GetFiles(dir).Where(s
            => Path.GetFileName(s).StartsWith(filename, StringComparison.OrdinalIgnoreCase)
            && s.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        int count = prevFiles.Count();
        if (count > 999)
        {
            Console.WriteLine($"Backup error: too many automatic backups exist for {filepath}. No work was performed.");
            return false;
        }

        string backupFilepath = $"{filepath}.{count:d3}{extension}";

        if (File.Exists(backupFilepath))
        {
            int openNum = 0;
            while (File.Exists($"{filepath}.{openNum:d3}{extension}"))
            {
                openNum++;
            }
            backupFilepath = $"{filepath}.{openNum:d3}{extension}";
            Console.WriteLine($"Backup warning: automatic file number {openNum:d3} will be used, since it is the first open slot.");
        }

        File.Copy(filepath, backupFilepath);
        Console.WriteLine($"\"{filepath}\" backed up as \"{backupFilepath}\".");

        return true;
    }

    /// <summary>
    /// Backup the file with automatic numbering of 000, incrementing every existing backup's number.
    /// </summary>
    /// <remarks>A maximum of 1,000 automatic backups can exist in one location, numbered 000 to 999.</remarks>
    static public bool NewBottom(string filepath, string extension = "")
    {
        if (!FileExistsWithMessage(filepath)) return false;

        if (string.IsNullOrEmpty(extension)) extension = DefaultExtension;

        string dir = Path.GetDirectoryName(filepath);
        string filename = Path.GetFileName(filepath);

        var prevFiles = Directory.GetFiles(dir).Where(s
            => Path.GetFileName(s).StartsWith(filename, StringComparison.OrdinalIgnoreCase)
            && s.EndsWith(extension, StringComparison.OrdinalIgnoreCase)).ToList();

        int count = prevFiles.Count;
        if (count > 999)
        {
            Console.WriteLine($"Backup error: too many automatic backups exist for {filepath}. No work was performed.");
            return false;
        }

        prevFiles.Sort();
        prevFiles.Reverse();

        for (int i = count; i > 0; i--)
        {
            File.Move(prevFiles[i], $"{filepath}.{i:d3}{extension}");
        }

        File.Copy(filepath, $"{filepath}.{count:d3}{extension}");
        Console.WriteLine($"\"{filepath}\" backed up as \"{filepath}{extension}\".");

        return true;
    }

    static private bool FileExistsWithMessage(string filepath)
    {
        bool result = File.Exists(filepath);

        if (!result) Console.WriteLine($"Backup error: the file \"{filepath}\" does not exist. No work was performed.");

        return result;
    }
}
