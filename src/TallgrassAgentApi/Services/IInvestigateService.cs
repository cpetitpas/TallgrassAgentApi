using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public interface IInvestigateService
{
    Task<InvestigateResponse> InvestigateAsync(
        InvestigateRequest request,
        CancellationToken cancellationToken = default);
}