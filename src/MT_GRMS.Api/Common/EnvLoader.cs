namespace MT_GRMS.Api.Common;

/// <summary>
/// Helper for loading environment variables from a .env file.
/// Searches current working directory, application base directory, and parent folders up to repository root.
/// </summary>
public static class EnvLoader
{
    public static void Load(string fileName = ".env")
    {
        var envPath = FindEnvFile(fileName);
        if (envPath == null || !File.Exists(envPath))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf("=");
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();

            // Strip wrapping single or double quotes
            if (value.Length >= 2 &&
                ((value.StartsWith(""") && value.EndsWith(""")) ||
                 (value.StartsWith("'") && value.EndsWith("'"))))
            {
                value = value[1..^1];
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? FindEnvFile(string fileName)
    {
        // 1. Try current working directory and walk up
        try
        {
            var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (var dir = currentDir; dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // Ignore directory search exceptions
        }

        // 2. Try AppContext.BaseDirectory and walk up
        try
        {
            var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var dir = baseDir; dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // Ignore directory search exceptions
        }

        return null;
    }
}
