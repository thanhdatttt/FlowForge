namespace FlowForge.Api;

// Coordinates private file changes with the enclosing workspace database transaction.
public sealed class AttachmentFileActions
{
    private readonly List<string> deleteAfterCommit = [];
    private readonly List<string> deleteOnFailure = [];

    public void DeleteAfterCommit(string path) => deleteAfterCommit.Add(path);
    public void DeleteOnFailure(string path) => deleteOnFailure.Add(path);

    public void Committed(ILogger logger)
    {
        Delete(deleteAfterCommit, logger);
        deleteOnFailure.Clear();
    }

    public void Failed(ILogger logger)
    {
        Delete(deleteOnFailure, logger);
        deleteAfterCommit.Clear();
    }

    private static void Delete(List<string> paths, ILogger logger)
    {
        foreach (var path in paths)
        {
            try { File.Delete(path); }
            catch (IOException error) { logger.LogWarning(error, "Could not clean up a private attachment file."); }
            catch (UnauthorizedAccessException error) { logger.LogWarning(error, "Could not clean up a private attachment file."); }
        }
        paths.Clear();
    }
}
