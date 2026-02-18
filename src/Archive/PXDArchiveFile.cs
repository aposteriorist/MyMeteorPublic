using MyMeteor.IO;
using MyMeteor.IO.Compression;

namespace MyMeteor.Archive;

public class PXDArchiveFileHeader
{
    public string Name { get; internal set; }
    public bool WasCompressed { get; internal set; }
    public bool OrigCompressed { get; internal set; }
    public int Size { get; internal set; }
    public int EntryLength { get; internal set; }
    public FileAttributes Attributes { get; internal set; }
    public DateTime Timestamp { get; internal set; }

    internal static PXDArchiveFileHeader HeaderFromManifest(StreamReader reader, out SLLZParameters encodingParams)
    {
        PXDArchiveFileHeader header = new()
        {
            Name = reader.ReadLine().Split().Last(),
            OrigCompressed = reader.ReadLine().Split().Last() == "Y"
        };

        encodingParams = header.OrigCompressed
            ? new((Endianness) byte.Parse(reader.ReadLine().Split().Last()), (SLLZVersion) byte.Parse(reader.ReadLine().Split().Last()))
            : new(Version: SLLZVersion.UNCOMPRESSED);

        string[] line = reader.ReadLine().Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);

        if (line[0] == "Attr")
        {
            header.Attributes = (FileAttributes) int.Parse(line[1], System.Globalization.NumberStyles.AllowHexSpecifier);
            line = reader.ReadLine().Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);
        }

        if (line[0] == "Time")
            header.Timestamp = DateTime.UnixEpoch.AddSeconds(ulong.Parse(line[1]));
        else
            throw new Exception($"ERROR: The manifest entry for {header.Name} was missing a Time component.");

        line = [reader.ReadLine()];

        if (!line[0].EndsWith("</file>"))
            throw new Exception($"ERROR: The manifest entry for {header.Name} should have ended with </file>. Instead, \"{line[0]}\" was found.");

        return header;
    }
}

public class PXDArchiveFile : PXDArchiveFileHeader
{
    public long DataOffset { get; internal set; }   // long's maximum value is acceptable here on the basis of sanity
    public bool DataLoaded => DataHistory.CurrentIndex > -1;
    public byte[]? Data => DataHistory.Current?.Data;
    public bool IsCompressed => DataHistory.Current?.IsCompressed ?? false;
    public SLLZParameters? CurrentEncoding => IsCompressed ? SLLZ.GetEncoding(Data) : null;

    internal readonly History<DataRecord> DataHistory = new(4);

    public PXDArchiveDirectory ContainingDirectory { get; internal set; }

    public PXDArchiveFile(string name) => Name = name;

    public static PXDArchiveFile FromArchiveEntry(MyBinaryReader reader, string name, bool loadData = false, bool decodeData = false)
    {
        if (!loadData && decodeData)
            throw new Exception($"ERROR for {name}: Data decoding requested in FromArchiveEntry, but data loading was not requested.");

        PXDArchiveFile file = new(name)
        {
            WasCompressed = false,
            OrigCompressed = reader.ReadUInt32() == 0x80000000,
            Size = reader.ReadInt32(),
            EntryLength = reader.ReadInt32()
        };

        uint offset = reader.ReadUInt32();
        file.Attributes = (FileAttributes) reader.ReadUInt32();
        file.DataOffset = ((reader.ReadUInt32() & 0xFFFFFF) << 32) | offset;
        file.Timestamp = DateTime.UnixEpoch.AddSeconds(reader.ReadInt64());

        if (PXDArchiveOptions.Verbose)
            Console.WriteLine($"Reading archive entry {name}");
        
        if (loadData)
        {
            file.LoadData(reader);

            if (decodeData)
            {
                if (file.IsCompressed)
                    file.DecodeData();

                else if (!PXDArchiveOptions.SuppressWarnings)
                    Console.WriteLine($"WARNING: SLLZ decode requested for {name}, but file data is not encoded.");
            }
        }

        // The file's directory will be resolved later.

        return file;
    }

    public static PXDArchiveFile FromFilePath(string filePath, PXDArchiveDirectory directory, bool loadData = true, SLLZParameters? encodingParams = null)
    {
        // Get the FileInfo for the path if it exists.
        return FromFileInfo(new FileInfo(filePath), directory, loadData, encodingParams);
    }

    public static PXDArchiveFile FromFileInfo(FileInfo info, PXDArchiveDirectory directory, bool loadData = true, SLLZParameters? encodingParams = null)
    {
        PXDArchiveFile? entry = null;

        if (info.Exists)
        {
            if (info.Length > int.MaxValue)
                throw new OverflowException($"ERROR: The maximum supported filesize for an entry is {int.MaxValue}, but {info.FullName} is of size {info.Length}.");

            if (PXDArchiveOptions.Verbose)
                Console.WriteLine($"Reading file on disc {info.Name}");

            // Create the archive entry and input the necessary details.
            entry = new(info.Name);
            entry.Size = entry.EntryLength = (int) info.Length;
            entry.WasCompressed = entry.OrigCompressed = false;
            entry.DataOffset = -1;
            entry.Attributes = info.Attributes;
            entry.Timestamp = info.CreationTimeUtc;

            if (loadData)
            {
                using FileStream file = File.Open(info.FullName, FileMode.Open, FileAccess.Read);
                using MyBinaryReader reader = new(file);
                entry.LoadData(reader);

                if (encodingParams != null && encodingParams.Version != SLLZVersion.UNCOMPRESSED)
                {
                    if (!entry.IsCompressed)
                        entry.EncodeData(encodingParams);

                    else if (!PXDArchiveOptions.SuppressWarnings)
                        Console.WriteLine($"WARNING: SLLZ encode requested for {info.Name}, but data is already encoded. No encoding was performed.");
                }
            }

            entry.ContainingDirectory = directory;
        }

        return entry;
    }

    public static PXDArchiveFile FromBytes(byte[] data, PXDArchiveDirectory directory, string name, SLLZParameters? encodingParams = null)
    {
        PXDArchiveFile entry = new(name);
        entry.Size = entry.EntryLength = data.Length;
        entry.WasCompressed = entry.OrigCompressed = false;
        entry.DataOffset = -1;
        entry.Attributes = FileAttributes.None;
        entry.Timestamp = DateTime.UtcNow;

        entry.DataHistory.Add(new(data, false));

        if (encodingParams != null && encodingParams.Version != SLLZVersion.UNCOMPRESSED)
        {
            if (!entry.IsCompressed)
                entry.EncodeData(encodingParams);

            else if (!PXDArchiveOptions.SuppressWarnings)
                Console.WriteLine($"WARNING: SLLZ encode requested for {name}, but data is already encoded. No encoding was performed.");
        }

        entry.ContainingDirectory = directory;

        return entry;
    }

    public MemoryStream ToFile(bool decode = true)
    {
        MemoryStream outStream = new();
        using MyBinaryWriter writer = new(outStream, true);
        ToFile(writer, decode);
        return outStream;
    }

    public void ToFile(Stream outStream, bool decode = true)
    {
        using MyBinaryWriter writer = new(outStream, true);
        ToFile(writer, decode);
    }

    public void ToFile(MyBinaryWriter writer, bool decode = true)
    {
        if (Data != null)
        {
            if (PXDArchiveOptions.Verbose)
                Console.WriteLine($"\tWriting {Name} to stream...");

            if (decode)
                DecodeData();

            writer.Write(Data);

            if (PXDArchiveOptions.Verbose)
                Console.WriteLine($"\t\tWrote {Name} to stream with length {Data.Length}");
        }

        else
            throw new NullReferenceException($"ERROR: Data write requested for {Name}, but data was never loaded.");
    }

    internal void ToArchiveEntry(MyBinaryWriter writer, uint align = 0x800, SLLZParameters? encodingParams = null)
    {
        if (Data != null)
        {
            // Encode the data if requested.
            if (encodingParams != null && encodingParams.Version != SLLZVersion.UNCOMPRESSED)
            {
                if (!IsCompressed)
                    EncodeData(encodingParams);

                else if (!PXDArchiveOptions.SuppressWarnings)
                    Console.WriteLine($"WARNING: Encoding requested for archive entry {Name}, but file data is already encoded.");
            }
            
            // Mark the current position, which is where the header should be written.
            // Move to the end of the stream, which is where the data should be written.
            long diffToAlign = writer.PushForwardToEnd() % align;
            if (diffToAlign > 0)
            {
                // If there's not enough space to write the file within alignment, pad before writing.
                // (RGG pads if there's exactly enough space, so that behaviour is preserved here, hence the <=.)
                long alignedSpace = align - diffToAlign;
                if (alignedSpace <= Data.Length)
                {
                    writer.Length += alignedSpace;
                    writer.Position = writer.Length;
                }
            }

            // Note the current offset, and write the file.
            DataOffset = writer.Position;
            writer.Write(Data);
            

            // Go back and write the file header.
            writer.PopBack();
            writer.Write(IsCompressed ? 0x80000000 : 0U);
            writer.Write(Size);
            writer.Write(Data.Length);
            writer.Write(DataOffset > uint.MaxValue ? 0xFFFFFFFF : (uint)DataOffset);
            writer.Write((uint)Attributes);
            writer.Write((uint)(DataOffset >> 32) & 0xFFFFFF);
            writer.Write((ulong)((Timestamp - DateTime.UnixEpoch).TotalSeconds));
        }

        else
            throw new NullReferenceException($"ERROR: Data write requested for {Name}, but data was never loaded.");
    }

    internal void ToManifest(StreamWriter writer, int tabCount)
    {
        string tab = new('\t', tabCount);

        writer.WriteLine($"{(tabCount > 0 ? tab[0..^1] : "")}<file>");

        writer.WriteLine($"{tab}Name\t{Name}");
        writer.WriteLine($"{tab}Comp\t{(OrigCompressed ? 'Y' : 'N')}");
        if (OrigCompressed)
        {
            writer.WriteLine($"{tab}Endi\t{DataHistory.First.Data[4]}");
            writer.WriteLine($"{tab}SLLZ\t{DataHistory.First.Data[5]}");
        }

        if (Attributes != 0)
            writer.WriteLine($"{tab}Attr\t{Attributes:X}");

        writer.WriteLine($"{tab}Time\t{(ulong)((Timestamp - DateTime.UnixEpoch).TotalSeconds)}");

        writer.WriteLine($"{(tabCount > 0 ? tab[0..^1] : "")}</file>");
    }

    internal static PXDArchiveFile FromManifest(StreamReader reader, PXDArchiveDirectory directory)
    {
        PXDArchiveFileHeader manifestHeader = HeaderFromManifest(reader, out SLLZParameters encodingParams);
        FileInfo info = new(manifestHeader.Name);

        // If the file exists, scoop it up.
        PXDArchiveFile entry = FromFileInfo(info, directory, true, encodingParams) ?? throw new FileNotFoundException(info.FullName);

        entry.FlashFromHeader(manifestHeader);

        return entry;
    }

    internal void FlashFromHeader(PXDArchiveFileHeader header)
    {
        Attributes = header.Attributes;
        Timestamp = header.Timestamp;
    }

    public void LoadData(MyBinaryReader reader, int bytecount = -1)
    {
        DataHistory.Clear();

        if (DataOffset > -1)
            reader.PushForward(DataOffset);

        DataHistory.Add(new(reader.ReadBytes(bytecount > 0 ? bytecount : EntryLength), OrigCompressed));

        if (DataOffset > -1)
            reader.PopBack();

        if (PXDArchiveOptions.Verbose)
            Console.WriteLine($"\tLoaded data for {Name} with a length of {Data.Length}.");
    }

    public void DecodeData()
    {
        DataRecord? current = DataHistory.Current;

        if (current != null)
        {
            if (current.IsCompressed)
            {
                byte[] decodedData = SLLZ.Decode(current.Data);

                if (decodedData != null)
                {
                    if (Size != decodedData.Length)
                    {
                        if (!PXDArchiveOptions.SuppressWarnings)
                            Console.WriteLine($"WARNING: The expected decoded size of {Name} was {Size}, but the actual decoded size was {decodedData.Length}. Actual size recorded.");
                        Size = decodedData.Length;
                    }

                    EntryLength = decodedData.Length;
                    DataHistory.Add(new(decodedData, false));
                    WasCompressed = true;

                    if (PXDArchiveOptions.Verbose)
                        Console.WriteLine($"\tSuccessfully decoded data. Decoded length is {Data.Length}");
                }
            }

            else if (!PXDArchiveOptions.SuppressWarnings)
                Console.WriteLine($"WARNING: Data decode requested for {Name}, but data is not compressed.");
        }

        else
            throw new NullReferenceException($"ERROR: Data decode requested for {Name}, but data was never loaded.");
    }

    public void EncodeData(SLLZParameters param, bool forceLargerEncode = false)
    {
        DataRecord? current = DataHistory.Current;

        if (current != null)
        {
            if (param.Version == SLLZVersion.UNCOMPRESSED && !PXDArchiveOptions.SuppressWarnings)
                Console.WriteLine($"WARNING: Data encode requested for {Name}, but without compression. No work will be done.");

            else if (!current.IsCompressed)
            {
                byte[] encodedData = SLLZ.Encode(current.Data, param);

                if (encodedData != null)
                {
                    // If the encoded data is shorter, use it. (Or if the user forces encoding.)
                    if (Data.Length > encodedData.Length || forceLargerEncode)
                    {
                        EntryLength = encodedData.Length;
                        DataHistory.Add(new(encodedData, true));
                        WasCompressed = false;
                    }
                    else if (PXDArchiveOptions.Verbose)
                        Console.WriteLine($"Data encode requested for {Name}, but encoded data was longer. Force encoding if encoding is required.");
                }
            }

            else if (!PXDArchiveOptions.SuppressWarnings)
                Console.WriteLine($"WARNING: Data encode requested for {Name}, but data is already compressed.");
        }

        else
            throw new NullReferenceException($"ERROR: Data encode requested for {Name}, but data was never loaded.");
    }

    internal void FlashFromFile(PXDArchiveFile other)
    {
        Attributes = other.Attributes;
        Timestamp = other.Timestamp;
    }

    public bool RecordDataChange(byte[] newData)
    {
        WasCompressed = IsCompressed;
        DataHistory.Add(new(newData, false));
        Size = EntryLength = newData.Length;

        if (PXDArchiveOptions.Verbose)
            Console.WriteLine($"\tChanged data for {Name}. The current data length is {Data.Length}.");

        return true;
    }


    internal record DataRecord(byte[] Data, bool IsCompressed);
}