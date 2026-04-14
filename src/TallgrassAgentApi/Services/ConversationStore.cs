using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public interface IConversationStore
{
    ConversationState GetOrCreate(string incidentId, string nodeId);
    ConversationState? Get(string incidentId);
    void Append(string incidentId, ChatMessage message);
    IReadOnlyList<ConversationState> All();
    bool Delete(string incidentId);
}