using System;
using BTree;
using Godot;
using Ritgard.Mining;

namespace Ritgard;

public record ActiveItem(
    string Id,
    object OriginalItem,
    BPlusTree<DateTimeOffset, ActiveItemEvent> Events,
    Vector2 Position
)
{
    public static ActiveItem Create(Issue item, Vector2 position)
    {
        var events = new BPlusTree<DateTimeOffset, ActiveItemEvent>();
        events.InsertOrUpdate(item.CreatedAt, new ActiveItemEvent(item.CreatedAt, null));
        foreach (var @event in item.Events)
        {
            events.InsertOrUpdate(@event.CreatedAt, new ActiveItemEvent(@event.CreatedAt, @event));
        }
        return new ActiveItem(
            Id: item.Id,
            OriginalItem: item,
            Events: events,
            Position: position
        );
    }
}
