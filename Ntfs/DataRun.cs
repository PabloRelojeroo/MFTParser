namespace MftParser.Ntfs;

public readonly struct DataRun {
    public DataRun(long lcn, long length) {
        Lcn = lcn;
        Length = length;
    }

    public long Lcn { get; }
    public long Length { get; }
    public bool IsSparse => Lcn < 0;

    public static List<DataRun> Decode(byte[] b, int at, int end) {
        var runs = new List<DataRun>();
        long lcn = 0;
        while (at < end) {
            var header = b[at++];
            if (header == 0) break;

            var lengthSize = header & 0x0F;
            var offsetSize = header >> 4;
            if (lengthSize == 0 || lengthSize > 8 || offsetSize > 8 || at + lengthSize + offsetSize > end) break;

            var length = Bytes.Unsigned(b, at, lengthSize);
            at += lengthSize;

            if (offsetSize == 0) {
                runs.Add(new DataRun(-1, length));
                continue;
            }

            lcn += Bytes.Signed(b, at, offsetSize);
            at += offsetSize;
            runs.Add(new DataRun(lcn, length));
        }
        return runs;
    }
}
