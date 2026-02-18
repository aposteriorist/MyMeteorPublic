using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;


namespace MyMeteor.IO;

public class MyBinaryWriter : BinaryWriter
{
    public Endianness StreamEndianness { get; internal set; } = Endianness.Little;
    private readonly Stack<long> _positionStack = [];

    public long Position
    {
        get => OutStream.Position;
        set => OutStream.Position = value;
    }

    public long Length
    {
        get => OutStream.Length;
        set => OutStream.SetLength(value);
    }


    protected MyBinaryWriter() : base()
    {
    }

    public MyBinaryWriter(Stream output, bool leaveOpen = false, Endianness streamEndianness = Endianness.Little)
        : this(output, Encoding.UTF8, leaveOpen, streamEndianness)
    {
    }

    public MyBinaryWriter(Stream output, Encoding encoding, Endianness streamEndianness = Endianness.Little)
        : this(output, encoding, false, streamEndianness)
    {
    }

    public MyBinaryWriter(Stream output, Encoding encoding, bool leaveOpen, Endianness streamEndianness)
        : base(output, encoding, leaveOpen)
    {
        StreamEndianness = streamEndianness;
    }


    public override void Write(double value) => Write(value, StreamEndianness);

    public void Write(double value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
            base.Write(value);

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(double)];
            BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
            OutStream.Write(buffer);
        }
    }

    // Writes a two-byte signed integer to this stream. The current position of
    // the stream is advanced by two.
    //
    public override void Write(short value) => Write(value, StreamEndianness);

    public void Write(short value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
            base.Write(value);

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(short)];
            BinaryPrimitives.WriteInt16BigEndian(buffer, value);
            OutStream.Write(buffer);
        }
    }

    // Writes a two-byte unsigned integer to this stream. The current position
    // of the stream is advanced by two.
    //
    public override void Write(ushort value) => Write(value, StreamEndianness);

    public void Write(ushort value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
            base.Write(value);

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
            OutStream.Write(buffer);
        }
    }

    // Writes a four-byte signed integer to this stream. The current position
    // of the stream is advanced by four.
    //
    public override void Write(int value) => Write(value, StreamEndianness);

    public void Write(int value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
            base.Write(value);

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            OutStream.Write(buffer);
        }
    }

    // Writes a four-byte unsigned integer to this stream. The current position
    // of the stream is advanced by four.
    //
    public override void Write(uint value) => Write(value, StreamEndianness);

    public void Write(uint value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
            base.Write(value);

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            OutStream.Write(buffer);
        }
    }

    // Writes an eight-byte signed integer to this stream. The current position
    // of the stream is advanced by eight.
    //
    public override void Write(long value) => Write(value, StreamEndianness);

    public void Write(long value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
            base.Write(value);

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(buffer, value);
            OutStream.Write(buffer);
        }
    }

    // Writes an eight-byte unsigned integer to this stream. The current
    // position of the stream is advanced by eight.
    //
    public override void Write(ulong value) => Write(value, StreamEndianness);

    public void Write(ulong value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
            base.Write(value);

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
            OutStream.Write(buffer);
        }
    }

    // Writes a float to this stream. The current position of the stream is
    // advanced by four.
    //
    public override void Write(float value) => Write(value, StreamEndianness);

    public void Write(float value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
            base.Write(value);

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(float)];
            BinaryPrimitives.WriteSingleBigEndian(buffer, value);
            OutStream.Write(buffer);
        }
    }

    // Writes a half to this stream. The current position of the stream is
    // advanced by two.
    //
    public override void Write(Half value) => Write(value, StreamEndianness);

    public void Write(Half value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
            base.Write(value);

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(ushort) /* = sizeof(Half) */];
            BinaryPrimitives.WriteHalfBigEndian(buffer, value);
            OutStream.Write(buffer);
        }
    }

    /// <summary>
    /// Writes a UInt24 to this stream.
    /// </summary>
    public void Write(UInt24 value) => Write(value, StreamEndianness);

    /// <summary>
    /// Writes a UInt24 to this stream using the requested endianness.
    /// </summary>
    public void Write(UInt24 value, Endianness endianness)
    {
        if (BitConverter.IsLittleEndian == (endianness == Endianness.Little))
            base.Write((Span<byte>)value);
        else
        {
            base.Write(value[2]);
            base.Write(value[1]);
            base.Write(value[0]);
        }
    }

    /// <summary>
    /// Writes a UInt128 to this stream using the requested endianness.
    /// </summary>
    public void Write(UInt128 value, Endianness endianness)
    {
        Span<byte> buffer = stackalloc byte[16];
        if (endianness == Endianness.Big)
            BinaryPrimitives.WriteUInt128BigEndian(buffer, value);
        else
            BinaryPrimitives.WriteUInt128LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    /// <summary>
    /// Writes an Int128 to this stream using the requested endianness.
    /// </summary>
    public void Write(Int128 value, Endianness endianness)
    {
        Span<byte> buffer = stackalloc byte[16];
        if (endianness == Endianness.Big)
            BinaryPrimitives.WriteInt128BigEndian(buffer, value);
        else
            BinaryPrimitives.WriteInt128LittleEndian(buffer, value);
        OutStream.Write(buffer);
    }

    // Writes a Vector2 to this stream. The current position of the stream is
    // advanced by eight.
    //
    public void Write(Vector2 value) => Write(value, StreamEndianness);

    public void Write(Vector2 value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
        {
            base.Write(value.X);
            base.Write(value.Y);
        }

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(float)];
            BinaryPrimitives.WriteSingleBigEndian(buffer, value.X);
            OutStream.Write(buffer);
            BinaryPrimitives.WriteSingleBigEndian(buffer, value.Y);
            OutStream.Write(buffer);
        }
    }

    // Writes a Vector3 to this stream. The current position of the stream is
    // advanced by twelve.
    //
    public void Write(Vector3 value) => Write(value, StreamEndianness);

    public void Write(Vector3 value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
        {
            base.Write(value.X);
            base.Write(value.Y);
            base.Write(value.Z);
        }

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(float)];
            BinaryPrimitives.WriteSingleBigEndian(buffer, value.X);
            OutStream.Write(buffer);
            BinaryPrimitives.WriteSingleBigEndian(buffer, value.Y);
            OutStream.Write(buffer);
            BinaryPrimitives.WriteSingleBigEndian(buffer, value.Z);
            OutStream.Write(buffer);
        }
    }

    // Writes a Vector4 to this stream. The current position of the stream is
    // advanced by sixteen.
    //
    public void Write(Vector4 value) => Write(value, StreamEndianness);

    public void Write(Vector4 value, Endianness endianness)
    {
        if (endianness == Endianness.Little)
        {
            base.Write(value.X);
            base.Write(value.Y);
            base.Write(value.Z);
            base.Write(value.W);
        }

        else
        {
            Span<byte> buffer = stackalloc byte[sizeof(float)];
            BinaryPrimitives.WriteSingleBigEndian(buffer, value.X);
            OutStream.Write(buffer);
            BinaryPrimitives.WriteSingleBigEndian(buffer, value.Y);
            OutStream.Write(buffer);
            BinaryPrimitives.WriteSingleBigEndian(buffer, value.Z);
            OutStream.Write(buffer);
            BinaryPrimitives.WriteSingleBigEndian(buffer, value.W);
            OutStream.Write(buffer);
        }
    }


    public void PushMark() => _positionStack.Push(OutStream.Position);
    public long PopMark() => _positionStack.Pop();
    public long Seek(long offset, SeekOrigin origin = SeekOrigin.Begin) => OutStream.Seek(offset, origin);
    public long Skip(long offset) => OutStream.Seek(offset, SeekOrigin.Current);

    public long PushForward(long offset, SeekOrigin origin = SeekOrigin.Begin)
    {
        _positionStack.Push(OutStream.Position);
        return OutStream.Seek(offset, origin);
    }

    public long PushForwardToEnd()
    {
        _positionStack.Push(OutStream.Position);
        return OutStream.Seek(OutStream.Length, SeekOrigin.Begin);
    }

    public long PopBack() => OutStream.Seek(_positionStack.Pop(), SeekOrigin.Begin);

    public void InPosition(Action action, long offset, SeekOrigin origin = SeekOrigin.Begin)
    {
        ArgumentNullException.ThrowIfNull(action);

        PushForward(offset, origin);
        action();
        PopBack();
    }

    public void FlipStreamEndianness() => StreamEndianness = (StreamEndianness == Endianness.Little ? Endianness.Big : Endianness.Little);

    // Let the stream's implementation handle exceptions for length adjustments.
    public void SetLength(long newLength) => OutStream.SetLength(newLength);
    public void AdjustLength(long adj) => OutStream.SetLength(OutStream.Length + adj);

    public long PadTo(long boundary)
    {
        long diff = OutStream.Length % boundary;

        if (diff != 0)
            OutStream.SetLength(OutStream.Length + boundary - diff);

        return diff;
    }
}
