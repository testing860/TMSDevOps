using System.Collections.Generic;
using System.Threading.Tasks;
using TMS.API.Data.Entities;
using TMS.Shared.DTOs;

namespace TMS.API.Services
{
    public interface IAppTaskService
    {
        Task<List<TaskDto>> GetAllTasksAsync();
        Task<List<TaskDto>> GetMyTasksAsync();
        Task<TaskDto?> GetTaskByIdAsync(int id);
        Task<bool> CreateTaskAsync(TaskDto task);
        Task<bool> UpdateTaskAsync(TaskDto task);
        Task<bool> DeleteTaskAsync(int id);
        Task<bool> AssignToTaskAsync(int taskId);
        Task<bool> UnassignFromTaskAsync(int taskId);
        Task<bool> AssignUserToTaskAsync(int taskId, string userId);
        Task<bool> UnassignUserFromTaskAsync(int taskId, string userId);
        Task<List<AppUser>> GetAssignedUsersAsync(int taskId);
        Task<bool> IsCurrentUserAssignedAsync(int taskId);
        Task<bool> CanCurrentUserEditTaskAsync(int taskId);
    }
}