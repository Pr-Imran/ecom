namespace FashionStore.Infrastructure.Services.Storage;

internal static class FileStoragePath
{
    public static string Normalize(string relativePath)
    {
        relativePath = relativePath.Replace('\\', '/');
        return relativePath.Trim('/');
    }

    public static string Combine(string directory, string fileName)
    {
        directory = Normalize(directory).TrimEnd('/');
        return string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{Normalize(fileName)}";
    }

    public static string GetDirectory(string relativePath)
    {
        var normalized = Normalize(relativePath);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[..lastSlash] : string.Empty;
    }

    public static string GetFileName(string relativePath)
    {
        var normalized = Normalize(relativePath);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
    }

    public static string GetExtension(string relativePath)
    {
        var fileName = GetFileName(relativePath);
        var lastDot = fileName.LastIndexOf('.');
        return lastDot >= 0 ? fileName[lastDot..] : string.Empty;
    }

    public static string GetFileNameWithoutExtension(string relativePath)
    {
        var fileName = GetFileName(relativePath);
        var lastDot = fileName.LastIndexOf('.');
        return lastDot >= 0 ? fileName[..lastDot] : fileName;
    }
}
