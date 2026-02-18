using System.Runtime.CompilerServices;


namespace MyMeteor;

[InlineArray(3)]
public struct UInt24 : IEquatable<UInt24>
{
    private const uint MAX_VALUE = 0xFFFFFF;
    private byte b;

    public uint Value
    {
        get
        {
            if (BitConverter.IsLittleEndian)
                return (uint)(this[0] | this[1] << 8 | this[2] << 16);
            else
                return (uint)(this[0] << 16 | this[1] << 8 | this[2]);
        }

        set
        {
            if (value > MAX_VALUE) throw new ArgumentOutOfRangeException($"Value must be between 0 and {MAX_VALUE} inclusive.");

            this[1] = (byte)((value >> 8) & 0xFF);

            // Store appropriately based on system endianness.
            if (BitConverter.IsLittleEndian)
            {
                this[0] = (byte)(value & 0xFF);
                this[2] = (byte)((value >> 16) & 0xFF);
            }

            else
            {
                this[0] = (byte)((value >> 16) & 0xFF);
                this[2] = (byte)(value & 0xFF);
            }
        }
    }


    public UInt24(uint value) => Value = value;

    public UInt24(byte[] value)
    {
        if (value.Length > 3) throw new ArgumentOutOfRangeException(nameof(value), "UInt24 has a size of three bytes only.");

        this[0] = value[0];
        if (value.Length > 1) this[1] = value[1];
        if (value.Length > 2) this[2] = value[2];
    }


    public static explicit operator UInt24(ushort n) => new(n);
    public static explicit operator UInt24(uint n) => new(n);   // Throws on max value but I don't mind
    public static explicit operator int(UInt24 n) => (int) n.Value;
    public static explicit operator uint(UInt24 n) => n.Value;

    public static UInt24 operator +(UInt24 n) => n;
    public static int operator -(UInt24 n) => -(int)(n.Value);
    public static UInt24 operator ++(UInt24 n) => n + 1;
    public static UInt24 operator --(UInt24 n) => n - 1;

    public static UInt24 operator +(UInt24 m, UInt24 n) => new(m.Value + n.Value);
    public static UInt24 operator +(UInt24 m, uint n) => new(m.Value + n);
    public static uint operator +(uint m, UInt24 n) => m + n.Value;
    //public static int operator +(int m, UInt24 n) => m + (int)n.uValue;

    public static UInt24 operator -(UInt24 m, UInt24 n) => new(m.Value - n.Value);
    //public static UInt24 operator -(UInt24 m, int n) => new(m.iValue - n);
    public static UInt24 operator -(UInt24 m, uint n) => new(m.Value - n);
    public static uint operator -(uint m, UInt24 n) => m - n.Value;
    //public static int operator -(int m, UInt24 n) => m - (int)n.uValue;

    public static UInt24 operator *(UInt24 m, UInt24 n) => new(m.Value * n.Value);
    public static UInt24 operator *(UInt24 m, uint n) => new(m.Value * n);
    public static uint operator *(uint m, UInt24 n) => m * n.Value;

    public static UInt24 operator /(UInt24 m, UInt24 n) => new(m.Value / n.Value);
    public static UInt24 operator /(UInt24 m, uint n) => new(m.Value / n);
    public static uint operator /(uint m, UInt24 n) => m / n.Value;

    public static bool operator ==(UInt24 left, UInt24 right) => left.Equals(right);

    public static bool operator !=(UInt24 left, UInt24 right) => !(left == right);

    public override bool Equals(object? obj) => obj is UInt24 @uint && Equals(@uint);

    public bool Equals(UInt24 other) => this[0] == other[0] && this[1] == other[1] && this[2] == other[2];
}