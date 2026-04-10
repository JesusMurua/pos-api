namespace POS.Domain.Exceptions;

/// <summary>
/// Thrown when a business on a limited plan exceeds a quantitative limit.
/// Controllers should map this to HTTP 402 Payment Required.
/// </summary>
public class PlanLimitExceededException : Exception
{
    public string CurrentPlan { get; }
    public string Resource { get; }
    public int Limit { get; }

    public PlanLimitExceededException(string resource, int limit, string currentPlan)
        : base($"El plan {currentPlan} permite un maximo de {limit} {resource}. Actualiza tu plan para continuar.")
    {
        Resource = resource;
        Limit = limit;
        CurrentPlan = currentPlan;
    }
}
