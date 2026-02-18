using MyMeteor.IO;
using MyMeteor.IO.Compression;
using System.Collections.ObjectModel;
using System.IO;

namespace MyMeteor.Archive;

public class PXDArchiveHeader
{
    public static string DefaultRootDirectoryName => PXDArchiveDirectoryHeader.DefaultRootName;

    public static readonly char[] Magic = ['P', 'A', 'R', 'C'];  // Regardless of endianness

    public byte Platform { get; internal set; }
    public Endianness Endianness { get; internal set; }
    public bool SizeExtended { get; internal set; }
    public bool Relocated { get; internal set; }
    internal ushort FileSizeMode = 1;
    internal ushort UnknownA = 1;
    internal int DirCount = 0;
    internal int FileCount = 0;

    internal static PXDArchiveHeader FromManifest(StreamReader reader, out string name)
    {
        name = reader.ReadLine().Split().Last();

        return new()
        {
            Platform = byte.Parse(reader.ReadLine().Split().Last()),
            Endianness = (Endianness)byte.Parse(reader.ReadLine().Split().Last()),
            SizeExtended = reader.ReadLine().Split().Last() == "Y",
            Relocated = reader.ReadLine().Split().Last() == "Y",
            FileSizeMode = ushort.Parse(reader.ReadLine().Split().Last()),
            UnknownA = ushort.Parse(reader.ReadLine().Split().Last()),
            DirCount = int.Parse(reader.ReadLine().Split().Last()),
            FileCount = int.Parse(reader.ReadLine().Split().Last()),
        };
    }
}

public class PXDArchive : PXDArchiveHeader
{
    public string Name { get; private set; }

    private PXDArchiveDirectory[] _directories;
    public ReadOnlyCollection<PXDArchiveDirectory> Directories => _directories.AsReadOnly();

    private PXDArchiveFile[] _files;
    public ReadOnlyCollection<PXDArchiveFile> Files => _files.AsReadOnly();

    public PXDArchiveDirectory RootDirectory { get; private set; }

    public bool DataLoaded { get; private set; } = false;
    public bool ArchiveInitialized { get; private set; } = false;
    public bool FileTreeInitialized { get; private set; } = false;

    private byte[] Data;

    #region Construction
    public PXDArchive(string name) => Name = name;

    public PXDArchive(string name, byte platform, Endianness endianness = Endianness.Big)
    {
        Name = name;
        Platform = platform;
        Endianness = endianness;
    }

    public static PXDArchive CreateEmptyArchive(string name, int expectedDirCount = 0, int expectedFileCount = 0, byte platform = 2, Endianness endianness = Endianness.Big)
    {
        var par = new PXDArchive(name, platform, endianness)
        {
            _directories = new PXDArchiveDirectory[expectedDirCount],
            _files = new PXDArchiveFile[expectedFileCount],
            DirCount = expectedDirCount,
            FileCount = expectedFileCount
        };

        return par;
    }
    #endregion

    #region FromSubArchive
    public static PXDArchive? FromSubArchive(PXDArchiveFile file)
    {
        PXDArchive? subpar = null;

        if (file.DataLoaded)
        {
            MemoryStream stream = file.ToFile(true);
            subpar = FromStream(stream, Path.GetFileNameWithoutExtension(file.Name), true, false, true);
        }
        else
            Console.WriteLine($"ERROR in FromSubArchive: Data was never loaded for \"{file.Name}\". No work was performed.");

        return subpar;
    }
    #endregion

    #region ImportFile
    public static PXDArchive? FromFile(string parPath, bool loadData = false, bool decodeData = false)
        => FromFile(new FileInfo(parPath), loadData, decodeData);

    public static PXDArchive? FromFile(FileInfo parInfo, bool loadAllData = false, bool decodeData = false)
    {
        PXDArchive? par = null;

        if (parInfo.Exists)
        {
            if (PXDArchiveOptions.Verbose)
                Console.WriteLine($"Loading archive file {parInfo.Name}...");

            par = new(Path.GetFileNameWithoutExtension(parInfo.Name));
            using FileStream file = parInfo.OpenRead();
            MyBinaryReader reader;

            if (loadAllData)
            {
                par.Data = new byte[file.Length];
                file.ReadExactly(par.Data);
                file.Close();

                MemoryStream stream = new(par.Data);
                reader = new(stream);
            }
            else
                reader = new(file);

            par.ParseStream(reader, loadAllData, decodeData, true);
        }
        else
            Console.WriteLine($"ERROR: The path {parInfo.DirectoryName} does not contain a file named {parInfo.Name}. No work was performed.");

        return par;
    }
    #endregion

    #region ImportStream
    public static PXDArchive? FromStream(Stream stream, string archiveName = "Untitled.par", bool loadAllData = false, bool decodeData = false, bool disposeStream = false)
    {
        MyBinaryReader reader = new(stream, !disposeStream);

        return FromStream(reader, archiveName, loadAllData, decodeData, true);
    }

    public static PXDArchive? FromStream(MyBinaryReader reader, string archiveName = "Untitled.par", bool loadAllData = false, bool decodeData = false, bool disposeReader = false)
    {
        PXDArchive par = new(archiveName);

        if (loadAllData)
        {
            reader.PushForward(0);

            par.Data = new byte[reader.Length];
            reader.ReadExactly(par.Data);

            reader.PopBack();
        }

        par.ParseStream(reader, loadAllData, decodeData, disposeReader);

        return par;
    }

    public void ParseStream(Stream stream, bool loadAllData = false, bool decodeData = false, bool disposeStream = false)
    {
        MyBinaryReader reader = new(stream, !disposeStream);

        ParseStream(reader, loadAllData, decodeData, true);
    }

    // TO-DO: This function should only run if the archive is already empty.
    public void ParseStream(MyBinaryReader reader, bool loadAllData = false, bool decodeData = false, bool disposeOfReader = false)
    {
        reader.PushForward(0);

        if (!reader.ReadChars(4).SequenceEqual(Magic))
        {
            Console.WriteLine("Not a valid PARC header! No work was done.");
            return;
        }

        Platform      = reader.ReadByte();
        Endianness    = reader.StreamEndianness = (Endianness) reader.ReadByte();
        SizeExtended  = reader.ReadBoolean();
        Relocated     = reader.ReadBoolean();
        FileSizeMode  = reader.ReadUInt16();
        UnknownA      = reader.ReadUInt16();
        uint dataSize = reader.ReadUInt32();

        // Verify that the specified data size is valid.
        if (FileSizeMode == 1 && reader.Length < dataSize)
            throw new EndOfStreamException($"ERROR in {Name}: The data size noted in the PARC header is longer than the file length.");

        DirCount = reader.ReadInt32();
        _directories = new PXDArchiveDirectory[DirCount];
        uint dirTableOffset = reader.ReadUInt32();

        FileCount = reader.ReadInt32();
        _files = new PXDArchiveFile[FileCount];
        uint fileTableOffset = reader.ReadUInt32();

        // Parse all directories in the archive.
        string name;
        for (uint i = 0; i < _directories.Length; i++)
        {
            name = new string(reader.ReadChars(0x40)).TrimEnd('\0');

            reader.InPosition(() => _directories[i] = PXDArchiveDirectory.FromArchiveEntry(reader, name), dirTableOffset + i * 0x20);

            if (name.Length == 0 || name == ".")
                RootDirectory = _directories[i];
        }

        // If the par didn't account for the root directory, create a root directory.
        RootDirectory ??= new(DefaultRootDirectoryName);

        // Parse all files in the archive.
        for (uint i = 0; i < _files.Length; i++)
        {
            name = new string(reader.ReadChars(0x40)).TrimEnd('\0');

            reader.InPosition(() => _files[i] = PXDArchiveFile.FromArchiveEntry(reader, name, loadAllData), fileTableOffset + i * 0x20);
        }

        reader.PopBack();

        if (disposeOfReader)
            reader.Dispose();

        DataLoaded = loadAllData;

        if (PXDArchiveOptions.Verbose)
            Console.WriteLine($"{Name} {(loadAllData ? "loaded" : "parsed")}");

        ArchiveInitialized = true;
        InitializeFileTreeFromArchive();

        // If decoding was requested, call DecodeAll to perform decoding in parallel.
        if (decodeData)
        {
            if (loadAllData)
                DecodeAll(true);

            else if (!PXDArchiveOptions.SuppressWarnings)
                Console.WriteLine($"WARNING in ParseStream:" +
                    $"\t{Name} decoding was requested, but file load into memory was not requested. No decoding could be performed.");
        }
    }
    #endregion

    #region ImportDirectory
    public static PXDArchive? FromDirectory(string parName, string dirPath, bool loadAllData = true, SLLZParameters? encodingParams = null)
    {
        SuppressDirectoryTaxiWarnings();
        PXDArchive? par = null;

        if (Directory.Exists(dirPath))
        {
            if (PXDArchiveOptions.Verbose)
                Console.WriteLine($"Parsing directory {dirPath} into archive {parName}...");

            PushDirectoryAndGo(dirPath);

            // TO-DO: Assign an appropriate platform number.
            // I have not yet found example files with a number other than 2.
            // I wonder if Kenzan does anything with this information.
            par = new(parName, 2);

            par.FileSizeMode = (ushort)(PXDArchiveOptions.WriteTotalFileSize != PXDFileSizeWriteMode.NoWrite ? 1 : 2);

            // Recursively parse all directories in this file tree.
            par.RootDirectory = PXDArchiveDirectory.FromDirectory(DefaultRootDirectoryName, ref par.DirCount, ref par.FileCount, loadAllData);

            // If the root directory is being included, add one to DirCount.
            if (PXDArchiveOptions.RootDirectoryMode != PXDRootDirMode.NotIncluded)
                par.DirCount++;

            par.DataLoaded = loadAllData;

            if (PXDArchiveOptions.Verbose)
                Console.WriteLine($"Directory file tree {Directory.GetCurrentDirectory()} contained {par.FileCount} file(s) in {par.DirCount} folder(s).");

            par.FileTreeInitialized = true;
            par.InitializeArchiveFromFileTree();

            // If encoding was requested, call EncodeAll to perform encoding in parallel.
            if (encodingParams != null)
            {
                if (loadAllData)
                    par.EncodeAll(encodingParams, true);

                else if (!PXDArchiveOptions.SuppressWarnings)
                    Console.WriteLine($"WARNING in FromDirectory:" +
                        $"\t{parName} encoding was requested, but file load into memory was not requested. No encoding could be performed.");
            }

            PopDirectoryAndGo();
        }

        EmitDirectoryTaxiWarnings();
        return par;
    }

    public static PXDArchive? FromManifest(string manifestPath) => FromManifest(new FileInfo(manifestPath));

    public static PXDArchive? FromManifest(FileInfo manifest)
    {
        if (manifest.Exists)
        {
            using var reader = manifest.OpenText();
            return FromManifest(reader);
        }

        else if (!PXDArchiveOptions.SuppressWarnings)
            Console.WriteLine($"ERROR: Manifest file not found ({manifest.FullName}).");

        return null;
    }

    public static PXDArchive? FromManifest(StreamReader reader)
    {
        PXDArchive? par = null;

        if (reader.ReadLine() == "PXD ARCHIVE MANIFEST")
        {
            par = new(reader.ReadLine().Split().Last())
            {
                Platform = byte.Parse(reader.ReadLine().Split().Last()),
                Endianness = (Endianness)byte.Parse(reader.ReadLine().Split().Last()),
                SizeExtended = reader.ReadLine().Split().Last() == "Y",
                Relocated = reader.ReadLine().Split().Last() == "Y",
                FileSizeMode = ushort.Parse(reader.ReadLine().Split().Last()),
                UnknownA = ushort.Parse(reader.ReadLine().Split().Last()),
                DirCount = int.Parse(reader.ReadLine().Split().Last()),
                FileCount = int.Parse(reader.ReadLine().Split().Last()),
            };

            if (PXDArchiveOptions.Verbose)
                Console.WriteLine($"Parsing manifest into archive {par.Name}...");

            if (reader.ReadLine() == "<dir>")
            {
                // Recursively parse all directory entries in this manifest.
                // (The root directory will be included only if its first dir index isn't 0.)
                par.RootDirectory = PXDArchiveDirectory.FromManifest(reader);
                if (par.RootDirectory.FirstDirIndex != 0)
                {
                    PXDArchiveOptions.RootDirectoryMode = par.RootDirectory.Name switch
                    {
                        "." => PXDRootDirMode.WithDotName,
                        _ => PXDRootDirMode.WithName,
                    };
                }
                else
                    PXDArchiveOptions.RootDirectoryMode = PXDRootDirMode.NotIncluded;

                par.DataLoaded = true;

                if (PXDArchiveOptions.Verbose)
                    Console.WriteLine($"Manifest contained {par.FileCount} file(s) in {par.DirCount} folder(s).");

                par.FileTreeInitialized = true;
                par.InitializeArchiveFromFileTree();
            }

            else
                Console.WriteLine("ERROR: Manifest file's first entry should have been a directory and was not.");
        }

        else
            Console.WriteLine("ERROR: Manifest file is not a well-formatted PXD archive manifest.");

        return par;
    }
    #endregion

    #region ExportFile
    public void ToArchiveFile(string path, uint fileAlignment = 0x800, SLLZParameters? encodingParams = null)
    {
        var buffer = ToArchiveFile(fileAlignment, encodingParams);

        File.WriteAllBytes(Path.Combine(path, $"{Name}.par"), buffer);
    }

    public ReadOnlySpan<byte> ToArchiveFile(uint fileAlignment = 0x800, SLLZParameters? encodingParams = null)
    {
        AssertReadyForWrite();  // Unrecovered error is fine for now

        // Calculate the various file offsets.
        int dirTableOffset = 0x20 + (DirCount + FileCount) * 0x40;
        int fileTableOffset = dirTableOffset + DirCount * 0x20;
        int endOfFileHeaders = fileTableOffset + FileCount * 0x20;

        // Align the end of the file headers if need be.
        int diffToAlign = endOfFileHeaders % (int)fileAlignment;
        if (diffToAlign > 0)
            endOfFileHeaders += (int)fileAlignment - diffToAlign;

        using MemoryStream bufferStream = new(endOfFileHeaders);
        bufferStream.SetLength(endOfFileHeaders); // Fill the space with zeroes
        using MyBinaryWriter writer = new(bufferStream, false, Endianness.Big);

        // Write archive info.
        writer.Write(Magic);
        writer.Write(Platform);
        writer.Write((byte)Endianness);
        writer.Write(SizeExtended);
        writer.Write(Relocated);
        writer.Write(FileSizeMode);
        writer.Write(UnknownA);

        // Mark the position for data size if we're writing it, then skip.
        if (FileSizeMode != 2)
            writer.PushMark();
        writer.Skip(4);

        // Write directory and file info.
        writer.Write(DirCount);
        writer.Write(dirTableOffset);
        writer.Write(FileCount);
        writer.Write(fileTableOffset);

        // Write out directory names.
        foreach (var entry in _directories)
        {
            writer.Write(entry.Name.AsSpan());
            writer.Skip(0x40 - entry.Name.Length);
        }

        // Write out file names.
        foreach (var entry in _files)
        {
            writer.Write(entry.Name.AsSpan());
            writer.Skip(0x40 - entry.Name.Length);
        }

        // Write out directory headers.
        foreach (var entry in _directories)
        {
            entry.ToArchiveEntry(writer);
        }

        // Write out files.
        foreach (var entry in _files)
        {
            entry.ToArchiveEntry(writer, fileAlignment, encodingParams);
        }

        // Record the data size if appropriate.
        if (FileSizeMode == 1)
        {
            long totalFileSize = writer.Length;

            // Weirdo option from an early version of PAR in Kenzan.
            if (PXDArchiveOptions.WriteTotalFileSize == PXDFileSizeWriteMode.WriteAligned)
            {
                long diff = totalFileSize % fileAlignment;
                if (diff > 0)
                    totalFileSize += fileAlignment - diff;
            }

            writer.PopBack();
            writer.Write((uint)totalFileSize);
        }

        // Pad the end of the file to the appropriate boundary.
        writer.PadTo(0x800);

        return bufferStream.ToArray();
    }
    #endregion

    #region ExportDirectory
    public void ToDirectory(string path = "")
    {
        CreateDirectoryPushAndGo(path);

        AssertReadyForWrite();  // Unrecovered error is fine for now

        RootDirectory.ToDirectory();

        if (PXDArchiveOptions.GenerateManifest)
            ToManifest();

        PopDirectoryAndGo();
    }

    public void ToManifest(string path = "")
    {
        CreateDirectoryPushAndGo(path);

        AssertReadyForWrite();  // Unrecovered error is fine for now

        using StreamWriter writer = new(new MemoryStream());
        writer.WriteLine("PXD ARCHIVE MANIFEST");
        writer.WriteLine($"Name\t{Name}");
        writer.WriteLine($"Plat\t{Platform}");
        writer.WriteLine($"Endi\t{Endianness:D}");
        writer.WriteLine($"SExt\t{(SizeExtended ? 'Y' : 'N')}");
        writer.WriteLine($"Relo\t{(Relocated ? 'Y' : 'N')}");
        writer.WriteLine($"FSM\t{FileSizeMode}");
        writer.WriteLine($"UnkA\t{UnknownA}");
        writer.WriteLine($"DC\t{DirCount}");
        writer.WriteLine($"FC\t{FileCount}");

        RootDirectory.ToManifest(writer, 1);

        writer.Flush();
        writer.BaseStream.Position = 0;

        using FileStream outFile = File.Open($"{Name}.par.manifest", FileMode.Create, FileAccess.Write);
        writer.BaseStream.CopyTo(outFile);

        PopDirectoryAndGo();
    }

    public void DumpAllFiles()
    {
        foreach (var entry in _files)
        {
            using FileStream outFile = File.Open(entry.Name, FileMode.Create, FileAccess.Write);
            using MyBinaryWriter writer = new(outFile);
            entry.ToFile(writer);
        }
    }
    #endregion

    #region FileInitialization
    private void InitializeFileTreeFromArchive()
    {
        if (ArchiveInitialized)
        {
            if (!FileTreeInitialized)
            {
                int filesAccountedFor = 0;
                foreach (PXDArchiveDirectory dir in _directories)
                {
                    if (!dir.FileTreeInitialized)
                    {
                        CopyFileTreeTo(dir);
                        filesAccountedFor += dir.FileCount;
                    }
                }

                // If the first directory in the archive isn't the root directory,
                // then we potentially have files unaccounted for in the directory tree.
                // (In theory this is true, but in practice with RGG pars it may not be.)
                //
                // We also want to tuck everything into this root directory for file-writing later.
                if (RootDirectory != _directories[0])
                {
                    int dirIndex = 0;
                    List<PXDArchiveDirectory> rootDirs = [];
                    while (dirIndex < DirCount)
                    {
                        rootDirs.Add(_directories[dirIndex]);
                        dirIndex += _directories[dirIndex].DirCount + 1;
                    }

                    RootDirectory._subdirectories = [.. rootDirs];
                    RootDirectory.FirstDirIndex = 0;
                    RootDirectory.DirCount = rootDirs.Count;

                    int rootFileCount = FileCount - filesAccountedFor;
                    if (rootFileCount > 0)
                    {
                        var rootFiles = _files[..rootFileCount];

                        // Record each file's parent directory.
                        foreach (var file in rootFiles)
                            file.ContainingDirectory = RootDirectory;

                        RootDirectory._files = rootFiles;
                        RootDirectory.FirstFileIndex = 0;
                        RootDirectory.FileCount = rootFileCount;
                    }
                    else
                        RootDirectory._files = [];

                    RootDirectory.FileTreeInitialized = true;
                }

                FileTreeInitialized = true;
            }
            else if (!PXDArchiveOptions.SuppressWarnings)
                Console.WriteLine("WARNING: File tree initialization requested, but file tree was already initialized. No work was performed.");
        }
        else if(!PXDArchiveOptions.SuppressWarnings)
            Console.WriteLine("WARNING: File tree initialization from archive requested, but archive was never initialized.");
    }

    public void CopyFileTreeTo(PXDArchiveDirectory dir)
    {
        _directories.AsSpan(dir.FirstDirIndex, dir.DirCount).CopyTo(dir._subdirectories);
        var dirFiles = _files.AsSpan(dir.FirstFileIndex, dir.FileCount);
        dirFiles.CopyTo(dir._files);  // Uses memory copy internally, no need to include in below loop

        // Record each file's parent directory.
        foreach (var file in dirFiles)
            file.ContainingDirectory = dir;

        dir.FileTreeInitialized = true;
    }

    private void InitializeArchiveFromFileTree()
    {
        if (FileTreeInitialized)
        {
            if (!ArchiveInitialized)
            {
                _directories = new PXDArchiveDirectory[DirCount];

                // If the root directory is included in the archive, it is Directories[0].
                int count = 0;
                if (PXDArchiveOptions.RootDirectoryMode != PXDRootDirMode.NotIncluded)
                {
                    _directories[0] = RootDirectory;
                    count = 1;
                }

                // Initialize the archive's directory array.
                foreach (var dirList in RootDirectory.FileTreeDirectorySets)
                {
                    dirList.CopyTo(_directories, count);
                    count += dirList.Length;
                }

                // For each directory, if it has no subdirectories, its FirstDirIndex should be par.DirCount.
                foreach (var dir in _directories)
                    if (dir.DirCount == 0)
                        dir.FirstDirIndex = DirCount;

                // Initialize the archive's file array with the complete list from the root directory.
                _files = [.. RootDirectory.FileTreeFiles];

                ArchiveInitialized = true;
            }
            else if (!PXDArchiveOptions.SuppressWarnings)
                Console.WriteLine("WARNING: Archive initialization requested, but archive was already initialized. No work was performed.");
        }
        else if (!PXDArchiveOptions.SuppressWarnings)
            Console.WriteLine("WARNING: Archive initialization from file tree requested, but file tree was never initialized.");
    }

    internal void RecordFilesAt(Span<PXDArchiveFile> newFiles, int index) => newFiles.CopyTo(_files.AsSpan(index));
    internal void RecordDirectoryAt(PXDArchiveDirectory dir, int index) => _directories[index] = dir;
    public void MarkDataLoaded() => DataLoaded = true;
    public void MarkFileTreeInitialized() => FileTreeInitialized = true;
    public void MarkArchiveInitialized() => ArchiveInitialized = true;
    #endregion

    #region Flash
    public void FlashFrom(FileInfo flashFile)
    {
        if (flashFile.Extension == ".par")
            FlashFromArchive(flashFile);
        else if (flashFile.Extension == ".manifest")
            FlashFromManifest(flashFile);
        else
            Console.WriteLine($"ERROR: The file {flashFile.FullName} does not have a .par or .manifest extension. No flash was performed.");
    }

    public void FlashFromManifest(string manifestPath) => FlashFromManifest(new FileInfo(manifestPath));

    public void FlashFromManifest(FileInfo manifest)
    {
        if (manifest.Exists)
        {
            using var reader = manifest.OpenText();
            FlashFromManifest(reader);
        }

        else
            Console.WriteLine($"ERROR: Manifest file not found ({manifest.FullName}). No flash was performed.");
    }

    public void FlashFromManifest(StreamReader reader)
    {
        if (reader.ReadLine() == "PXD ARCHIVE MANIFEST")
        {
            var manifestHeader = PXDArchiveHeader.FromManifest(reader, out string manifestName);

            if (!PXDArchiveOptions.SuppressWarnings && manifestName != Name)
                Console.WriteLine($"WARNING: The archive manifest's Name field is {manifestName}, but the archive's name is {Name}. Archive names are not flashed.");

            if (manifestHeader.DirCount != DirCount)
                throw new Exception($"ERROR: The archive manifest's total directory count ({manifestHeader.DirCount}) is different from this archive's count ({DirCount}). Work cannot proceed.");

            if (manifestHeader.FileCount != FileCount)
                throw new Exception($"ERROR: The archive manifest's total file count ({manifestHeader.FileCount}) is different from this archive's count ({FileCount}). Work cannot proceed.");

            string line = reader.ReadLine();
            if (line != "<dir>")
                throw new Exception($"ERROR: Unexpected line in archive manifest.\nExpected:\t<dir>\nActual:\t{line}");

            // Continue to parse the stream, performing checks along the way to determine if flashing is appropriate.
            RootDirectory.FlashFromManifest(reader);

            // All checks have been passed, so we can flash.
            FlashFromHeader(manifestHeader);
        }

        else if (!PXDArchiveOptions.SuppressWarnings)
            Console.WriteLine("WARNING: Manifest file is not a well-formatted PXD archive manifest. No flash was performed.");
    }

    internal void FlashFromHeader(PXDArchiveHeader header)
    {
        Platform = header.Platform;
        Endianness = header.Endianness;
        SizeExtended = header.SizeExtended;
        Relocated = header.Relocated;
        FileSizeMode = header.FileSizeMode;
        UnknownA = header.UnknownA;
    }

    public void FlashFromArchive(string path) => FlashFromArchive(new FileInfo(path));

    public void FlashFromArchive(FileInfo otherInfo)
    {
        PXDArchive? other = FromFile(otherInfo);

        if (other != null)
            FlashFromArchive(other);
        else
            Console.WriteLine("ERROR in FlashFromArchive: the other archive failed to parse.");
    }

    public void FlashFromArchive(PXDArchive other)
    {
        if (!SimilarTo(other))
            Console.WriteLine("ERROR in FlashFromArchive: the other archive is not similar enough to flash. No changes were made.");

        Platform = other.Platform;
        UnknownA = other.UnknownA;

        RootDirectory.FlashFromDirectory(other.RootDirectory);
    }
    #endregion

    #region FileReplacement
    public void ReplaceFile(PXDArchiveFile newFile, uint fileIndex)
    {
        if (fileIndex >= FileCount)
            throw new IndexOutOfRangeException($"ERROR: File index {fileIndex} is not present in this archive (file count is {FileCount}).");

        PXDArchiveDirectory dir;
        
        // Find which directory contains the file.
        for (int i = 0; i < DirCount; i++)
        {
            dir = _directories[i];
            if (dir.FirstFileIndex <= fileIndex && dir.FirstFileIndex + dir.FileCount > 6)
            {
                dir._files[fileIndex - dir.FirstFileIndex] = newFile;
                break;
            }
        }

        _files[fileIndex] = newFile;

        if (PXDArchiveOptions.Verbose)
            Console.WriteLine($"File {newFile.Name} inserted at index {fileIndex}.");
    }

    public void ReplaceFileOfSameName(PXDArchiveFile newFile)
    {
        // Find the old file by name.
        int i = Array.FindIndex(_files, f => f.Name == newFile.Name);

        if (i < 0)
            throw new Exception($"ERROR: No file by the name {newFile.Name} is present in the archive.");

        // Record the directory from the old file.
        newFile.ContainingDirectory = _files[i].ContainingDirectory;

        // Replace the file in the directory.
        newFile.ContainingDirectory._files[Array.IndexOf(newFile.ContainingDirectory._files, _files[i])] = newFile;

        // Replace the file in the archive.
        _files[i] = newFile;
    }
    #endregion

    #region FileEncoding
    public void DecodeFile(int fileIndex)
    {
        if (fileIndex >= FileCount)
        {
            Console.WriteLine($"ERROR in DecodeFileData: File index {fileIndex} was given, but the archive only contains {FileCount} file(s).");
        }
        else
        {
            var entry = _files[fileIndex];

            if (entry.DataLoaded)
            {
                if (entry.IsCompressed)
                    entry.DecodeData();
                else if (!PXDArchiveOptions.SuppressWarnings)
                    Console.WriteLine($"WARNING: Data decode requested for file {entry.Name} (index {fileIndex}), but the file's data is not encoded.");
            }
            else if (!PXDArchiveOptions.SuppressWarnings)
                Console.WriteLine($"WARNING: Data decode requested for file {entry.Name} (index {fileIndex}), but the file's data was never loaded.");
        }
    }

    public void EncodeFile(int fileIndex, SLLZParameters encodingParam)
    {
        if (fileIndex >= FileCount)
        {
            Console.WriteLine($"ERROR in DecodeFileData: File index {fileIndex} was given, but the archive only contains {FileCount} file(s).");
        }
        else
        {
            var entry = _files[fileIndex];

            if (entry.DataLoaded)
            {
                if (!entry.IsCompressed)
                    entry.EncodeData(encodingParam);
                else if (!PXDArchiveOptions.SuppressWarnings)
                    Console.WriteLine($"WARNING: Data encode requested for file {entry.Name} (index {fileIndex}), but the file's data is already encoded.");
            }
            else if (!PXDArchiveOptions.SuppressWarnings)
                Console.WriteLine($"WARNING: Data encode requested for file {entry.Name} (index {fileIndex}), but the file's data was never loaded.");
        }
    }

    public void DecodeAll(bool parallel = false)
    {
        if (parallel)
            Parallel.ForEach(_files, file => file.DecodeData());

        else
            foreach (var file in _files)
                file.DecodeData();

        //Task[] tasks = new Task[FileCount];
        //for (int i = 0; i < FileCount; i++)
        //{
        //    tasks[i] = new Task(() => _files[i].DecodeData());
        //}

        //var tasks = _files.Select(file => new Task(() => file.DecodeData()));
        //Task.WaitAll(tasks);

        if (PXDArchiveOptions.Verbose)
            Console.WriteLine($"{Name}: All {FileCount} files in archive decoded.");
    }

    public void EncodeAll(SLLZParameters encodingParam, bool parallel = false)
    {
        if (encodingParam.Version != SLLZVersion.UNCOMPRESSED)
        {
            if (parallel)
                Parallel.ForEach(_files, file => file.EncodeData(encodingParam));

            else
                foreach (var file in _files)
                    file.EncodeData(encodingParam);

            //var tasks = _files.Select(file => new Task(() => file.EncodeData(encodingParam)));
            //Task.WaitAll(tasks);

            if (PXDArchiveOptions.Verbose)
                Console.WriteLine($"{Name}: All {FileCount} files in archive encoded.");
        }
        else if (!PXDArchiveOptions.SuppressWarnings)
            Console.WriteLine($"WARNING in archive {Name}: Full file encoding was requested, but the SLLZ version specified was Uncompressed. No work was performed.");
    }
    #endregion

    #region FileAccess
    public IEnumerable<PXDArchiveFile> FilesNamed(string name) => _files.Where(file => name.Equals(file.Name, StringComparison.OrdinalIgnoreCase));
    public IEnumerable<PXDArchiveFile> FilesEnding(string suffix) => _files.Where(file => file.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<PXDArchiveDirectory> DirectoriesNamed(string name) => _directories.Where(dir => name.Equals(dir.Name, StringComparison.OrdinalIgnoreCase));

    public PXDArchiveFile GetFile(int index) => _files[index];
    public PXDArchiveDirectory GetDirectory(int index) => _directories[index];

    public int IndexOf(string name)
    {
        for (int i = 0; i < _files.Length; i++)
        {
            if (_files[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
    #endregion

    #region Other
    private void AssertReadyForWrite()
    {
        if (!DataLoaded)
            throw new Exception($"ERROR: Data in archive {Name} was never marked as loaded.");

        if (!FileTreeInitialized || !ArchiveInitialized)
            throw new Exception($"ERROR: Data in archive {Name} was loaded, but initialization was never completed.");
    }

    public bool SimilarTo(PXDArchive? other)
    {
        return other is not null &&
               Endianness == other.Endianness &&
               SizeExtended == other.SizeExtended &&
               Relocated == other.Relocated &&
               DirCount == other.DirCount &&
               FileCount == other.FileCount &&
               RootDirectory.SimilarTo(other.RootDirectory);
    }
    #endregion
}
