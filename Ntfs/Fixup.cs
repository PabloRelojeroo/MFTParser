namespace MftParser.Ntfs;

static class Fixup {
    const int Stride = 512;

    public static bool Apply(byte[] b, int at, int recordSize) {
        int arrayOffset = Bytes.U16(b, at + 4);
        int arrayCount = Bytes.U16(b, at + 6);
        if (arrayCount < 2 || arrayOffset + arrayCount * 2 > recordSize || (arrayCount - 1) * Stride > recordSize) return false;

        var usnLow = b[at + arrayOffset];
         var usnHigh = b[at + arrayOffset + 1];
        for (var i = 1; i < arrayCount; i++) {
            var tail = at + i * Stride - 2;
            if (b[tail] != usnLow || b[tail + 1] != usnHigh) return false;
            b[tail] = b[at + arrayOffset + i * 2];
            b[tail + 1] = b[at + arrayOffset + i * 2 + 1];
        }
        return true;
    }
}
