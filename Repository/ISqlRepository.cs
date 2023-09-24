using System.Threading.Tasks;

namespace core6.Repository
{
    public interface ISqlRepository
    {
        Task Persist(string message);
    }
}