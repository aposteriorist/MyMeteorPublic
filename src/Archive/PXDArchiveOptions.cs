namespace MyMeteor.Archive;

/// <summary>
/// Global options used in PXDArchive and related classes.
/// </summary>
public class PXDArchiveOptions
{
    /// <summary>
    /// Whether to generate an archive manifest after file extraction.
    /// </summary>
    public static bool GenerateManifest { get; set; } = false;

    /// <summary>
    /// How the root directory will be included in the archive, if at all.
    /// </summary>
    public static PXDRootDirMode RootDirectoryMode { get; set; } = PXDRootDirMode.NotIncluded;

    /// <summary>
    /// How to record the total file size of the archive in the header, if at all.
    /// </summary>
    public static PXDFileSizeWriteMode FileSizeWriteMode { get; set; } = PXDFileSizeWriteMode.Write;

    /// <summary>
    /// Whether to pack data into exact space when possible.
    /// </summary>
    /// <remarks>Currently unimplemented.</remarks>
    public static bool PackInExactSpace { get; set; } = false;  // Unimplemented in PXDArchiveFile

    /// <summary>
    /// Whether to generate verbose output on operation.
    /// </summary>
    public static bool Verbose { get; set; } = false;

    /// <summary>
    /// Whether to suppress warnings.
    /// </summary>
    public static bool SuppressWarnings { get; set; } = false;
}


public enum PXDRootDirMode
{
    /// <summary>
    /// Do not include the root directory in the archive.
    /// </summary>
    NotIncluded,

    /// <summary>
    /// Include the root directory in the archive with a name of ".".
    /// </summary>
    WithDotName,

    /// <summary>
    /// Include the root directory in the archive with a name.
    /// </summary>
    /// <remarks>(PS3 auth archives have root directories whose name is a single space.)</remarks>
    WithName
}

public enum PXDFileSizeWriteMode
{
    /// <summary>
    /// Write the total file size after final alignment.
    /// </summary>
    /// <remarks>Seemingly used in archives written prior to September 21st, 2007.</remarks>
    WriteAligned,

    /// <summary>
    /// Write the total file size.
    /// </summary>
    Write,

    /// <summary>
    /// Do not write the total file size.
    /// </summary>
    NoWrite
}