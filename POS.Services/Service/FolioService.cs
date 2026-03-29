using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class FolioService : IFolioService
{
    private readonly IUnitOfWork _unitOfWork;

    public FolioService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Atomically increments the branch folio counter and returns the formatted folio.
    /// </summary>
    public async Task<string> GenerateAsync(int branchId)
    {
        var (counter, prefix, format) = await _unitOfWork.Branches.IncrementFolioCounterAsync(branchId);
        return FormatFolio(prefix, format, counter);
    }

    #region Private Helper Methods

    private static string FormatFolio(string? prefix, string? format, int counter)
    {
        if (!string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(format))
        {
            var result = format.Replace("{PREFIX}", prefix);

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
