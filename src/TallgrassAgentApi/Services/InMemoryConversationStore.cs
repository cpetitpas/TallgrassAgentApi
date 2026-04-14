using System.Collections.Concurrent;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, ConversationState> _store = new();

    public ConversationState GetOrCreate(string incidentId, string nodeId)
        => _store.GetOrAdd(incidentId, id => new ConversationState
        {
            IncidentId = id,
            NodeId     = nodeId
        });

    public ConversationState? Get(string incidentId)
        => _store.TryGetValue(incidentId, out var s) ? s : null;

    public void Append(string incidentId, ChatMessage message)
    {
        if (!_store.TryGetValue(incidentId, out var state))
            throw new KeyNotFoundException($"Incident {incidentId} not found.");

        lock (state.Messages)
        {
            state.Messages.Add(message);
            state.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public IReadOnlyList<ConversationState> All()
        => _store.Values.OrderByDescending(s => s.UpdatedAt).ToList();

    public bool Delete(string incidentId)
        => _store.TryRemove(incidentId, out _);
}