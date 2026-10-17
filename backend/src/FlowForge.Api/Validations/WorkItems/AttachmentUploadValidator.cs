namespace FlowForge.Api.Validations.WorkItems;

public static class AttachmentUploadValidator
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedTypes = ["image/png", "image/jpeg", "application/pdf", "text/plain"];

    public static Dictionary<string, string[]> ValidateContentType(HttpRequest request) =>
        request.HasFormContentType ? [] : new() { ["file"] = ["Multipart form data is required."] };

    public static Dictionary<string, string[]> InvalidContent() =>
        new() { ["file"] = ["File content does not match its declared type."] };

    public static Dictionary<string, string[]> Validate(IFormFile? file)
    {
        var errors = new Dictionary<string, string[]>();
        if (file == null || file.Length < 1 || file.Length > MaxFileBytes)
            errors["file"] = ["File must be between 1 byte and 10 MiB."];
        if (file != null && !AllowedTypes.Contains(file.ContentType)) errors["contentType"] = ["Unsupported file type."];
        if (file != null && (string.IsNullOrWhiteSpace(file.FileName) || Path.GetFileName(file.FileName).Length > 255))
            errors["fileName"] = ["Invalid file name."];
        return errors;
    }

    public static async Task<bool> ValidSignatureAsync(string path, string type, CancellationToken ct)
    {
        var prefix = new byte[8];
        await using var file = File.OpenRead(path);
        var read = await file.ReadAsync(prefix, ct);
        return type switch
        {
            "image/png" => read == 8 && prefix.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => read >= 3 && prefix[0] == 255 && prefix[1] == 216 && prefix[2] == 255,
            "application/pdf" => read >= 5 && prefix[0] == 37 && prefix[1] == 80 && prefix[2] == 68 && prefix[3] == 70 && prefix[4] == 45,
            "text/plain" => !prefix.Take(read).Any(x => x == 0),
            _ => false
        };
    }
}
