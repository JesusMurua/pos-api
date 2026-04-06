using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Handles CRM customer operations including store credit (fiado) and loyalty points.
/// All balance mutations create an immutable ledger entry in CustomerTransaction.
/// </summary>
public class CustomerService : ICustomerService
{
    private readonly IUnitOfWork _unitOfWork;

    public CustomerService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    public async Task<Customer> GetByIdAsync(int id)
    {
        return await _unitOfWork.Customers.GetByIdAsync(id)
            ?? throw new NotFoundException($"Customer with id {id} not found");
    }

    public async Task<IEnumerable<Customer>> GetByBusinessAsync(int businessId)
    {
        return await _unitOfWork.Customers.GetByBusinessAsync(businessId);
    }

    public async Task<IEnumerable<Customer>> SearchAsync(int businessId, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await _unitOfWork.Customers.GetByBusinessAsync(businessId);

        return await _unitOfWork.Customers.SearchAsync(businessId, query);
    }

    public async Task<Customer> CreateAsync(int businessId, Customer customer)
    {
        customer.BusinessId = businessId;

        // Validate phone uniqueness within business
        if (!string.IsNullOrEmpty(customer.Phone))
        {
            var existing = await _unitOfWork.Customers.GetByPhoneAsync(businessId, customer.Phone);
            if (existing != null)
                throw new ValidationException($"A customer with phone '{customer.Phone}' already exists.");
        }

        await _unitOfWork.Customers.AddAsync(customer);
        await _unitOfWork.SaveChangesAsync();
        return customer;
    }

    public async Task<Customer> UpdateAsync(int id, Customer updated)
    {
        var customer = await GetByIdAsync(id);

        customer.FirstName = updated.FirstName;
        customer.LastName = updated.LastName;
        customer.Phone = updated.Phone;
        customer.Email = updated.Email;
        customer.CreditLimitCents = updated.CreditLimitCents;
        customer.Notes = updated.Notes;
        customer.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Customers.Update(customer);
        await _unitOfWork.SaveChangesAsync();
        return customer;
    }

    public async Task DeactivateAsync(int id)
    {
        var customer = await GetByIdAsync(id);
        customer.IsActive = false;
        customer.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Customers.Update(customer);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Adds credit to customer balance (customer pays down their tab).
    /// AmountCents is positive — increases the balance.
    /// </summary>
    public async Task<CustomerTransaction> AddCreditAsync(
        int customerId, int amountCents, string description, int branchId, string createdBy)
    {
        if (amountCents <= 0)
            throw new ValidationException("Amount must be greater than zero.");

        var customer = await GetByIdAsync(customerId);

        customer.CreditBalanceCents += amountCents;
        customer.UpdatedAt = DateTime.UtcNow;

        var transaction = new CustomerTransaction
        {
            CustomerId = customerId,
            BranchId = branchId,
            TransactionType = CustomerTransactionType.AddCredit,
            AmountCents = amountCents,
            PointsAmount = 0,
            BalanceAfterCents = customer.CreditBalanceCents,
            PointsBalanceAfter = customer.PointsBalance,
            Description = description,
            CreatedBy = createdBy
        };

        _unitOfWork.Customers.Update(customer);
        await _unitOfWork.CustomerTransactions.AddAsync(transaction);
        await _unitOfWork.SaveChangesAsync();

        return transaction;
    }

    /// <summary>
    /// Manual credit adjustment. AmountCents can be positive or negative.
    /// </summary>
    public async Task<CustomerTransaction> AdjustCreditAsync(
        int customerId, int amountCents, string description, int branchId, string createdBy)
    {
        var customer = await GetByIdAsync(customerId);

        customer.CreditBalanceCents += amountCents;
        customer.UpdatedAt = DateTime.UtcNow;

        var transaction = new CustomerTransaction
        {
            CustomerId = customerId,
            BranchId = branchId,
            TransactionType = CustomerTransactionType.CreditAdjustment,
            AmountCents = amountCents,
            PointsAmount = 0,
            BalanceAfterCents = customer.CreditBalanceCents,
            PointsBalanceAfter = customer.PointsBalance,
            Description = description,
            CreatedBy = createdBy
        };

        _unitOfWork.Customers.Update(customer);
        await _unitOfWork.CustomerTransactions.AddAsync(transaction);
        await _unitOfWork.SaveChangesAsync();

        return transaction;
    }

    /// <summary>
    /// Manual points adjustment. PointsAmount can be positive or negative.
    /// </summary>
    public async Task<CustomerTransaction> AdjustPointsAsync(
        int customerId, int pointsAmount, string description, int branchId, string createdBy)
    {
        var customer = await GetByIdAsync(customerId);

        customer.PointsBalance += pointsAmount;
        if (customer.PointsBalance < 0) customer.PointsBalance = 0;
        customer.UpdatedAt = DateTime.UtcNow;

        var transaction = new CustomerTransaction
        {
            CustomerId = customerId,
            BranchId = branchId,
            TransactionType = CustomerTransactionType.PointsAdjustment,
            AmountCents = 0,
            PointsAmount = pointsAmount,
            BalanceAfterCents = customer.CreditBalanceCents,
            PointsBalanceAfter = customer.PointsBalance,
            Description = description,
            CreatedBy = createdBy
        };

        _unitOfWork.Customers.Update(customer);
        await _unitOfWork.CustomerTransactions.AddAsync(transaction);
        await _unitOfWork.SaveChangesAsync();

        return transaction;
    }

    public async Task<IEnumerable<CustomerTransaction>> GetTransactionsAsync(
        int customerId, DateTime? from = null, DateTime? to = null)
    {
        return await _unitOfWork.CustomerTransactions.GetByCustomerAsync(customerId, from, to);
    }

    #endregion

    #region Transactional Operations (Phase 16)

    /// <summary>
    /// Consumes store credit (fiado) for an order payment.
    /// AmountCents is positive — decreases the balance (customer owes more).
    /// </summary>
    public async Task<CustomerTransaction> UseCreditAsync(
        int customerId, int amountCents, string orderId, int branchId, string createdBy)
    {
        if (amountCents <= 0)
            throw new ValidationException("Amount must be greater than zero.");

        var customer = await GetByIdAsync(customerId);

        if (!customer.IsActive)
            throw new ValidationException("Customer is inactive.");

        // Credit limit check: if limit > 0, ensure debt won't exceed it
        var projectedBalance = customer.CreditBalanceCents - amountCents;
        if (customer.CreditLimitCents > 0 && Math.Abs(projectedBalance) > customer.CreditLimitCents)
            throw new ValidationException(
                $"INSUFFICIENT_CREDIT: Customer '{customer.FirstName}' would exceed credit limit of {customer.CreditLimitCents} cents.");

        customer.CreditBalanceCents = projectedBalance;
        customer.UpdatedAt = DateTime.UtcNow;

        var transaction = new CustomerTransaction
        {
            CustomerId = customerId,
            BranchId = branchId,
            TransactionType = CustomerTransactionType.UseCredit,
            AmountCents = -amountCents,
            PointsAmount = 0,
            BalanceAfterCents = customer.CreditBalanceCents,
            PointsBalanceAfter = customer.PointsBalance,
            ReferenceOrderId = orderId,
            Description = $"Fiado - Orden #{orderId}",
            CreatedBy = createdBy
        };

        _unitOfWork.Customers.Update(customer);
        await _unitOfWork.CustomerTransactions.AddAsync(transaction);
        await _unitOfWork.SaveChangesAsync();

        return transaction;
    }

    /// <summary>
    /// Awards loyalty points based on order total and business loyalty config.
    /// Returns null if loyalty is disabled for the business.
    /// </summary>
    public async Task<CustomerTransaction?> EarnPointsAsync(
        int customerId, int orderTotalCents, string orderId, int branchId, string createdBy)
    {
        var customer = await GetByIdAsync(customerId);

        var business = await _unitOfWork.Business.GetByIdAsync(customer.BusinessId)
            ?? throw new NotFoundException($"Business {customer.BusinessId} not found.");

        if (!business.LoyaltyEnabled || business.CurrencyUnitsPerPoint <= 0)
            return null;

        var earnedPoints = orderTotalCents / business.CurrencyUnitsPerPoint * business.PointsPerCurrencyUnit;
        if (earnedPoints <= 0) return null;

        customer.PointsBalance += earnedPoints;
        customer.UpdatedAt = DateTime.UtcNow;

        var transaction = new CustomerTransaction
        {
            CustomerId = customerId,
            BranchId = branchId,
            TransactionType = CustomerTransactionType.EarnPoints,
            AmountCents = 0,
            PointsAmount = earnedPoints,
            BalanceAfterCents = customer.CreditBalanceCents,
            PointsBalanceAfter = customer.PointsBalance,
            ReferenceOrderId = orderId,
            Description = $"Puntos ganados - Orden #{orderId}",
            CreatedBy = createdBy
        };

        _unitOfWork.Customers.Update(customer);
        await _unitOfWork.CustomerTransactions.AddAsync(transaction);
        await _unitOfWork.SaveChangesAsync();

        return transaction;
    }

    /// <summary>
    /// Redeems loyalty points as payment. Returns the transaction and the cent value.
    /// </summary>
    public async Task<(CustomerTransaction Transaction, int RedeemedValueCents)> RedeemPointsAsync(
        int customerId, int points, string orderId, int branchId, string createdBy)
    {
        if (points <= 0)
            throw new ValidationException("Points must be greater than zero.");

        var customer = await GetByIdAsync(customerId);

        var business = await _unitOfWork.Business.GetByIdAsync(customer.BusinessId)
            ?? throw new NotFoundException($"Business {customer.BusinessId} not found.");

        if (!business.LoyaltyEnabled)
            throw new ValidationException("Loyalty program is not enabled for this business.");

        if (customer.PointsBalance < points)
            throw new ValidationException(
                $"INSUFFICIENT_POINTS: Customer has {customer.PointsBalance} points, needs {points}.");

        var redeemedValueCents = points * business.PointRedemptionValueCents;

        customer.PointsBalance -= points;
        customer.UpdatedAt = DateTime.UtcNow;

        var transaction = new CustomerTransaction
        {
            CustomerId = customerId,
            BranchId = branchId,
            TransactionType = CustomerTransactionType.RedeemPoints,
            AmountCents = 0,
            PointsAmount = -points,
            BalanceAfterCents = customer.CreditBalanceCents,
            PointsBalanceAfter = customer.PointsBalance,
            ReferenceOrderId = orderId,
            Description = $"Puntos canjeados - Orden #{orderId}",
            CreatedBy = createdBy
        };

        _unitOfWork.Customers.Update(customer);
        await _unitOfWork.CustomerTransactions.AddAsync(transaction);
        await _unitOfWork.SaveChangesAsync();

        return (transaction, redeemedValueCents);
    }

    /// <summary>
    /// Recalculates denormalized balances from the ledger (reconciliation).
    /// </summary>
    public async Task RecalculateBalancesAsync(int customerId)
    {
        var customer = await GetByIdAsync(customerId);
        var transactions = (await _unitOfWork.CustomerTransactions
            .GetByCustomerAsync(customerId)).ToList();

        customer.CreditBalanceCents = transactions.Sum(t => t.AmountCents);
        customer.PointsBalance = transactions.Sum(t => t.PointsAmount);
        if (customer.PointsBalance < 0) customer.PointsBalance = 0;
        customer.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Customers.Update(customer);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Links a CRM Customer to an existing FiscalCustomer.
    /// Validates both belong to the same business.
    /// </summary>
    public async Task LinkFiscalCustomerAsync(int customerId, int fiscalCustomerId)
    {
        var customer = await GetByIdAsync(customerId);

        var fiscalCustomer = await _unitOfWork.FiscalCustomers.GetByIdAsync(fiscalCustomerId)
            ?? throw new NotFoundException($"FiscalCustomer {fiscalCustomerId} not found.");

        if (fiscalCustomer.BusinessId != customer.BusinessId)
            throw new ValidationException("Customer and FiscalCustomer must belong to the same business.");

        fiscalCustomer.CustomerId = customerId;
        fiscalCustomer.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.FiscalCustomers.Update(fiscalCustomer);
        await _unitOfWork.SaveChangesAsync();
    }

    #endregion
}
