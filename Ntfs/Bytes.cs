namespace MftParser.Ntfs;

static class Bytes {
    public static ushort U16(byte[] b, int at) => (ushort)(b[at] | b[at + 1] << 8);
    public static uint U32(byte[] b, int at) => BitConverter.ToUInt32(b, at);
    public static long I64(byte[] b, int at) => BitConverter.ToInt64(b, at);
    public static ulong U64(byte[] b, int at) => BitConverter.ToUInt64(b, at);

    public static long Unsigned(byte[] b, int at, int size) {
        long value = 0;
        for (var i = size - 1; i >= 0; i--) value = value << 8 | b[at + i];
        return value;
    }

    public static long Signed(byte[] b, int at, int size) {
        var value = Unsigned(b, at, size);
        var shift = 64 - size * 8;
        return value << shift >> shift;
    }
}
