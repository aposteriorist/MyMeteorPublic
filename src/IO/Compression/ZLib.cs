using System.IO.Compression;


namespace MyMeteor.IO.Compression;

public class ZLib
{
    #region Decode
    public static ReadOnlySpan<byte> Decode(byte[] encodedData, byte[]? decodedDataBuffer = null)
        => Decode(encodedData, 0, encodedData.Length, decodedDataBuffer, 0, decodedDataBuffer?.Length ?? 0);

    public static ReadOnlySpan<byte> Decode(byte[] encodedData, int expectedDecodedLength)
        => Decode(encodedData, 0, encodedData.Length, expectedDecodedLength);

    public static ReadOnlySpan<byte> Decode(byte[] encodedData, int inputIndex, int inputCount, int expectedDecodedLength = 0)
    {
        byte[]? decodedData = expectedDecodedLength > 0 ? new byte[expectedDecodedLength] : null;

        return Decode(encodedData, inputIndex, inputCount, decodedData, 0, expectedDecodedLength);
    }

    public static ReadOnlySpan<byte> Decode(byte[] encodedData, int inputIndex, int inputCount, byte[]? decodedDataBuffer, int outputIndex, int outputCount)
    {
        using MemoryStream inputStream = new(encodedData, inputIndex, inputCount);
        using MemoryStream outputStream = decodedDataBuffer != null ? new(decodedDataBuffer, outputIndex, outputCount) : new();

        Decode(inputStream, outputStream);

        if (decodedDataBuffer != null)
            return decodedDataBuffer.AsSpan(outputIndex, outputCount);
        else
            return outputStream.ToArray().AsSpan();
    }

    public static void Decode(Stream inputStream, Stream outputStream)
    {
        using ZLibStream decodingStream = new(inputStream, CompressionMode.Decompress);
        decodingStream.CopyTo(outputStream);
    }
    #endregion

    #region Encode
    public static ReadOnlySpan<byte> Encode(byte[] inputData, byte[]? encodedDataBuffer = null)
        => Encode(inputData, 0, inputData.Length, encodedDataBuffer, 0, encodedDataBuffer?.Length ?? 0);

    public static ReadOnlySpan<byte> Encode(byte[] inputData, int expectedEncodedLength)
        => Encode(inputData, 0, inputData.Length, expectedEncodedLength);

    public static ReadOnlySpan<byte> Encode(byte[] inputData, int inputIndex, int inputCount, int expectedEncodedLength = 0)
    {
        byte[]? encodedData = expectedEncodedLength > 0 ? new byte[expectedEncodedLength] : null;

        return Encode(inputData, inputIndex, inputCount, encodedData, 0, expectedEncodedLength);
    }

    public static ReadOnlySpan<byte> Encode(byte[] inputData, int inputIndex, int inputCount, byte[]? encodedDataBuffer, int outputIndex, int outputCount)
    {
        using MemoryStream inputStream = new(inputData, inputIndex, inputCount);
        using MemoryStream outputStream = encodedDataBuffer != null ? new(encodedDataBuffer, outputIndex, outputCount) : new();

        Encode(inputStream, outputStream);

        if (encodedDataBuffer != null)
            return encodedDataBuffer.AsSpan(outputIndex, outputCount);
        else
            return outputStream.ToArray().AsSpan();
    }

    public static void Encode(Stream inputStream, Stream outputStream)
    {
        /*
         * TO-DO: Use these settings.
         *  In C#, everything needed here is at least internal to System.IO.Compression (DeflateStream, Deflater class) if not deeper.
             level = 9
             method = 8
             windowBits = 12 (default is 15)
             memLevel = 5 (default is 7 or 8)
             strategy = 0
             version = "1.2.11"
             stream_size = 88
         */
        using ZLibStream encodingStream = new(outputStream, CompressionLevel.SmallestSize);
        inputStream.CopyTo(encodingStream);
    }
    #endregion
}
