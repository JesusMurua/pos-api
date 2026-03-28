using Microsoft.EntityFrameworkCore;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class FolioService : IFolioService
{
    private readonly ApplicationDbContext _context;

    public FolioService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Atomically increments the branch folio counter and returns the formatted folio.
    /// Uses PostgreSQL UPDATE ... RETURNING for atomicity.
    /// </summary>
    public async Task<string> GenerateAsync(int branchId)
    {
        var result = await _context.Database
            .SqlQuery<int>($@"
                UPDATE ""Branches""
                SET ""FolioCounter"" = ""FolioCounter"" + 1
                WHERE ""Id"" = {branchId}
                RETURNING ""FolioCounter""")
            .ToListAsync();

        var counter = result.FirstOrDefault();
        if (counter == 0)
            throw new InvalidOperationException($"Branch {branchId} not found");

        var branch = await _context.Branches
            .AsNoTracking()
            .FirstAsync(b => b.Id == branchId);

        return FormatFolio(branch.FolioPrefix, branch.FolioFormat, counter);
    }

    #region Private Helper Methods

    private static string FormatFolio(string? prefix, string? format, int counter)
    {
        if (!string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(format))
        {
            var result = format
                .Replace("{PREFIX}", prefix);

            // Handle {NUM:N} pattern where N is digit count
            var numStart = result.IndexOf("{NUM:");
            if (numStart >= 0)
            {
                var numEnd = result.IndexOf('}', numStart);
                var digitStr = result[(numStart + 5)..numEnd];
                if (int.TryParse(digitStr, out var digits))
                {
                    result = result[..numStart] + counter.ToString($"D{digits}") + result[(numEnd + 1)..];
                    return result;
                }
            }

            return $"{prefix}-{counter:D4}";
        }

        if (!string.IsNullOrEmpty(prefix))
            return $"{prefix}-{counter:D4}";

        return counter.ToString("D4");
    }

    #endregion
}
