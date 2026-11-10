using System.Net.Http.Json;
using System.Text.Json;

namespace Garbati.ScheduleApi.Tests.Controllers;

// Small shared helpers for the HTTP-level controller tests below - building a valid create body (with a
// guaranteed-unique Name, since docs/DESIGN.md Section 2.1 requires one) and reading a JSON response
// without each test re-deriving the same boilerplate.
internal static class TestRequests
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public static object CreateRequest(
        string? idempotencyKey = null, string? name = null, string? customerId = null,
        string? fromAccountId = null, string? toAccountId = null,
        string fromAccountType = "Savings", string toAccountType = "Savings",
        string transferType = "AccountTransfer", string instructionType = "Fixed",
        string priority = "Medium", string recurrenceType = "Periodic", string recurrenceFrequency = "Months",
        decimal amount = 500m, int recurrenceInterval = 1, int? recurrenceOnDay = 1,
        bool allowPartialTransfer = false) => new
    {
        idempotencyKey = idempotencyKey ?? $"idem-{Guid.NewGuid():N}",
        name = name ?? $"test-{Guid.NewGuid():N}",
        customerId = customerId ?? $"CUS-{Guid.NewGuid():N}",
        fromAccountType,
        fromAccountId = fromAccountId ?? $"ACC-{Guid.NewGuid():N}"[..12],
        toAccountType,
        toAccountId = toAccountId ?? $"ACC-{Guid.NewGuid():N}"[..12],
        transferType,
        instructionType,
        priority,
        amount,
        currency = "NGN",
        narration = (string?)null,
        validFrom = "2026-01-01",
        validTill = (string?)null,
        recurrenceType,
        recurrenceFrequency,
        recurrenceInterval,
        recurrenceOnDay,
        allowPartialTransfer,
    };

    public static async Task<JsonElement> CreateAsync(HttpClient client, object? request = null)
    {
        using var response = await client.PostAsJsonAsync("/v1/standing-instructions", request ?? CreateRequest(), JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
