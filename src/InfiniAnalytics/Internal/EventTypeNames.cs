namespace InfiniAnalytics.Internal;

internal static class EventTypeNames
{
    public static bool TryToWire(EventType eventType, out string name)
    {
        switch (eventType)
        {
            case EventType.Start: name = "START"; return true;
            case EventType.Event: name = "EVENT"; return true;
            case EventType.Warning: name = "WARNING"; return true;
            case EventType.Error: name = "ERROR"; return true;
            case EventType.End: name = "END"; return true;
            default: name = ""; return false;
        }
    }

    public static EventType? FromWire(string name)
    {
        switch (name)
        {
            case "START": return EventType.Start;
            case "EVENT": return EventType.Event;
            case "WARNING": return EventType.Warning;
            case "ERROR": return EventType.Error;
            case "END": return EventType.End;
            default: return null;
        }
    }
}
