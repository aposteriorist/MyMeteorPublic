using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;


namespace MyMeteor.IO;

public class MyBinaryReader : BinaryReader
{
    public Endianness StreamEndianness { get; internal set; } = Endianness.Little;
    private readonly Stack<long> _positionStack = [];

    public long Position
    {
        get => BaseStream.Position;
        set => BaseStream.Position = value;
    }

    public long Length => BaseStream.Length;

    public MyBinaryReader(Stream input, bool leaveOpen = false, Endianness streamEndianness = Endianness.Little)
        : this(input, Encoding.UTF8, leaveOpen, streamEndianness)
    {
    }

    public MyBinaryReader(Stream input, Encoding encoding, Endianness streamEndianness = Endianness.Little)
        : this(input, encoding, false, streamEndianness)
    {
    }

    public MyBinaryReader(Stream input, Encoding encoding, bool leaveOpen, Endianness streamEndianness)
        : base(input, encoding, leaveOpen)
    {
        StreamEndianness = streamEndianness;
    }

    // Missing out on the benefits of InternalRead for big endian values here, but the penalty is worth not completely reimplementing BinaryReader.
    public override short ReadInt16() => ReadInt16(StreamEndianness);
    public short ReadInt16(Endianness endianness) => endianness == Endianness.Big ? BinaryPrimitives.ReadInt16BigEndian(ReadBytes(sizeof(short))) : base.ReadInt16();

    public override ushort ReadUInt16() => ReadUInt16(StreamEndianness);
    public ushort ReadUInt16(Endianness endianness) => endianness == Endianness.Big ? BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(sizeof(ushort))) : base.ReadUInt16();

    public override int ReadInt32() => ReadInt32(StreamEndianness);
    public int ReadInt32(Endianness endianness) => endianness == Endianness.Big ? BinaryPrimitives.ReadInt32BigEndian(ReadBytes(sizeof(int))) : base.ReadInt32();

    public override uint ReadUInt32() => ReadUInt32(StreamEndianness);
    public uint ReadUInt32(Endianness endianness) => endianness == Endianness.Big ? BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(sizeof(uint))) : base.ReadUInt32();

    public override long ReadInt64() => ReadInt64(StreamEndianness);
    public long ReadInt64(Endianness endianness) => endianness == Endianness.Big ? BinaryPrimitives.ReadInt64BigEndian(ReadBytes(sizeof(long))) : base.ReadInt64();

    public override ulong ReadUInt64() => ReadUInt64(StreamEndianness);
    public ulong ReadUInt64(Endianness endianness) => endianness == Endianness.Big ? BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(sizeof(ulong))) : base.ReadUInt64();

    public override Half ReadHalf() => ReadHalf(StreamEndianness);
    public Half ReadHalf(Endianness endianness) => endianness == Endianness.Big ? BinaryPrimitives.ReadHalfBigEndian(ReadBytes(sizeof(ushort) /* = sizeof(Half) */)) : base.ReadHalf();

    public override float ReadSingle() => ReadSingle(StreamEndianness);
    public float ReadSingle(Endianness endianness) => endianness == Endianness.Big ? BinaryPrimitives.ReadSingleBigEndian(ReadBytes(sizeof(float))) : base.ReadSingle();

    public override double ReadDouble() => ReadDouble(StreamEndianness);
    public double ReadDouble(Endianness endianness) => endianness == Endianness.Big ? BinaryPrimitives.ReadDoubleBigEndian(ReadBytes(sizeof(double))) : base.ReadDouble();

    public UInt24 ReadUInt24() => ReadUInt24(StreamEndianness);
    public UInt24 ReadUInt24(Endianness endianness)
    {
        if (BitConverter.IsLittleEndian == (endianness == Endianness.Little))
            return new UInt24(base.ReadBytes(3));
        else
            return new UInt24([.. base.ReadBytes(3).Reverse()]);
    }

    public Int128 ReadInt128() => ReadInt128(StreamEndianness);
    public Int128 ReadInt128(Endianness endianness)
        => endianness == Endianness.Big
        ? BinaryPrimitives.ReadInt128BigEndian(ReadBytes(16))
        : BinaryPrimitives.ReadInt128LittleEndian(ReadBytes(16));

    public UInt128 ReadUInt128() => ReadUInt128(StreamEndianness);
    public UInt128 ReadUInt128(Endianness endianness)
        => endianness == Endianness.Big
        ? BinaryPrimitives.ReadUInt128BigEndian(ReadBytes(16))
        : BinaryPrimitives.ReadUInt128LittleEndian(ReadBytes(16));

    public Vector2 ReadVector2() => new(ReadSingle(), ReadSingle());
    public Vector2 ReadVector2(Endianness endianness) => new(ReadSingle(endianness), ReadSingle(endianness));

    public Vector3 ReadVector3() => new(ReadSingle(), ReadSingle(), ReadSingle());
    public Vector3 ReadVector3(Endianness endianness) => new(ReadSingle(endianness), ReadSingle(endianness), ReadSingle(endianness));

    public Vector4 ReadVector4() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
    public Vector4 ReadVector4(Endianness endianness) => new(ReadSingle(endianness), ReadSingle(endianness), ReadSingle(endianness), ReadSingle(endianness));

    public byte[] ReadBytes(UInt24 count) => ReadBytes((int)count);

    public byte[] CopyAllBytes()
    {
        byte[] all = new byte[BaseStream.Length];

        InPosition(() => ReadExactly(all), 0);

        return all;
    }

    public byte[] CopyToEnd()
    {
        byte[] rest = new byte[BaseStream.Length - BaseStream.Position];

        InPosition(() => ReadExactly(rest), BaseStream.Position);

        return rest;
    }

    public void PushMark() => _positionStack.Push(BaseStream.Position);
    public long PopMark() => _positionStack.Pop();
    public long Seek(long offset, SeekOrigin origin = SeekOrigin.Begin) => BaseStream.Seek(offset, origin);
    public long Skip(long offset) => BaseStream.Seek(offset, SeekOrigin.Current);

    public long PushForward(long offset, SeekOrigin origin = SeekOrigin.Begin)
    {
        _positionStack.Push(BaseStream.Position);
        return BaseStream.Seek(offset, origin);
    }

    public long PopBack() => BaseStream.Seek(_positionStack.Pop(), SeekOrigin.Begin);

    public void InPosition(Action action, long offset, SeekOrigin origin = SeekOrigin.Begin)
    {
        ArgumentNullException.ThrowIfNull(action);

        PushForward(offset, origin);
        action();
        PopBack();
    }

    public void FlipStreamEndianness() => StreamEndianness = (StreamEndianness == Endianness.Little ? Endianness.Big : Endianness.Little);
}