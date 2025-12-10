using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using TMS.API.Data.Entities;

namespace TMS.API.Services
{
    public interface IUserService
    {
        Task<AppUser?> GetCurrentUserAsync(ClaimsPrincipal user);
        Task<string?> GetUserDisplayNameAsync(ClaimsPrincipal user);
        Task<bool> IsUserInRoleAsync(AppUser user, string role);
        Task<List<AppUser>> GetAllUsersAsync();
    }
}
