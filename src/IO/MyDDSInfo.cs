namespace MyMeteor.IO;

internal class MyDDSInfo
{
    private static readonly uint cigaM = 0x20534444;     // "DDS " in Little Endian
    private static readonly uint Magic = 0x44445320;

    internal static (int, int) GetDimensions (string filename)
    {
        // Assume the file exists.
        using FileStream file = File.OpenRead(filename);
        return GetDimensions(filename, file);
    }

    internal static (int, int) GetDimensions(string filename, Stream stream)
    {
        using MyBinaryReader reader = new(stream);

        uint magic = reader.ReadUInt32();
        if (magic != cigaM)
        {
            if (magic == Magic)
                reader.FlipStreamEndianness();
            else
                throw new Exception($"ERROR in {filename}: Not a proper DDS header!");
        }

        reader.Skip(8);
        // Height, width
        return (reader.ReadInt32(), reader.ReadInt32());
    }
}
