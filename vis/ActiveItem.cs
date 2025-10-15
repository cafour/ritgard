using System;
using System.Text;
using BTree;
using Godot;
using Ritgard.Mining;

namespace Ritgard;

public record ActiveItem(
    string Id,
    IConversation Conversation,
    BPlusTree<DateTimeOffset, ActiveItemEvent> Events,
    Vector2 Position
)
{
    public static ActiveItem FromConversation(IConversation conversation, Vector2 position)
    {
        var events = new BPlusTree<DateTimeOffset, ActiveItemEvent>();
        events.InsertOrUpdate(conversation.CreatedAt, new ActiveItemEvent(conversation.CreatedAt, null));
        // foreach (var @event in item.Events)
        // {
        //     events.InsertOrUpdate(@event.CreatedAt, new ActiveItemEvent(@event.CreatedAt, @event));
        // }
        foreach (var comment in conversation.Comments)
        {
            events.InsertOrUpdate(comment.CreatedAt, new ActiveItemEvent(comment.CreatedAt, comment));
        }

        return new ActiveItem(
            Id: conversation.Id,
            Conversation: conversation,
            Events: events,
            Position: position
        );
    }

    public override string ToString()
    {
        return Conversation.ToString();
    }
}
