using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace Web.Controllers;

[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("memberimporter")]
[ApiExplorerSettings(GroupName = "Member Importer API")]
public class MemberImportController : ManagementApiControllerBase
{
    private readonly IMemberManager _memberManager;
    private readonly IMemberService _memberService;
    private readonly IMemberTypeService _memberTypeService;

    public MemberImportController(
        IMemberManager memberManager,
        IMemberService memberService,
        IMemberTypeService memberTypeService)
    {
        _memberManager = memberManager;
        _memberService = memberService;
        _memberTypeService = memberTypeService;
    }

    [HttpPost("importcsv")]
    public async Task<IActionResult> ImportCsv(IFormFile file, [FromForm] bool deleteAllMembersBeforeImport = false)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { success = false, message = "No file uploaded" });
        }

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { success = false, message = "Only CSV files are allowed" });
        }

        var results = new ImportResult
        {
            TotalRows = 0,
            SuccessCount = 0,
            SkippedCount = 0,
            ErrorCount = 0,
            DeletedCount = 0,
            Errors = new List<string>()
        };

        try
        {
            // Delete all members before importing if requested
            if (deleteAllMembersBeforeImport)
            {
                var allMembers = _memberService.GetAllMembers();
                foreach (var member in allMembers)
                {
                    _memberService.Delete(member);
                    results.DeletedCount++;
                }
            }

            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);

            // Read header
            var header = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(header))
            {
                return BadRequest(new { success = false, message = "CSV file is empty" });
            }

            var headers = ParseCsvLine(header);
            var columnMap = MapColumns(headers);

            if (!ValidateRequiredColumns(columnMap, out var validationError))
            {
                return BadRequest(new { success = false, message = validationError });
            }

            // Process each row
            int lineNumber = 1;
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNumber++;
                results.TotalRows++;

                if (string.IsNullOrWhiteSpace(line))
                {
                    results.SkippedCount++;
                    continue;
                }

                var values = ParseCsvLine(line);
                if (values.Length != headers.Length)
                {
                    results.ErrorCount++;
                    results.Errors.Add($"Line {lineNumber}: Column count mismatch");
                    continue;
                }

                try
                {
                    var memberData = ExtractMemberData(values, columnMap);

                    if (string.IsNullOrWhiteSpace(memberData.Email))
                    {
                        results.SkippedCount++;
                        results.Errors.Add($"Line {lineNumber}: Missing email address");
                        continue;
                    }

                    var importOutcome = await CreateOrUpdateMember(memberData);
                    if (importOutcome == MemberImportOutcome.Skipped)
                    {
                        results.SkippedCount++;
                        results.Errors.Add($"Line {lineNumber}: Member with email '{memberData.Email}' already exists (skipped)");
                    }
                    else
                    {
                        results.SuccessCount++;
                    }
                }
                catch (Exception ex)
                {
                    results.ErrorCount++;
                    results.Errors.Add($"Line {lineNumber}: {ex.Message}");
                }
            }

            var message = deleteAllMembersBeforeImport
                ? $"Import completed: {results.DeletedCount} members deleted, {results.SuccessCount} successful, {results.SkippedCount} skipped, {results.ErrorCount} errors"
                : $"Import completed: {results.SuccessCount} successful, {results.SkippedCount} skipped, {results.ErrorCount} errors";

            return Ok(new
            {
                success = true,
                message,
                results
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = $"Import failed: {ex.Message}" });
        }
    }

    private async Task<MemberImportOutcome> CreateOrUpdateMember(MemberData data)
    {
        if (string.IsNullOrWhiteSpace(data.Email))
        {
            throw new ArgumentException("Email cannot be empty");
        }

        // Check if member exists by email
        var existingMemberIdentity = await _memberManager.FindByEmailAsync(data.Email);

        if (existingMemberIdentity != null)
        {
            // Skip existing members
            return MemberImportOutcome.Skipped;
        }
        else
        {
            // Create new member using IMemberManager
            var randomPassword = Guid.NewGuid().ToString("N") + "Aa1!"; // Ensure password meets requirements
            var memberName = $"{data.FirstName} {data.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(memberName))
            {
                memberName = data.Email;
            }

            var identityUser = MemberIdentityUser.CreateNew(
                data.Email,
                data.Email,
                "bbvMember",
                true,
                memberName);

            var createResult = await _memberManager.CreateAsync(identityUser, randomPassword);

            if (!createResult.Succeeded)
            {
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                throw new Exception($"Failed to create member: {errors}");
            }

            // Now update the member properties
            var member = _memberService.GetByEmail(data.Email);
            if (member != null)
            {
                member.SetValue("firstName", data.FirstName ?? "");
                member.SetValue("lastName", data.LastName ?? "");
                member.SetValue("phone", data.Phone ?? "");
                member.SetValue("tidligereArbejdssteder", data.TidligereArbejdssteder ?? "");

                if (data.Birthdate.HasValue)
                {
                    member.SetValue("birthdate", data.Birthdate.Value);
                }

                _memberService.Save(member);
            }
            return MemberImportOutcome.Created;
        }
    }

    private MemberData ExtractMemberData(string[] values, Dictionary<string, int> columnMap)
    {
        var data = new MemberData();

        if (columnMap.TryGetValue("Fornavn", out int firstNameIdx))
            data.FirstName = values[firstNameIdx]?.Trim();

        if (columnMap.TryGetValue("Efternavn", out int lastNameIdx))
            data.LastName = values[lastNameIdx]?.Trim();

        if (columnMap.TryGetValue("Email", out int emailIdx))
            data.Email = values[emailIdx]?.Trim();

        if (columnMap.TryGetValue("Telefon", out int phoneIdx))
            data.Phone = values[phoneIdx]?.Trim();

        if (columnMap.TryGetValue("Arbejdssteder", out int arbejdsIdx))
            data.TidligereArbejdssteder = values[arbejdsIdx]?.Trim();

        // Birthdate is not in the CSV, so it will remain null

        return data;
    }

    private Dictionary<string, int> MapColumns(string[] headers)
    {
        var map = new Dictionary<string, int>();
        for (int i = 0; i < headers.Length; i++)
        {
            map[headers[i]] = i;
        }
        return map;
    }

    private bool ValidateRequiredColumns(Dictionary<string, int> columnMap, out string? error)
    {
        var requiredColumns = new[] { "Email" };
        var missing = requiredColumns.Where(col => !columnMap.ContainsKey(col)).ToList();

        if (missing.Any())
        {
            error = $"Missing required columns: {string.Join(", ", missing)}";
            return false;
        }

        error = null;
        return true;
    }

    private string[] ParseCsvLine(string line)
    {
        var values = new List<string>();
        var currentValue = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(currentValue.ToString());
                currentValue.Clear();
            }
            else
            {
                currentValue.Append(c);
            }
        }

        values.Add(currentValue.ToString());
        return values.ToArray();
    }

    private class MemberData
    {
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public string? TidligereArbejdssteder { get; set; }
        public DateTime? Birthdate { get; set; }
    }

    private class ImportResult
    {
        public int TotalRows { get; set; }
        public int SuccessCount { get; set; }
        public int SkippedCount { get; set; }
        public int ErrorCount { get; set; }
        public int DeletedCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    private enum MemberImportOutcome
    {
        Created,
        Skipped
    }
}
