namespace MyMeteor.Archive;

public class PXDArchiveOptions
{
    public static bool GenerateManifest { get; set; } = false;
    public static PXDRootDirMode RootDirectoryMode { get; set; } = PXDRootDirMode.NotIncluded;
    public static PXDFileSizeWriteMode WriteTotalFileSize { get; set; } = PXDFileSizeWriteMode.Write;
    public static bool PackInExactSpace { get; set; } = false;  // Unimplemented in PXDArchiveFile
    public static bool Verbose { get; set; } = false;
    public static bool SuppressWarnings { get; set; } = false;
}

public enum PXDRootDirMode
{
    NotIncluded,
    WithDotName,
    WithName
}

public enum PXDFileSizeWriteMode
{
    WriteAligned,   // In PARs written prior to September 21st, 2007
    Write,
    NoWrite
}