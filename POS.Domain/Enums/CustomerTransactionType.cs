namespace POS.Domain.Enums;

/// <summary>
/// Types of customer ledger transactions for credit and loyalty points.
/// </summary>
public enum CustomerTransactionType
{
    /// <summary>Loyalty points earned from a purchase.</summary>
    EarnPoints,
    /// <summary>Loyalty points redeemed as payment.</summary>
    RedeemPoints,
    /// <summary>Credit added (customer pays down their tab / fiado).</summary>
    AddCredit,
    /// <summary>Credit used (customer buys on credit / fiado).</summary>
    UseCredit,
    /// <summary>Manual credit adjustment by owner/manager.</summary>
    CreditAdjustment,
    /// <summary>Manual points adjustment by owner/manager.</summary>
    PointsAdjustment
}
