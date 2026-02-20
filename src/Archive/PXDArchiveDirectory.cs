using MyMeteor.IO;
using MyMeteor.IO.Compression;
using System.Collections.ObjectModel;

namespace MyMeteor.Archive;

/// <summary>
/// A class representing the header for a directory within an archive.
/// </summary>
public class PXDArchiveDirectoryHeader
{
    /// <summary>
    /// The default name for a root directory, based on the root directory mode in the settings.
    /// </summary>
    internal static string DefaultRootName => PXDArchiveOptions.RootDirectoryMode == PXDRootDirMode.WithDotName ? "." : "";

    /// <summary>
    /// The directory's name.
    /// </summary>
    public string Name { get; internal set; } = "";
    internal int DirCount = 0;
    internal int FirstDirIndex = 0;
    internal int FileCount = 0;
    internal int FirstFileIndex = 0;
    public FileAttributes Attributes { get; internal set; } = FileAttributes.Directory;

    /// <summary>
    /// Create a header from a reader over the text of an archive manifest, positioned appropriately.
    /// </summary>
    internal static PXDArchiveDirectoryHeader HeaderFromManifest(StreamReader reader)
    {
        PXDArchiveDirectoryHeader header = new()
        {
            Name = reader.ReadLine().Split().Last(),
            DirCount = int.Parse(reader.ReadLine().Split().Last()),
            FirstDirIndex = int.Parse(reader.ReadLine().Split().Last()),
            FileCount = int.Parse(reader.ReadLine().Split().Last()),
            FirstFileIndex = int.Parse(reader.ReadLine().Split().Last())
        };

        long position = reader.BaseStream.Position;
        string[] line = reader.ReadLine().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        if (line[0] == "Attr")
            header.Attributes = (FileAttributes)int.Parse(line.Last(), System.Globalization.NumberStyles.AllowHexSpecifier);
        else
            reader.BaseStream.Position = position;

        return header;
    }
}

/// <summary>
/// A virtual directory used as part of the file structure for an archive.
/// </summary>
public class PXDArchiveDirectory : PXDArchiveDirectoryHeader
{
    /// <summary>
    /// A read-only collection of the subdirectories in this directory.
    /// </summary>
    public ReadOnlyCollection<PXDArchiveDirectory> Subdirectories => _subdirectories.AsReadOnly();

    /// <summary>
    /// A read-only collection of the files in this directory.
    /// </summary>
    public ReadOnlyCollection<PXDArchiveFile> Files => _files.AsReadOnly();

    internal PXDArchiveDirectory[] _subdirectories;

    internal PXDArchiveFile[] _files;

    internal bool FileTreeInitialized = false;

    internal int FileTreeDirCount => DirCount + _subdirectories.Sum(subdir => subdir.FileTreeDirCount);
    internal int FileTreeFileCount => FileCount + _subdirectories.Sum(subdir => subdir.FileTreeFileCount);
    internal List<PXDArchiveDirectory[]> FileTreeDirectorySets;
    internal List<PXDArchiveFile> FileTreeFiles = [];

    /// <summary>
    /// Create a directory.
    /// </summary>
    public PXDArchiveDirectory(string name)
    {
        Name = name ?? DefaultRootName;
    }

    internal static PXDArchiveDirectory FromArchiveEntry(MyBinaryReader reader, string name)
    {
        PXDArchiveDirectory dir = new(name)
        {
            DirCount       = reader.ReadInt32(),
            FirstDirIndex  = reader.ReadInt32(),
            FileCount      = reader.ReadInt32(),
            FirstFileIndex = reader.ReadInt32(),
            Attributes     = (FileAttributes) reader.ReadUInt32(),
        };

        dir._subdirectories = new PXDArchiveDirectory[dir.DirCount];
        dir._files = new PXDArchiveFile[dir.FileCount];

        return dir;
    }

    internal static PXDArchiveDirectory FromDirectory(string dirPath, ref int dirCount, ref int fileCount, bool loadData = true, SLLZParameters? encodingParams = null)
    {
        PXDArchiveDirectory dir = new(Path.GetFileName(dirPath));

        DirectoryInfo dirInfo = new(dirPath);  // Not taking the directory's name from this because . would give us the actual directory name

        dir.FirstFileIndex = fileCount; // This is always true even if the directory has no files. (e.g. lua.par from LJ)
        dir.Attributes = dirInfo.Attributes;

        PushDirectoryAndGo(dir.Name);

        // Parse all files in the directory.
        var fileEntries = dirInfo.GetFiles();
        if (fileEntries.Length > 0)
        {
            dir._files = [.. fileEntries.Select((info) => PXDArchiveFile.FromFileInfo(info, dir, loadData, encodingParams))];
            dir.FileCount = dir._files.Length;
            fileCount += dir.FileCount;
            dir.FileTreeFiles.AddRange(dir._files);
        }

        else
        {
            dir._files = [];
        }

        // Parse all directories in the directory.
        string[] dirEntries = [.. Directory.EnumerateDirectories(Directory.GetCurrentDirectory())];
        if (dirEntries.Length > 0)
        {
            dir._subdirectories = new PXDArchiveDirectory[dirEntries.Length];
            int dirCountWithSubdirs = dirCount + dir._subdirectories.Length;
            dir.FirstDirIndex = dirCount + 1;
            dir.DirCount = dir._subdirectories.Length;
            dir.FileTreeDirectorySets = [dir._subdirectories];

            for (int i = 0; i < dir._subdirectories.Length; i++)
            {
                dir._subdirectories[i] = FromDirectory(dirEntries[i], ref dirCountWithSubdirs, ref fileCount, loadData, encodingParams);

                // Collect this subdirectory's subdirectories and files in sets to facilitate archive initialization via file tree.
                if (dir._subdirectories[i].FileTreeDirectorySets.Count != 0)
                    dir.FileTreeDirectorySets.AddRange(dir._subdirectories[i].FileTreeDirectorySets);

                if (dir._subdirectories[i].FileTreeFiles.Count != 0)
                    dir.FileTreeFiles.AddRange(dir._subdirectories[i].FileTreeFiles);
            }

            dirCount = dirCountWithSubdirs;
        }

        else
        {
            // Here FirstDirIndex should be set to the total directory count for the archive. (e.g. lua.par from LJ)
            // But we don't know that total yet, since we are actively calculating it.
            dir._subdirectories = [];
            dir.DirCount = 0;
            dir.FileTreeDirectorySets = [];
        }

        dir.FileTreeInitialized = true;

        PopDirectoryAndGo();

        return dir;
    }

    internal void ToArchiveEntry(MyBinaryWriter writer)
    {
        writer.Write(DirCount);
        writer.Write(FirstDirIndex);
        writer.Write(FileCount);
        writer.Write(FirstFileIndex);
        writer.Write((uint) Attributes);
        writer.Skip(0xc);
    }

    internal void ToManifest(StreamWriter writer, int tabCount)
    {
        string tab = new('\t', tabCount);
        writer.WriteLine($"{(tabCount > 0 ? tab[0..^1] : "")}<dir>");

        writer.WriteLine($"{tab}Name\t{Name}");
        writer.WriteLine($"{tab}DC\t{DirCount}");
        writer.WriteLine($"{tab}FDI\t{FirstDirIndex}");
        writer.WriteLine($"{tab}FC\t{FileCount}");
        writer.WriteLine($"{tab}FFI\t{FirstFileIndex}");
        if (Attributes != 0)
            writer.WriteLine($"{tab}Attr\t{Attributes:X}");

        foreach (var entry in _files)
            entry.ToManifest(writer, tabCount + 1);

        foreach (var dir in _subdirectories)
            dir.ToManifest(writer, tabCount + 1);

        writer.WriteLine($"{(tabCount > 0 ? tab[0..^1] : "")}</dir>");
    }

    internal void FlashFromManifest(StreamReader reader)
    {
        PXDArchiveDirectoryHeader manifestHeader = HeaderFromManifest(reader);

        if (!PXDArchiveOptions.SuppressWarnings && Name != manifestHeader.Name)
            Console.WriteLine($"WARNING: Expected directory {Name}, but the manifest directory's name was {manifestHeader.Name}. Directory names are not flashed.");

        if (manifestHeader.DirCount != DirCount)
            throw new Exception($"ERROR: The directory's total directory count in the manifest ({manifestHeader.DirCount}) is different from the count in the archive ({DirCount}). Work cannot proceed.");

        if (manifestHeader.FileCount != FileCount)
            throw new Exception($"ERROR: The directory's total file count in the manifest ({manifestHeader.FileCount}) is different from the count in the archive ({FileCount}). Work cannot proceed.");

        string[] line = reader.ReadLine().Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);

        // Parse all subdirectory entries.
        for (int i = 0; i < DirCount; i++)
        {
            if (line[0] != "<dir>")
                throw new Exception($"ERROR in manifest: {Name} specified {DirCount} subdirectories, but position {i} did not begin with <dir>. ({line[0]})");

            _subdirectories[i].FlashFromManifest(reader);

            line = reader.ReadLine().Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);
        }

        // Parse all file entries.
        PXDArchiveFileHeader[] fileHeaders = new PXDArchiveFileHeader[FileCount];
        SLLZParameters[] encodingParams = new SLLZParameters[FileCount];
        for (int i = 0; i < FileCount; i++)
        {
            if (line[0] != "<file>")
                throw new Exception($"ERROR in manifest: {Name} specified {FileCount} files, but position {i} did not begin with <file>. ({line[0]})");

            fileHeaders[i] = PXDArchiveFileHeader.HeaderFromManifest(reader, out encodingParams[i]);

            line = reader.ReadLine().Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);
        }

        // Flash all files now that checks have been passed.
        for (int i = 0; i < FileCount; i++)
        {
            _files[i].FlashFromHeader(fileHeaders[i]);

            if (encodingParams[i].Version != SLLZVersion.UNCOMPRESSED && !_files[i].IsCompressed)
                _files[i].EncodeData(encodingParams[i]);

            else if (encodingParams[i].Version == SLLZVersion.UNCOMPRESSED && _files[i].IsCompressed)
                _files[i].DecodeData();
        }

        // Flash the directory.
        FlashFromHeader(manifestHeader);
    }

    internal void FlashFromHeader(PXDArchiveDirectoryHeader header)
    {
        Attributes = header.Attributes;
    }

    internal static PXDArchiveDirectory FromManifest(StreamReader reader)
    {
        PXDArchiveDirectory dir = new(reader.ReadLine().Split().Last())
        {
            DirCount        = int.Parse(reader.ReadLine().Split().Last()),
            FirstDirIndex   = int.Parse(reader.ReadLine().Split().Last()),
            FileCount       = int.Parse(reader.ReadLine().Split().Last()),
            FirstFileIndex  = int.Parse(reader.ReadLine().Split().Last())
        };

        if (!Directory.Exists(dir.Name) && dir.Name != DefaultRootName)
            throw new DirectoryNotFoundException(dir.Name);

        PushDirectoryAndGo(dir.Name);

        dir._subdirectories = new PXDArchiveDirectory[dir.DirCount];
        dir._files = new PXDArchiveFile[dir.FileCount];
        dir.FileTreeDirectorySets = [dir._subdirectories];

        string[] line = reader.ReadLine().Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);

        if (line[0] == "Attr")
        {
            dir.Attributes = (FileAttributes) int.Parse(line.Last(), System.Globalization.NumberStyles.AllowHexSpecifier);
            line = reader.ReadLine().Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);
        }

        // Parse all subdirectory entries.
        for (int i = 0; i < dir.DirCount; i++)
        {
            if (line[0] != "<dir>")
                throw new Exception($"ERROR: The directory entry for {dir.Name} in the manifest specified {dir.DirCount} subdirectories, but subdirectory entry {i} did not begin with <dir>. ({line[0]})");

            dir._subdirectories[i] = FromManifest(reader);

            line = reader.ReadLine().Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);
        }

        // Parse all file entries.
        for (int i = 0; i < dir.FileCount; i++)
        {
            if (line[0] != "<file>")
                throw new Exception($"ERROR: The directory entry for {dir.Name} in the manifest specified {dir.FileCount} files, but file entry {i} did not begin with <file>. ({line[0]})");

            dir._files[i] = PXDArchiveFile.FromManifest(reader, dir);

            line = reader.ReadLine().Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);
        }

        // If there are files, add them to the file tree.
        if (dir._files.Length > 0)
            dir.FileTreeFiles.AddRange(dir._files);

        // Add subdirectories' subdirectories and files to sets to facilitate archive initialization via file tree.
        foreach (var subdir in dir._subdirectories)
        {
            if (subdir.FileTreeDirectorySets.Count != 0)
                dir.FileTreeDirectorySets.AddRange(subdir.FileTreeDirectorySets);

            if (subdir.FileTreeFiles.Count != 0)
                dir.FileTreeFiles.AddRange(subdir.FileTreeFiles);
        }

        if (line[0] != "</dir>")
            throw new Exception($"ERROR in manifest: The entry for {dir.Name} should have ended with </dir>. Instead, \"{line[0]}\" was found.");

        dir.FileTreeInitialized = true;

        PopDirectoryAndGo();

        return dir;
    }

    /// <summary>
    /// Extract the directory to the specified location, including all files and subdirectories.
    /// </summary>
    /// <param name="outputPath">The output path for the directory.</param>
    public void ToDirectory(string outputPath)
    {
        CreateDirectoryPushAndGo(outputPath);

        ToDirectory();

        PopDirectoryAndGo();
    }

    /// <summary>
    /// Extract the directory, including all files and subdirectories.
    /// </summary>
    /// <returns>The number of subdirectories extracted, recursively.</returns>
    internal int ToDirectory()
    {
        int fileTreeDirCount = 0;

        if (FileTreeInitialized)
        {
            if (PXDArchiveOptions.Verbose)
                Console.WriteLine($"Writing out directory {Name}");
            CreateDirectoryPushAndGo(Name);

            foreach (var entry in _files)
            {
                using MemoryStream buffer = new();
                using MyBinaryWriter writer = new(buffer);
                entry.ToFile(writer);

                FileInfo info = new(entry.Name);
                using FileStream outFile = info.Open(FileMode.Create, FileAccess.Write);
                buffer.Position = 0;
                buffer.CopyTo(outFile);
                info.Attributes = entry.Attributes;
                info.CreationTimeUtc = entry.Timestamp;
            }

            foreach (var dir in _subdirectories)
                fileTreeDirCount += dir.ToDirectory();

            fileTreeDirCount += DirCount;

            PopDirectoryAndGo();
        }

        return fileTreeDirCount;
    }

    internal void RecordFilesAt(Span<PXDArchiveFile> newFiles, int index) => newFiles.CopyTo(_files.AsSpan(index));
    internal void RecordSubdirectoryAt(PXDArchiveDirectory dir, int index) => _subdirectories[index] = dir;

    /// <summary>
    /// Check if the directory is similar to another directory.
    /// </summary>
    /// <remarks>Compares the directory headers and file names.</remarks>
    public bool SimilarTo(PXDArchiveDirectory? other)
    {
        bool eq =
            other is not null &&
            Name == other.Name &&
            DirCount == other.DirCount &&
            FirstDirIndex == other.FirstDirIndex &&
            FileCount == other.FileCount &&
            FirstFileIndex == other.FirstFileIndex;

        for (int i = 0; eq && i < DirCount; i++)
            eq = _subdirectories[i].SimilarTo(other._subdirectories[i]);

        for (int i = 0; eq && i < FileCount; i++)
            eq = _files[i].Name == other._files[i].Name;

        return eq;
    }

    internal void FlashFromDirectory(PXDArchiveDirectory other)
    {
        Attributes = other.Attributes;

        for (int i = 0; i < DirCount; i++)
            _subdirectories[i].FlashFromDirectory(other._subdirectories[i]);

        for (int i = 0; i < FileCount; i++)
            _files[i].FlashFromFile(other._files[i]);
    }

    internal IEnumerable<PXDArchiveFile> FilesNamed(string name) => _files.Where(file => name.Equals(file.Name, StringComparison.OrdinalIgnoreCase));
}
