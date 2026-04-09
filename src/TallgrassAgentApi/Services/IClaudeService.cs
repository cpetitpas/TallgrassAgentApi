using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public interface IClaudeService
{
    Task<string> AnalyzeAlarmAsync(AlarmRequest alarm, CancellationToken ct = default);
    Task<string> AnalyzeFlowAsync(FlowRequest flow, CancellationToken ct = default);
    Task<string> AnalyzeMultiNodeAsync(MultiNodeRequest request, CancellationToken ct = default);
}