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
        => _store.TryGetValue(incidentId, out var state) ? Snapshot(state) : null;

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
        => _store.Values
            .Select(Snapshot)
            .OrderByDescending(s => s.UpdatedAt)
            .ToList();

    public bool Delete(string incidentId)
        => _store.TryRemove(incidentId, out _);

    private static ConversationState Snapshot(ConversationState state)
    {
        List<ChatMessage> messages;
        lock (state.Messages)
        {
            messages = state.Messages
                .Select(m => new ChatMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    Timestamp = m.Timestamp
                })
                .ToList();
        }

        return new ConversationState
        {
            IncidentId = state.IncidentId,
            NodeId = state.NodeId,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt,
            Messages = messages
        };
    }
}