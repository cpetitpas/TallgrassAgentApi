using TallgrassAgentApi.Models;
using TallgrassAgentApi.Controllers;

namespace TallgrassAgentApi.Services;

public interface IClaudeService
{
    Task<string> AnalyzeAlarmAsync(AlarmRequest alarm);
    Task<string> AnalyzeFlowAsync(FlowRequest flow);
    Task<string> AnalyzeMultiNodeAsync(MultiNodeRequest request);
}