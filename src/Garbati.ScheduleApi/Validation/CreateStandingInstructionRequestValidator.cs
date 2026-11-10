using Garbati.ScheduleApi.Contracts.Requests;
using Garbati.ScheduleApi.Entities;

namespace Garbati.ScheduleApi.Validation;

// Shape-only checks (400, via the controller's BadRequest(errors) - same split every sibling service
// uses). The v1 financing-scope rejections (Loan account type, non-AccountTransfer, non-Fixed,
// non-Periodic) are a separate, deliberate 422 via NoFinancingModuleException, thrown from
// StandingInstructionService - docs/DESIGN.md Section 4 calls those out as their own clear reason, not a
// generic "bad request."
public class CreateStandingInstructionRequestValidator
{
    public IReadOnlyList<string> Validate(CreateStandingInstructionRequest request)
    {
        var errors = new List<string>();

        if (IdempotencyKeyRule.Validate(request.IdempotencyKey) is { } idempotencyError)
        {
            errors.Add(idempotencyError);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add("Name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            errors.Add("CustomerId is required.");
        }

        if (EnumParser.Parse<AccountType>(request.FromAccountType) is null)
        {
            errors.Add("FromAccountType must be Savings or Loan.");
        }

        if (string.IsNullOrWhiteSpace(request.FromAccountId))
        {
            errors.Add("FromAccountId is required.");
        }

        if (EnumParser.Parse<AccountType>(request.ToAccountType) is null)
        {
            errors.Add("ToAccountType must be Savings or Loan.");
        }

        if (string.IsNullOrWhiteSpace(request.ToAccountId))
        {
            errors.Add("ToAccountId is required.");
        }

        if (!string.IsNullOrWhiteSpace(request.FromAccountId) && request.FromAccountId == request.ToAccountId)
        {
            errors.Add("FromAccountId and ToAccountId must be different.");
        }

        if (EnumParser.Parse<TransferType>(request.TransferType) is null)
        {
            errors.Add("TransferType must be AccountTransfer, LoanRepayment, ChargePayment, InterestTransfer or LoanDownPayment.");
        }

        if (EnumParser.Parse<InstructionType>(request.InstructionType) is null)
        {
            errors.Add("InstructionType must be Fixed or Dues.");
        }

        if (EnumParser.Parse<Priority>(request.Priority) is null)
        {
            errors.Add("Priority must be Urgent, High, Medium or Low.");
        }

        if (string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Length != 3)
        {
            errors.Add("Currency must be a 3-letter currency code.");
        }

        ScheduleFieldsValidator.Validate(
            request.Amount, request.ValidFrom, request.ValidTill,
            request.RecurrenceType, request.RecurrenceFrequency, request.RecurrenceInterval, request.RecurrenceOnDay,
            errors);

        return errors;
    }
}
