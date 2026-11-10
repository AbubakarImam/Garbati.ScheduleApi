namespace Garbati.ScheduleApi.Exceptions;

// docs/DESIGN.md Section 1.1/2.1/4: GARBATI has no financing/loan module yet (ProductApi is
// deposit-only), so every Fineract axis that requires a loan is modeled in the schema for parity but
// rejected clearly at creation (and re-checked on PUT, for the one field - RecurrenceType - PUT lets a
// caller edit) with this exception, named exactly as docs/DESIGN.md's own "NoFinancingModuleException-
// style 422" phrasing describes. Caught by ExceptionHandlingMiddleware and mapped to 422, matching
// OrchestratorApi's own NoProductMappingException's "fails cleanly, not silently" discipline for an
// unmapped case.
public class NoFinancingModuleException(string message) : Exception(message);
