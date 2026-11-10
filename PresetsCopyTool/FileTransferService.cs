
namespace PresetsCopyTool;

public sealed record FileTransferResult(
    int CopiedFiles,
    int SkippedFiles,
    int FailedFiles,
    long CopiedBytes,
    List<string> Errors);

public sealed class FileTransferService
{
    private const int BufferSize = 1024 * 1024;

    public async Task<FileTransferResult> CopyAsync(
        IEnumerable<FileInfo> files,
        string outputFolder,
        string prefix,
        Action<long>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        int copied = 0;
        int skipped = 0;
        int failed = 0;

        long copiedBytes = 0;

        var errors = new List<string>();
        byte[] buffer = new byte[BufferSize];

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string destinationPath = Path.Combine(
                outputFolder,
                prefix + file.Name);

            bool destinationCreated = false;

            try
            {
                if (File.Exists(destinationPath))
                {
                    skipped++;
                    continue;
                }

                await using var source = new FileStream(
                    file.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    BufferSize,
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan);

                await using var destination = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan);

                destinationCreated = true;

                int bytesRead;

                while ((bytesRead = await source.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken)) > 0)
                {
                    await destination.WriteAsync(
                        buffer.AsMemory(0, bytesRead),
                        cancellationToken);

                    copiedBytes += bytesRead;
                    onProgress?.Invoke(bytesRead);
                }

                await destination.FlushAsync(cancellationToken);

                copied++;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                if (destinationCreated)
                {
                    try
                    {
                        File.Delete(destinationPath);
                    }
                    catch
                    {
                        // Ignore cleanup errors.
                    }
                }

                throw;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{file.Name}: {ex.Message}");

                if (destinationCreated)
                {
                    try
                    {
                        File.Delete(destinationPath);
                    }
                    catch
                    {
                        // Ignore cleanup errors.
                    }
                }
            }
        }

        return new FileTransferResult(
            copied,
            skipped,
            failed,
            copiedBytes,
            errors);
    }
}

