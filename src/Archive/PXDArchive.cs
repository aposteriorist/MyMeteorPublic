using MyMeteor.IO;
using MyMeteor.IO.Compression;
using System.Collections.ObjectModel;
using System.IO;

namespace MyMeteor.Archive;

/// <summary>
/// A class representing the header for an archive.
/// Enables basic analysis of archive information without a full analysis being performed.
/// </summary>
public class PXDArchiveHeader
{
    /// <summary>
    /// The default name for a root directory, based on the root directory mode in the settings.
    /// </summary>
    public static string DefaultRootDirectoryName => PXDArchiveDirectoryHeader.DefaultRootName;

    /// <summary>
    /// The file signature for the header.
    /// </summary>
    public static readonly char[] Magic = ['P', 'A', 'R', 'C'];  // Regardless of endianness

    public byte Platform { get; internal set; }
    public Endianness Endianness { get; internal set; }
    public bool SizeExtended { get; internal set; }
    public bool Relocated { get; internal set; }
    internal ushort FileSizeMode = 1;
    internal ushort UnknownA = 1;

    // TO-DO: Create public properties in PXDArchive to access the collection counts, and rename these.
    internal int DirCount = 0;
    internal int FileCount = 0;

    /// <summary>
    /// Create a header from a reader over the text of an archive manifest.
    /// </summary>
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

/// <summary>
/// A class storing a virtual file structure that can be binarized into a .par file.
/// </summary>
public class PXDArchive : PXDArchiveHeader
{
    /// <summary>
    /// The archive's name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// A read-only collection of the directories in this archive.
    /// </summary>
    public ReadOnlyCollection<PXDArchiveDirectory> Directories => _directories.AsReadOnly();

    /// <summary>
    /// A read-only collection of the files in this archive.
    /// </summary>
    public ReadOnlyCollection<PXDArchiveFile> Files => _files.AsReadOnly();

    /// <summary>
    /// The root directory for this archive.
    /// </summary>
    public PXDArchiveDirectory RootDirectory { get; private set; }

    /// <summary>
    /// Whether the archive's data has been loaded into memory.
    /// </summary>
    public bool DataLoaded { get; private set; } = false;

    /// <summary>
    /// Whether the archive has been initialized.
    /// </summary>
    public bool ArchiveInitialized { get; private set; } = false;

    /// <summary>
    /// Whether the file tree has been initialized.
    /// </summary>
    public bool FileTreeInitialized { get; private set; } = false;

    private PXDArchiveDirectory[] _directories;

    private PXDArchiveFile[] _files;

    private byte[] Data;

    #region Construction
    /// <summary>
    /// Create an empty archive.
    /// </summary>
    /// <param name="name">The name of the archive.</param>
    public PXDArchive(string name) => Name = name;

    /// <summary>
    /// Create an empty archive.
    /// </summary>
    /// <param name="name">The name of the archive.</param>
    /// <param name="platform">The platform.</param>
    /// <param name="endianness">The endianness.</param>
    public PXDArchive(string name, byte platform, Endianness endianness = Endianness.Big)
    {
        Name = name;
        Platform = platform;
        Endianness = endianness;
    }

    /// <summary>
    /// Create an empty archive, initializing the directory and file collections.
    /// </summary>
    /// <param name="name">The name of the archive.</param>
    /// <param name="expectedDirCount">The expected number of directories. For initial capacity.</param>
    /// <param name="expectedFileCount">The expected number of files. For initial capacity.</param>
    /// <param name="platform">The platform.</param>
    /// <param name="endianness">The endianness.</param>
    /// <returns></returns>
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
    /// <summary>
    /// Create an archive from an archived file known to be a sub-archive.
    /// </summary>
    /// <returns>An archive, or null if the file is not a PXD archive.</returns>
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
    /// <summary>
    /// Create an archive from a file path on disk.
    /// File data can be loaded into memory and decoded at this time.
    /// </summary>
    /// <param name="parPath">The file path to an archive.</param>
    /// <param name="loadAllData">Whether to load the archived files' data into memory.</param>
    /// <param name="decodeData">Whether to decode the archived files' data.</param>
    /// <remarks>(Requesting that data be decoded is a no-op if data is not loaded.)</remarks>
    public static PXDArchive? FromFile(string parPath, bool loadAllData = false, bool decodeData = false)
        => FromFile(new FileInfo(parPath), loadAllData, decodeData);

    /// <summary>
    /// Create an archive from a FileInfo instance pointing to a file path on disk.
    /// File data can be loaded into memory and decoded at this time.
    /// </summary>
    /// <param name="parInfo">The file in question.</param>
    /// <param name="loadAllData">Whether to load the archived files' data into memory.</param>
    /// <param name="decodeData">Whether to decode the archived files' data.</param>
    /// <remarks>(Requesting that data be decoded is a no-op if data is not loaded.)</remarks>
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
    /// <summary>
    /// Create an archive from a stream.
    /// File data can be loaded into memory and decoded at this time.
    /// </summary>
    /// <param name="stream">A stream over the archive data.</param>
    /// <param name="archiveName">A name for the archive.</param>
    /// <param name="loadAllData">Whether to store the archived files' data in memory.</param>
    /// <param name="decodeData">Whether to decode the archived files' data.</param>
    /// <param name="disposeStream">Whether to dispose of the stream after work is complete.</param>
    /// <remarks>(Requesting that data be decoded is a no-op if data is not loaded.)</remarks>
    public static PXDArchive? FromStream(Stream stream, string archiveName = "Untitled.par", bool loadAllData = false, bool decodeData = false, bool disposeStream = false)
    {
        MyBinaryReader reader = new(stream, !disposeStream);

        return FromStream(reader, archiveName, loadAllData, decodeData, true);
    }

    /// <summary>
    /// Create an archive from a stream via a binary reader.
    /// File data can be loaded into memory and decoded at this time.
    /// </summary>
    /// <param name="reader">A reader over an archive data stream.</param>
    /// <param name="archiveName">A name for the archive.</param>
    /// <param name="loadAllData">Whether to store the archived files' data in memory.</param>
    /// <param name="decodeData">Whether to decode the archived files' data.</param>
    /// <param name="disposeOfReader">Whether to dispose of the reader after work is complete.</param>
    /// <remarks>(Requesting that data be decoded is a no-op if data is not loaded.)</remarks>
    public static PXDArchive? FromStream(MyBinaryReader reader, string archiveName = "Untitled.par", bool loadAllData = false, bool decodeData = false, bool disposeOfReader = false)
    {
        PXDArchive par = new(archiveName);

        if (loadAllData)
        {
            reader.PushForward(0);

            par.Data = new byte[reader.Length];
            reader.ReadExactly(par.Data);

            reader.PopBack();
        }

        par.ParseStream(reader, loadAllData, decodeData, disposeOfReader);

        return par;
    }

    /// <summary>
    /// Parse a stream over archive data.
    /// File data can be loaded into memory and decoded at this time.
    /// </summary>
    /// <param name="stream">A stream over the archive data.</param>
    /// <param name="loadAllData">Whether to store the archived files' data in memory.</param>
    /// <param name="decodeData">Whether to decode the archived files' data.</param>
    /// <param name="disposeStream">Whether to dispose of the stream after work is complete.</param>
    /// <remarks>(Requesting that data be decoded is a no-op if data is not loaded.)</remarks>
    public void ParseStream(Stream stream, bool loadAllData = false, bool decodeData = false, bool disposeStream = false)
    {
        MyBinaryReader reader = new(stream, !disposeStream);

        ParseStream(reader, loadAllData, decodeData, true);
    }

    // TO-DO: This function should only run if the archive is already empty.
    /// <summary>
    /// Parse a stream over archive data via a binary reader.
    /// File data can be loaded into memory and decoded at this time.
    /// </summary>
    /// <param name="reader">A reader over an archive data stream.</param>
    /// <param name="loadAllData">Whether to store the archived files' data in memory.</param>
    /// <param name="decodeData">Whether to decode the archived files' data.</param>
    /// <param name="disposeOfReader">Whether to dispose of the stream after work is complete.</param>
    /// <remarks>(Requesting that data be decoded is a no-op if data is not loaded.)</remarks>
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
    /// <summary>
    /// Create an archive from a directory path on disk.
    /// File data can be loaded into memory at this time.
    /// </summary>
    /// <param name="parName">A name for the archive.</param>
    /// <param name="dirPath">The directory path.</param>
    /// <param name="loadAllData">Whether to store the archived files' data in memory.</param>
    /// <param name="encodingParams">The parametres for file encoding (default is null, signifying no encoding).</param>
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

            par.FileSizeMode = (ushort)(PXDArchiveOptions.FileSizeWriteMode != PXDFileSizeWriteMode.NoWrite ? 1 : 2);

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

    /// <summary>
    /// Create an archive from an archive manifest.
    /// </summary>
    /// <param name="manifestPath">The path to a manifest file on disk.</param>
    public static PXDArchive? FromManifest(string manifestPath) => FromManifest(new FileInfo(manifestPath));

    /// <summary>
    /// Create an archive from an archive manifest.
    /// </summary>
    /// <param name="manifest">A FileInfo instance for a manifest file on disk.</param>
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

    /// <summary>
    /// Create an archive from a reader over an archive manifest.
    /// </summary>
    /// <param name="reader">A reader over the text of an archive manifest.</param>
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
    /// <summary>
    /// Export the archive as a .par file.
    /// </summary>
    /// <param name="path">The output directory.</param>
    /// <param name="fileAlignment">The alignment for individual files in the archive (defaults to 0x800).</param>
    /// <param name="encodingParams">The parametres for file encoding (default is null, signifying no encoding).</param>
    public void ToArchiveFile(string path, uint fileAlignment = 0x800, SLLZParameters? encodingParams = null)
    {
        var buffer = ToArchiveFile(fileAlignment, encodingParams);

        File.WriteAllBytes(Path.Combine(path, $"{Name}.par"), buffer);
    }

    /// <summary>
    /// Binarize the archive.
    /// </summary>
    /// <param name="fileAlignment">The alignment for individual files in the archive (defaults to 0x800).</param>
    /// <param name="encodingParams">The parametres for file encoding (default is null, signifying no encoding).</param>
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
            if (PXDArchiveOptions.FileSizeWriteMode == PXDFileSizeWriteMode.WriteAligned)
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
    /// <summary>
    /// Extract the contents of the archive.
    /// </summary>
    /// <param name="path">The extraction path.</param>
    public void ToDirectory(string path = "")
    {
        CreateDirectoryPushAndGo(path);

        AssertReadyForWrite();  // Unrecovered error is fine for now

        RootDirectory.ToDirectory();

        if (PXDArchiveOptions.GenerateManifest)
            ToManifest();

        PopDirectoryAndGo();
    }

    /// <summary>
    /// Produce an archive manifest.
    /// </summary>
    /// <param name="path">The output path.</param>
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

    /// <summary>
    /// Blindly extract all files to the current working directory.
    /// Directory hierarchy is ignored. File naming conflicts are not handled (files with the same name will overwrite one another).
    /// Usage of this function is NOT recommended.
    /// </summary>
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
    /// <summary>
    /// If archive initialization has been performed, initialize the file tree from the archive.
    /// </summary>
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

    /// <summary>
    /// Copy the appropriate slice of the archive's file tree into a directory's file tree.
    /// </summary>
    internal void CopyFileTreeTo(PXDArchiveDirectory dir)
    {
        _directories.AsSpan(dir.FirstDirIndex, dir.DirCount).CopyTo(dir._subdirectories);
        var dirFiles = _files.AsSpan(dir.FirstFileIndex, dir.FileCount);
        dirFiles.CopyTo(dir._files);  // Uses memory copy internally, no need to include in below loop

        // Record each file's parent directory.
        foreach (var file in dirFiles)
            file.ContainingDirectory = dir;

        dir.FileTreeInitialized = true;
    }

    /// <summary>
    /// If the file tree has been initialized, initialize the archive from the file tree.
    /// </summary>
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
    /// <summary>
    /// Flash the archive with an appropriate file on disk.
    /// </summary>
    /// <param name="flashFile">A FileInfo instance pointing to either an archive or an archive manifest.</param>
    public void FlashFrom(FileInfo flashFile)
    {
        if (flashFile.Extension == ".par")
            FlashFromArchive(flashFile);
        else if (flashFile.Extension == ".manifest")
            FlashFromManifest(flashFile);
        else
            Console.WriteLine($"ERROR: The file {flashFile.FullName} does not have a .par or .manifest extension. No flash was performed.");
    }

    /// <summary>
    /// Flash the archive with an archive manifest on disk.
    /// </summary>
    /// <param name="manifestPath">The file path.</param>
    public void FlashFromManifest(string manifestPath) => FlashFromManifest(new FileInfo(manifestPath));

    /// <summary>
    /// Flash the archive with an archive manifest on disk.
    /// </summary>
    /// <param name="manifest">A FileInfo instance pointing to a manifest on disk.</param>
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

    /// <summary>
    /// Flash the archive with a reader over an archive manifest.
    /// </summary>
    /// <param name="reader">A reader over the text data of an archive manifest.</param>
    /// <exception cref="Exception"></exception>
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

    /// <summary>
    /// Flash the archive's header with some other archive header.
    /// </summary>
    internal void FlashFromHeader(PXDArchiveHeader header)
    {
        Platform = header.Platform;
        Endianness = header.Endianness;
        SizeExtended = header.SizeExtended;
        Relocated = header.Relocated;
        FileSizeMode = header.FileSizeMode;
        UnknownA = header.UnknownA;
    }

    /// <summary>
    /// Flash the archive with an archive on disk.
    /// </summary>
    /// <param name="path">The file path of an archive.</param>
    public void FlashFromArchive(string path) => FlashFromArchive(new FileInfo(path));

    /// <summary>
    /// Flash the archive with an archive on disk.
    /// </summary>
    /// <param name="otherInfo">A FileInfo instance pointing to the file path of an archive.</param>
    public void FlashFromArchive(FileInfo otherInfo)
    {
        PXDArchive? other = FromFile(otherInfo);

        if (other != null)
            FlashFromArchive(other);
        else
            Console.WriteLine("ERROR in FlashFromArchive: the other archive failed to parse.");
    }

    /// <summary>
    /// Flash the archive with some other archive.
    /// </summary>
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
    /// <summary>
    /// Replace the file at an index with a new file.
    /// </summary>
    /// <param name="newFile">The new file.</param>
    /// <param name="fileIndex">The index to be replaced.</param>
    /// <exception cref="IndexOutOfRangeException"></exception>
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

    /// <summary>
    /// Replace an existing file with a new file of the same name.
    /// </summary>
    /// <param name="newFile">The new file.</param>
    /// <exception cref="Exception"></exception>
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
    /// <summary>
    /// Request decoding for the file at the given index.
    /// If the file's data was not loaded, no work is performed.
    /// If the file's data is not currently encoded, no work is performed.
    /// </summary>
    /// <param name="fileIndex">The file index for decoding.</param>
    /// <remarks></remarks>
    public void DecodeFile(int fileIndex)
    {
        if (fileIndex >= FileCount)
        {
            Console.WriteLine($"ERROR in DecodeFile: File index {fileIndex} was given, but the archive only contains {FileCount} file(s).");
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

    /// <summary>
    /// Request encoding for the file at the given index.
    /// If the file's data was not loaded, no work is performed.
    /// If the file's data is already encoded, no work is performed.
    /// </summary>
    /// <param name="fileIndex">The file index for encoding.</param>
    /// <param name="encodingParam">The requested encoding parametres.</param>
    public void EncodeFile(int fileIndex, SLLZParameters encodingParam)
    {
        if (fileIndex >= FileCount)
        {
            Console.WriteLine($"ERROR in EncodeFile: File index {fileIndex} was given, but the archive only contains {FileCount} file(s).");
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

    /// <summary>
    /// Request decoding for all files in the archive.
    /// If a particular file is not encoded, no work is performed.
    /// </summary>
    /// <param name="parallel">Whether decoding should be performed in parallel.</param>
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

    /// <summary>
    /// Request encoding for all files in the archive.
    /// If a particular file is already encoded, no work is performed.
    /// </summary>
    /// <param name="encodingParam">The requested encoding parametres.</param>
    /// <param name="parallel">Whether encoding should be performed in parallel.</param>
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
    /// <summary>
    /// Get all files with a matching name (case-insensitive).
    /// </summary>
    public IEnumerable<PXDArchiveFile> FilesNamed(string name) => _files.Where(file => name.Equals(file.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Get all files ending with a matching suffix (case-insensitive).
    /// </summary>
    public IEnumerable<PXDArchiveFile> FilesEnding(string suffix) => _files.Where(file => file.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Get all directories with a matching name (case-insensitive).
    /// </summary>
    public IEnumerable<PXDArchiveDirectory> DirectoriesNamed(string name) => _directories.Where(dir => name.Equals(dir.Name, StringComparison.OrdinalIgnoreCase));

    // TO-DO: These two functions below need invalid index handling.
    /// <summary>
    /// Get the file at a given index.
    /// </summary>
    public PXDArchiveFile GetFile(int index) => _files[index];

    /// <summary>
    /// Get the directory at a given index.
    /// </summary>
    public PXDArchiveDirectory GetDirectory(int index) => _directories[index];

    /// <summary>
    /// Get the index of the first file found with a matching name (case-insensitive).
    /// </summary>
    /// <returns>The index, or -1 if no file with that name is found.</returns>
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
    /// <summary>
    /// Verify that data has been loaded and properly initialized.
    /// </summary>
    private void AssertReadyForWrite()
    {
        if (!DataLoaded)
            throw new Exception($"ERROR: Data in archive {Name} was never marked as loaded.");

        if (!FileTreeInitialized || !ArchiveInitialized)
            throw new Exception($"ERROR: Data in archive {Name} was loaded, but initialization was never completed.");
    }

    /// <summary>
    /// Check if the archive is similar to another archive.
    /// </summary>
    /// <remarks>Compares the archive headers, directory headers, and file names.</remarks>
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
