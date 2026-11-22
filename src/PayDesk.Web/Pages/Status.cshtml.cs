using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PayDesk.Core.Data;

namespace PayDesk.Web.Pages;

public class StatusModel : PageModel
{
    private readonly PayDeskDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StatusModel> _logger;

    public StatusModel(PayDeskDbContext db, IConfiguration configuration, ILogger<StatusModel> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public bool OverallHealthy => IcmpPingSuccess && DatabaseConnected;

    // ICMP Ping Results
    public string TargetHost { get; set; } = "localhost";
    public string ResolvedIp { get; set; } = "N/A";
    public bool IcmpPingSuccess { get; set; }
    public long IcmpRoundtripTimeMs { get; set; }
    public string IcmpStatusMessage { get; set; } = string.Empty;
    public string? IcmpExceptionDetails { get; set; }
    public bool IsPermissionError { get; set; }

    // Database Connection Results
    public bool DatabaseConnected { get; set; }
    public long DatabaseLatencyMs { get; set; }
    public string? DatabaseVersion { get; set; }
    public string? DatabaseErrorMessage { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    public async Task OnGetAsync()
    {
        CheckedAt = DateTime.UtcNow;

        // 1. Extract Target Host from Connection String
        string connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=paydesk_db;Username=paydesk;Password=paydesk_secret_password";

        try
        {
            var npgsqlBuilder = new NpgsqlConnectionStringBuilder(connectionString);
            TargetHost = npgsqlBuilder.Host ?? "localhost";
        }
        catch
        {
            TargetHost = "localhost";
        }

        // 2. Perform ICMP Ping
        await PerformIcmpPingAsync(TargetHost);

        // 3. Test Database Connectivity
        await TestDatabaseConnectionAsync();
    }

    private async Task PerformIcmpPingAsync(string host)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Resolve host to IP
            IPAddress? targetAddress = null;
            if (IPAddress.TryParse(host, out var parsedIp))
            {
                targetAddress = parsedIp;
            }
            else
            {
                var addresses = await Dns.GetHostAddressesAsync(host);
                targetAddress = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) 
                             ?? addresses.FirstOrDefault();
            }

            if (targetAddress == null)
            {
                IcmpPingSuccess = false;
                IcmpStatusMessage = $"DNS resolution failed for host '{host}'.";
                return;
            }

            ResolvedIp = targetAddress.ToString();

            using var ping = new Ping();
            // Send ICMP Echo Request with 2.5 second timeout
            var reply = await ping.SendPingAsync(targetAddress, 2500);
            sw.Stop();

            if (reply.Status == IPStatus.Success)
            {
                IcmpPingSuccess = true;
                IcmpRoundtripTimeMs = reply.RoundtripTime;
                IcmpStatusMessage = $"ICMP Echo Reply received from {reply.Address} (RTT: {reply.RoundtripTime} ms, TTL: {reply.Options?.Ttl ?? 0})";
            }
            else
            {
                IcmpPingSuccess = false;
                IcmpStatusMessage = $"ICMP Echo Request failed with status: {reply.Status}";
            }
        }
        catch (SocketException ex)
        {
            sw.Stop();
            IcmpPingSuccess = false;
            IcmpExceptionDetails = $"SocketException ({ex.SocketErrorCode}): {ex.Message}";
            IcmpStatusMessage = "Socket error occurred while attempting ICMP ping.";
            if (ex.SocketErrorCode == SocketError.AccessDenied || ex.Message.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase))
            {
                IsPermissionError = true;
            }
        }
        catch (PingException ex)
        {
            sw.Stop();
            IcmpPingSuccess = false;
            IcmpExceptionDetails = $"PingException: {ex.Message}";
            if (ex.InnerException != null)
            {
                IcmpExceptionDetails += $" (Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message})";
                if (ex.InnerException.Message.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) ||
                    ex.InnerException.Message.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
                {
                    IsPermissionError = true;
                }
            }
            IcmpStatusMessage = "PingException encountered while sending ICMP request.";
        }
        catch (Exception ex)
        {
            sw.Stop();
            IcmpPingSuccess = false;
            IcmpExceptionDetails = $"{ex.GetType().Name}: {ex.Message}";
            IcmpStatusMessage = "Unexpected exception during ICMP ping.";
        }
    }

    private async Task TestDatabaseConnectionAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            DatabaseConnected = await _db.Database.CanConnectAsync();
            sw.Stop();
            DatabaseLatencyMs = sw.ElapsedMilliseconds;

            if (DatabaseConnected)
            {
                try
                {
                    using var command = _db.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "SELECT version();";
                    await _db.Database.OpenConnectionAsync();
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        DatabaseVersion = reader.GetString(0);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query database version string.");
                }
                finally
                {
                    await _db.Database.CloseConnectionAsync();
                }
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            DatabaseConnected = false;
            DatabaseLatencyMs = sw.ElapsedMilliseconds;
            DatabaseErrorMessage = ex.Message;
        }
    }
}
