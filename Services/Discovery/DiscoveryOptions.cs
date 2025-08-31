using System;

namespace TVPPdfConverter.Services.Discovery;

public sealed class DiscoveryOptions
{
    // Max nested container depth (zip->zip->...)
    public int MaxDepth { get; init; } = 5;

    // Max total entries processed across all containers
    public int MaxEntries { get; init; } = 5000;

    // Max total uncompressed bytes processed across all entries
    public long MaxUncompressedBytes { get; init; } = 500_000_000; // 500 MB

    // Max single entry uncompressed size
    public long MaxEntryUncompressedBytes { get; init; } = 50_000_000; // 50 MB

    // Guard to detect abnormal compression ratios when metadata unavailable
    public double MaxCompressionRatio { get; init; } = 200.0; // 200x

    public bool AllowZip { get; init; } = true;
    public bool AllowRar { get; init; } = true;
}

