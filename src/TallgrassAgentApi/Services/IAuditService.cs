using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public interface IAuditService
{
    void Record(AuditEntry entry);
    IReadOnlyList<AuditEntry> GetRecent(int count = 100);
    AuditSummary GetSummary();
}