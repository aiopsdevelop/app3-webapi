namespace core6.Events
{
    public class MessagePersistedEvent : IEvent
    {
        public string Message { get; set; }
    }

    public interface IEvent
    {

    }
}
