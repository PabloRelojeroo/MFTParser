using System.IO;

namespace MftParser.Ntfs;

public readonly struct RecordChunk {
    public RecordChunk(long firstIndex, byte[] buffer, int count) {
        FirstIndex = firstIndex;
        Buffer = buffer;
        Count = count;
    }

    public long FirstIndex { get; }
    public byte[] Buffer { get; }
    public int Count { get; }
}

public abstract class RecordSource : IDisposable {
    protected const int ChunkBytes = 4 * 1024 * 1024;

    public int RecordSize { get; protected set; } = 1024;
    public long RecordCount { get; protected set; }
    public long TotalBytes => RecordCount * RecordSize;
    public string Root { get; protected set; } = "";
    public string Label { get; protected set; } = "";
    public string SourcePath { get; protected set; } = "";
    public bool IsLive { get; protected set; }
    public List<string> Notes { get; } = new();

    public abstract IEnumerable<RecordChunk> ReadAll(CancellationToken token);
    public abstract byte[] ReadRecord(long index);
    public abstract void Dispose();

    public static RecordSource Open(char letter) => new VolumeSource(letter);
    public static RecordSource Open(string path) => new FileSource(path);
}

sealed class VolumeSource : RecordSource {
    readonly RawVolume _volume;
    readonly List<DataRun> _runs;
    readonly long _clusterSize;
    readonly object _lock = new();

    public VolumeSource(char letter) {
        letter = char.ToUpperInvariant(letter);
        Root = $"{letter}:";
        Label = $@"{letter}:\";
        SourcePath = $@"{letter}:\$MFT";
        IsLive = true;
        _volume = new RawVolume(letter);

        try {
            var boot = _volume.Read(0, 512);
            if (Bytes.U64(boot, 3) != 0x202020205346544E) throw new InvalidDataException("NTFS");

            var sectorSize = (int)Bytes.U16(boot, 0x0B);
            int sectorsPerCluster = boot[0x0D];
            if (sectorsPerCluster > 0x80) sectorsPerCluster = 1 << (256 - sectorsPerCluster);
            _clusterSize = (long)sectorSize * sectorsPerCluster;
            _volume.Alignment = Math.Max(512, sectorSize);

            var recordClusters = (sbyte)boot[0x40];
            RecordSize = recordClusters > 0 ? (int)(recordClusters * _clusterSize) : 1 << -recordClusters;

            var mftOffset = Bytes.I64(boot, 0x30) * _clusterSize;
            var first = _volume.Read(mftOffset, RecordSize);
            if (!Fixup.Apply(first, 0, RecordSize)) throw new InvalidDataException("$MFT");

            var data = RecordParser.FindUnnamedData(first, RecordSize)
                ?? throw new InvalidDataException("$MFT $DATA");
            _runs = data.Runs;
            RecordCount = data.RealSize / RecordSize;
            if (data.HasAttributeList) Notes.Add("attrlist");
        } catch {
            _volume.Dispose();
            throw;
        }
    }

    public override IEnumerable<RecordChunk> ReadAll(CancellationToken token) {
        var chunkSize = (int)(ChunkBytes / RecordSize * RecordSize);
        long index = 0;
        foreach (var run in _runs) {
            var runBytes = run.Length * _clusterSize;
            var runRecords = runBytes / RecordSize;
            if (run.IsSparse) {
                index += runRecords;
                continue;
            }

            long done = 0;
            while (done < runBytes && index < RecordCount) {
                token.ThrowIfCancellationRequested();
                var bytes = (int)Math.Min(chunkSize, runBytes - done);
                var buffer = new byte[bytes];
                lock (_lock) _volume.ReadExact(run.Lcn * _clusterSize + done, buffer, bytes);

                var count = (int)Math.Min(bytes / RecordSize, RecordCount - index);
                yield return new RecordChunk(index, buffer, count);
                index += count;
                done += bytes;
            }
            if (index >= RecordCount) yield break;
        }
    }

    public override byte[] ReadRecord(long index) {
        var position = index * RecordSize;
        foreach (var run in _runs) {
            var runBytes = run.Length * _clusterSize;
            if (position < runBytes) {
                if (run.IsSparse) return new byte[RecordSize];
                lock (_lock) return _volume.Read(run.Lcn * _clusterSize + position, RecordSize);
            }
            position -= runBytes;
        }
        return new byte[RecordSize];
    }

    public override void Dispose() => _volume.Dispose();
}

sealed class FileSource : RecordSource {
    readonly FileStream _stream;
    readonly object _lock = new();

    public FileSource(string path) {
        Label = Path.GetFileName(path);
        SourcePath = Path.GetFullPath(path);
        Root = ".";
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);

        var header = new byte[64];
        if (_stream.Read(header, 0, header.Length) < header.Length || Bytes.U32(header, 0) != RecordParser.FileSignature) {
            _stream.Dispose();
            throw new InvalidDataException("FILE");
        }

        var allocated = (int)Bytes.U32(header, 0x1C);
        RecordSize = allocated is 1024 or 4096 ? allocated : 1024;
        RecordCount = _stream.Length / RecordSize;
    }

    public override IEnumerable<RecordChunk> ReadAll(CancellationToken token) {
        var chunkSize = ChunkBytes / RecordSize * RecordSize;
        long index = 0;
        while (index < RecordCount) {
            token.ThrowIfCancellationRequested();
            var count = (int)Math.Min(chunkSize / RecordSize, RecordCount - index);
            var buffer = new byte[count * RecordSize];
            lock (_lock) {
                _stream.Position = index * RecordSize;
                var done = 0;
                while (done < buffer.Length) {
                    var read = _stream.Read(buffer, done, buffer.Length - done);
                    if (read <= 0) break;
                    done += read;
                }
                count = done / RecordSize;
            }
            if (count == 0) yield break;
            yield return new RecordChunk(index, buffer, count);
            index += count;
        }
    }

    public override byte[] ReadRecord(long index) {
        var buffer = new byte[RecordSize];
        lock (_lock) {
            _stream.Position = index * RecordSize;
            _stream.Read(buffer, 0, RecordSize);
        }
        return buffer;
    }

    public override void Dispose() => _stream.Dispose();
}
