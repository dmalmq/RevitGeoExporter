using System;

namespace RevitGeoExporter.Core.Preview;

public readonly struct PreviewTileCacheEntry
{
    public PreviewTileCacheEntry(string path, bool exists, bool isFresh, DateTimeOffset lastWriteTimeUtc)
    {
        Path = path ?? string.Empty;
        Exists = exists;
        IsFresh = isFresh;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    public string Path { get; }

    public bool Exists { get; }

    public bool IsFresh { get; }

    public DateTimeOffset LastWriteTimeUtc { get; }
}
