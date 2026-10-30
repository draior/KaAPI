namespace KaApi.Models;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string RootPath { get; set; } = @"C:\Temp\KaAPI\";

    public string TraceFolderName { get; set; } = "trace";

    public int TraceRetentionDays { get; set; } = 30;

    public string PayloadFolderName { get; set; } = "payload";
}
