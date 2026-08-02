namespace FashionStore.Application.DTOs.Images;

public sealed record StoredFileResult(
    string RelativePath,
    string Url,
    long SizeBytes
);

public sealed record UploadedFileInput(
    Stream Content,
    string OriginalFileName,
    string ContentType,
    long Length
);
