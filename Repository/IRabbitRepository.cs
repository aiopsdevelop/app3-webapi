using core6.Events;

namespace core6.Repository
{
    public interface IRabbitRepository
    {
        void Publish(IEvent evt);
    }
}
