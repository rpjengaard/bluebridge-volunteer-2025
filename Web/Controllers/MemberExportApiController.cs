// [CHANGE: member export API endpoint] Related: Code/Services/IMemberListService.cs, Code/Services/MemberListService.cs
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Code.Services;
using Microsoft.AspNetCore.Mvc;

namespace Web.Controllers;

[ApiController]
public class MemberExportApiController : ControllerBase
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ApiKeyConfigKey = "MemberExportApi:ApiKey";

    private readonly IMemberListService _memberListService;
    private readonly IConfiguration _configuration;

    public MemberExportApiController(
        IMemberListService memberListService,
        IConfiguration configuration)
    {
        _memberListService = memberListService;
        _configuration = configuration;
    }

    [HttpGet("/api/members/export")]
    public async Task<IActionResult> Export([FromQuery] string? group = null)
    {
        var stopwatch = Stopwatch.StartNew();

        var configuredKey = _configuration[ApiKeyConfigKey];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return ErrorResponse(StatusCodes.Status403Forbidden,
                "Member export API is not configured", stopwatch);
        }

        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey) ||
            !FixedTimeEquals(providedKey.ToString(), configuredKey))
        {
            return ErrorResponse(StatusCodes.Status401Unauthorized,
                "Invalid or missing API key", stopwatch);
        }

        var members = await _memberListService.GetMemberExportAsync(group);
        stopwatch.Stop();

        return Ok(new
        {
            meta = new
            {
                count = members.Count,
                statusCode = StatusCodes.Status200OK,
                durationMs = stopwatch.ElapsedMilliseconds
            },
            members
        });
    }

    private ObjectResult ErrorResponse(int statusCode, string error, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return StatusCode(statusCode, new
        {
            meta = new
            {
                count = 0,
                statusCode,
                durationMs = stopwatch.ElapsedMilliseconds
            },
            error
        });
    }

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
}
