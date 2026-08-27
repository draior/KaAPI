namespace KaApi.Models;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string RootPath { get; set; } = @"C:\Temp\KaAPI\";

    public string TraceFolderName { get; set; } = "trace";

    public string PayloadFolderName { get; set; } = "payload";
}
