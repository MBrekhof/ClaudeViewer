namespace ClaudeViewer.Services;

internal static class FileReader
{
    public static async Task<string> ReadAllTextWithRetryAsync(string path)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                return await reader.ReadToEndAsync();
            }
            catch (IOException) when (attempt < 3)
            {
                await Task.Delay(60 * (attempt + 1));
            }
        }
        return string.Empty;
    }
}
