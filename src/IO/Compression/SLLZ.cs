using System;
using System.IO.Compression;


namespace MyMeteor.IO.Compression;

/// <summary>
/// A class enabling SLLZ encoding and decoding.
/// </summary>
/// <remarks>(SL for Standard Library, LZ for Lempel-Ziv.)</remarks>
public class SLLZ
{
    /// <summary>
    /// The header signature for SLLZ-encoded data.
    /// </summary>
    public static readonly byte[] Magic = [0x53, 0x4C, 0x4C, 0x5A];  // Regardless of endianness

    private const int MAX_WINDOW_SIZE = 4096;
    private const int MAX_MATCH_LENGTH = 18;
    private const int MATCH_THRESHOLD = 3;


    #region HeaderedDecode
    /// <summary>
    /// Attempt to decode the input data.
    /// </summary>
    /// <returns>The decoded data.</returns>
    public static byte[] Decode(byte[] inputData)
    {
        using MemoryStream inputStream = new(inputData);
        using MyBinaryReader reader = new(inputStream);
        return Decode(reader);
    }

    /// <summary>
    /// Attempt to decode the input data.
    /// </summary>
    /// <returns>The decoded data.</returns>
    public static byte[] Decode(Stream inputStream)
    {
        using MyBinaryReader reader = new(inputStream, true);
        return Decode(reader);
    }

    /// <summary>
    /// Attempt to decode the input data.
    /// </summary>
    /// <returns>The decoded data.</returns>
    public static byte[] Decode(MyBinaryReader reader)
    {
        if (!reader.ReadBytes(4).SequenceEqual(Magic))
        {
            Console.WriteLine("Not a valid SLLZ header!");
            reader.Skip(-4);
            throw new Exception("Not a valid SLLZ header!");    // Fine for now
        }

        reader.StreamEndianness = (Endianness) reader.ReadByte();

        byte version = reader.ReadByte();
        ushort dataOffset = reader.ReadUInt16();
        int decompressedSize = reader.ReadInt32();
        int size = reader.ReadInt32();

        if (reader.Position != dataOffset)
            reader.Seek(dataOffset);

        byte[] inputData = reader.ReadBytes(size);
        byte[] outputData = new byte[decompressedSize];

        if (version == 2)
            DecodeVersion2(inputData, outputData);
        else
            DecodeVersion1(inputData, outputData);

        return outputData;
    }
    #endregion

    #region HeaderedEncode
    /// <summary>
    /// Attempt to encode data with the requested parametres.
    /// </summary>
    /// <returns>The encoded data.</returns>
    public static byte[] Encode(BinaryReader reader, SLLZParameters param, uint dataSize = 0)
        => Encode(reader.BaseStream, param, dataSize);

    /// <summary>
    /// Attempt to encode data with the requested parametres.
    /// </summary>
    /// <returns>The encoded data.</returns>
    public static byte[] Encode(Stream inputStream, SLLZParameters param, uint dataSize = 0)
    {
        long remainder = inputStream.Length - inputStream.Position;

        if (remainder < dataSize)
            throw new Exception($"Encoding of {dataSize} bytes requested, but {remainder} bytes remain ahead of stream position.");

        byte[] inputData = new byte[dataSize > 0 ? dataSize : remainder];
        inputStream.ReadExactly(inputData);
        return Encode(inputData, param);
    }

    /// <summary>
    /// Attempt to encode data with the requested parametres.
    /// </summary>
    /// <returns>The encoded data.</returns>
    public static byte[] Encode(byte[] inputData, SLLZParameters param)
    {
        if (param.Version == SLLZVersion.V2 && inputData.Length < 27)
            throw new Exception("SLLZ version 2 cannot encode data shorter than 27 bytes in length.");

        ReadOnlySpan<byte> encodedData = param.Version == SLLZVersion.V2 ? EncodeVersion2(inputData) : EncodeVersion1(inputData);

        byte[] outputData = new byte[encodedData.Length + 0x10];

        using MemoryStream stream = new(outputData);
        using MyBinaryWriter writer = new(stream, false, param.Endianness);

        writer.Write(Magic);
        writer.Write((byte) param.Endianness);
        writer.Write((byte) param.Version);
        writer.Write((ushort) 0x10);
        writer.Write(inputData.Length);
        writer.Write(outputData.Length);
        writer.Write(encodedData);

        return outputData;
    }
    #endregion

    #region EncodeVersion1
    private static Tuple<int, int>? FindMatch(ReadOnlySpan<byte> inputData, int inputPosition, int windowSize, int maxOffsetLength)
    {
        ReadOnlySpan<byte> data = inputData.Slice(inputPosition - windowSize, windowSize);

        int currentLength = maxOffsetLength;

        int pos;
        while (currentLength >= MATCH_THRESHOLD)
        {
            ReadOnlySpan<byte> pattern = inputData.Slice(inputPosition, currentLength);

            pos = data.LastIndexOf(pattern);

            if (pos >= 0)
            {
                return new(windowSize - pos, currentLength);
            }

            currentLength--;
        }

        return null;
    }


    /// <summary>
    /// Attempt to encode data using SLLZ version 1.
    /// </summary>
    /// <param name="inputData">The input data to be encoded.</param>
    /// <remarks>Throws an exception if the encoded data is larger than anticipated.</remarks>
    public static ReadOnlySpan<byte> EncodeVersion1(ReadOnlySpan<byte> inputData)
    {
        int bufferSize = inputData.Length + 2048;
        Span<byte> outputData = new byte[bufferSize];

        int inputPosition = 0;
        int outputPosition = 0;
        byte currentFlags = 0;
        int bitCount = 0;
        int flagPosition = outputPosition;

        void VerifyOutputPositionWithinBounds()
        {
            if (outputPosition >= bufferSize)
                throw new Exception("Compressed size is bigger than original size.");
        }

        outputData[flagPosition] = 0x00;
        outputPosition++;
        VerifyOutputPositionWithinBounds();

        while (inputPosition < inputData.Length)
        {
            Tuple<int, int>? match = FindMatch(inputData, inputPosition,
                Math.Min(inputPosition, MAX_WINDOW_SIZE),
                Math.Min(inputData.Length - inputPosition, MAX_MATCH_LENGTH));

            bool matched = match != null;

            // If a match was found, mark it for compression.
            if (matched)
                currentFlags |= (byte)(1 << 7 - bitCount);

            bitCount++;

            // Write and flush the current flag byte if it's full.
            if (bitCount == 8)
            {
                outputData[flagPosition] = currentFlags;

                currentFlags = 0x00;
                bitCount = 0;
                flagPosition = outputPosition;
                outputData[flagPosition] = 0x00;

                outputPosition++;
                VerifyOutputPositionWithinBounds();
            }

            // If a match was found, compress and write it.
            if (matched)
            {
                var offset = (short)(match.Item1 - 1 << 4);
                var size   = (short)(match.Item2 - MATCH_THRESHOLD & 0x0F);
                var tuple  = (short)(offset | size);

                outputData[outputPosition] = (byte)tuple;
                inputPosition += match.Item2;

                outputPosition++;
                VerifyOutputPositionWithinBounds();

                outputData[outputPosition] = (byte)(tuple >> 8);
            }

            // Otherwise, write an uncompressed byte from input.
            else
            {
                outputData[outputPosition] = inputData[inputPosition];
                inputPosition++;
            }

            outputPosition++;
            VerifyOutputPositionWithinBounds();
        }

        // Write the final flag byte.
        outputData[flagPosition] = currentFlags;

        // Return the compressed buffer truncated to the actual size of the output.
        return outputData[..outputPosition];
    }
    #endregion

    #region DecodeVersion1
    /// <summary>
    /// Decode data using SLLZ version 1.
    /// </summary>
    /// <param name="inputData">The encoded input data.</param>
    /// <param name="outputData">A suitably-sized span for output.</param>
    public static void DecodeVersion1(ReadOnlySpan<byte> inputData, Span<byte> outputData)
    {
        int inputPosition = 0;
        int outputPosition = 0;

        bool compressed;
        ushort copyFlags;
        int copyDistance, copyCount;

        byte currentFlags = inputData[inputPosition];
        inputPosition++;
        uint bitCount = 8;

        while (outputPosition < outputData.Length)
        {
            // Check if the next flag indicates compression.
            compressed = (currentFlags & 0x80) == 0x80;

            currentFlags <<= 1;
            bitCount--;

            // If this byte has been exhausted, move on to the next.
            if (bitCount == 0)
            {
                currentFlags = inputData[inputPosition];
                inputPosition++;
                bitCount = 8;
            }

            // If there is a compression, decompress it.
            if (compressed)
            {
                copyFlags = (ushort)(inputData[inputPosition] | inputData[inputPosition + 1] << 8);

                copyDistance = 1 + (copyFlags >> 4);
                copyCount = MATCH_THRESHOLD + (copyFlags & 0xF);
                inputPosition += 2;

                while (copyCount > 0)
                {
                    outputData[outputPosition] = outputData[outputPosition - copyDistance];
                    outputPosition++;
                    copyCount--;
                }
            }

            // Otherwise, the next byte is not compressed.
            else
            {
                outputData[outputPosition] = inputData[inputPosition];
                inputPosition++;
                outputPosition++;
            }
        }
    }
    #endregion

    #region EncodeVersion2
    /// <summary>
    /// Encode data using SLLZ version 2.
    /// </summary>
    /// <param name="inputData">The input data to be encoded.</param>
    private static ReadOnlySpan<byte> EncodeVersion2(byte[] inputData)
    {
        MemoryStream outputDataStream = new();
        using MyBinaryWriter writer = new(outputDataStream, false, Endianness.Big); // For some reason, chunk info is BE
        using MyBinaryReader reader = new(new MemoryStream(inputData));

        int chunkSize;
        byte[] chunk;
        ReadOnlySpan<byte> encodedChunk;
        bool encodingIsLarger;

        while (reader.Position < inputData.Length)
        {
            chunkSize = Math.Min(inputData.Length - (int)reader.Position, 0x10000);
            chunk = reader.ReadBytes(chunkSize);

            encodedChunk = ZLib.Encode(chunk);

            // If the encoded chunk is smaller, write it.
            // Otherwise, write the original chunk and flag it as uncompressed.
            // Sanity note: I haven't tested this uncompressed option against an authentic SLLZv2.

            encodingIsLarger = encodedChunk.Length >= chunkSize;

            writer.Write((UInt24)(encodingIsLarger ? 0x800000 : encodedChunk.Length + 5));
            chunkSize--;
            writer.Write((ushort)chunkSize);
            writer.Write(encodingIsLarger ? chunk : encodedChunk);
        }

        return outputDataStream.ToArray();
    }
    #endregion

    #region DecodeVersion2
    /// <summary>
    /// Attempt to decode data using SLLZ version 2.
    /// </summary>
    /// <param name="inputData">The encoded input data.</param>
    /// <param name="outputData">A suitably-sized span for output.</param>
    /// <remarks>Throws an exception if a chunk's size is ever different than expected.</remarks>
    /// <exception cref="Exception"></exception>
    public static void DecodeVersion2(byte[] inputData, Span<byte> outputData)
    {
        using MyBinaryReader reader = new(new MemoryStream(inputData), false, Endianness.Big); // For some reason, chunk info is BE
        int outputPosition = 0;

        UInt24 encodedChunkSize;
        byte[] encodedChunk;
        int chunkSize;
        ReadOnlySpan<byte> chunk;

        while (outputPosition < outputData.Length)
        {
            bool encoded = (reader.PeekChar() >> 7) == 0;

            encodedChunkSize = reader.ReadUInt24();
            chunkSize = 1 + reader.ReadUInt16();

            if (encoded)
            {
                encodedChunk = reader.ReadBytes(encodedChunkSize - 5);
                chunk = ZLib.Decode(encodedChunk);

                if (chunkSize != chunk.Length)
                    throw new Exception($"The decoded chunk's size ({chunk.Length}) was different than the size ({chunkSize}) noted in the data.");
            }

            // This chunk is not compressed.
            else
                chunk = reader.ReadBytes(chunkSize);

            // Write the chunk.
            chunk.CopyTo(outputData.Slice(outputPosition, chunk.Length));
            outputPosition += chunk.Length;
        }
    }


    #endregion

    /// <summary>
    /// Get the current encoding parametres of an array of bytes, if it is SLLZ-headered.
    /// </summary>
    public static SLLZParameters GetEncoding(byte[] data)
    {
        return data.AsSpan()[..4].SequenceEqual(Magic)
            ? new((Endianness) data[4], (SLLZVersion) data[5])
            : new(Version: SLLZVersion.UNCOMPRESSED);
    }
}

public enum SLLZVersion
{
    UNCOMPRESSED = 0,
    VERSION1 = 1,
    VERSION2 = 2,

    /// <summary>
    /// Synonym for version 1.
    /// </summary>
    V1 = 1,

    /// <summary>
    /// Synonym for version 2.
    /// </summary>
    V2 = 2,
}

/// <summary>
/// Encoding parametres.
/// </summary>
/// <param name="Endianness">The endianness.</param>
/// <param name="Version">The SLLZ version.</param>
public record SLLZParameters(Endianness Endianness = Endianness.Little, SLLZVersion Version = SLLZVersion.V1);