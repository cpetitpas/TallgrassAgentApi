using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public interface IMultiNodeInvestigateService
{
    Task<MultiNodeInvestigateResponse> InvestigateAsync(
        MultiNodeInvestigateRequest request,
        CancellationToken cancellationToken = default);
}