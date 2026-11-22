using System.Diagnostics;
using System.Text;

namespace PayDesk.Web.Services;

public class ConsolidatorRunner
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConsolidatorRunner> _logger;

    public ConsolidatorRunner(IConfiguration configuration, ILogger<ConsolidatorRunner> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(bool Success, string Output, string Error)> RunAsync(string month)
    {
        string? configuredPath = _configuration["Consolidator:ExecutablePath"] 
            ?? Environment.GetEnvironmentVariable("PAYDESK_CONSOLIDATOR_PATH");

        string executablePath;
        if (!string.IsNullOrEmpty(configuredPath) && Path.IsPathRooted(configuredPath))
        {
            executablePath = configuredPath;
        }
        else
        {
            string basePath = AppContext.BaseDirectory;
            string relativePath = configuredPath ?? "../PayDesk.Consolidator/bin/Debug/net10.0/PayDesk.Consolidator";
            executablePath = Path.GetFullPath(Path.Combine(basePath, relativePath));
        }

        // In case running on Linux with no extension or on Windows with .exe
        if (!File.Exists(executablePath) && File.Exists(executablePath + ".dll"))
        {
            executablePath = executablePath + ".dll";
        }

        string connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=paydesk_db;Username=paydesk;Password=paydesk_secret_password";

        string configuredDataDir = _configuration["Storage:DataDirectory"] 
            ?? Environment.GetEnvironmentVariable("PAYDESK_DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "../../data");
        string dataDir = Path.GetFullPath(configuredDataDir);

        _logger.LogInformation("Invoking Consolidator executable at: {Path} for month: {Month}", executablePath, month);

        var processStartInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (executablePath.EndsWith(".dll"))
        {
            processStartInfo.FileName = "dotnet";
            processStartInfo.Arguments = $"\"{executablePath}\" --month \"{month}\" --data-dir \"{dataDir}\" --connection-string \"{connectionString}\"";
        }
        else
        {
            processStartInfo.FileName = executablePath;
            processStartInfo.Arguments = $"--month \"{month}\" --data-dir \"{dataDir}\" --connection-string \"{connectionString}\"";
        }

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        try
        {
            using var process = new Process { StartInfo = processStartInfo };

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null) outputBuilder.AppendLine(args.Data);
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null) errorBuilder.AppendLine(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            bool success = process.ExitCode == 0;
            return (success, outputBuilder.ToString(), errorBuilder.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Consolidator process at {Path}", executablePath);
            return (false, outputBuilder.ToString(), $"Execution Exception: {ex.Message}");
        }
    }
}
