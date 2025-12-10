using System.Threading.Tasks;
using TMS.API.Data.Entities;

namespace TMS.API.Services
{
    public interface ITokenService
    {
        Task<string> GenerateTokenAsync(AppUser user);
    }
}
