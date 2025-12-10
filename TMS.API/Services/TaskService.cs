using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMS.API.Data;
using TMS.API.Data.Entities;
using TMS.Shared.DTOs;

namespace TMS.API.Services
{
    public class AppTaskService : IAppTaskService
    {
        private readonly TMSDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly UserManager<AppUser> _userManager;

        public AppTaskService(TMSDbContext context, IHttpContextAccessor httpContextAccessor, UserManager<AppUser> userManager)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _userManager = userManager;
        }

        private string? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }

        private async Task<bool> IsCurrentUserAdminAsync()
        {
            var user = await _userManager.GetUserAsync(_httpContextAccessor.HttpContext?.User);
            return user != null && await _userManager.IsInRoleAsync(user, "Admin");
        }

        public async Task<List<TaskDto>> GetAllTasksAsync()
        {
            var currentUserId = GetCurrentUserId();
            var tasks = await _context.Tasks
                .Include(t => t.CreatedBy)
                .Include(t => t.Assignments)
                    .ThenInclude(a => a.User)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            var isAdmin = await IsCurrentUserAdminAsync();
            return tasks.Select(t => MapToDto(t, currentUserId, isAdmin)).ToList();
        }

        public async Task<List<TaskDto>> GetMyTasksAsync()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return new List<TaskDto>();

            var tasks = await _context.Tasks
                .Include(t => t.CreatedBy)
                .Include(t => t.Assignments)
                    .ThenInclude(a => a.User)
                .Where(t => t.Assignments.Any(a => a.UserId == userId) || t.CreatedById == userId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            var isAdmin = await IsCurrentUserAdminAsync();
            return tasks.Select(t => MapToDto(t, userId, isAdmin)).ToList();
        }

        public async Task<TaskDto?> GetTaskByIdAsync(int id)
        {
            var currentUserId = GetCurrentUserId();
            var task = await _context.Tasks
                .Include(t => t.CreatedBy)
                .Include(t => t.Assignments)
                    .ThenInclude(a => a.User)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (task == null) return null;

            var isAdmin = await IsCurrentUserAdminAsync();
            return MapToDto(task, currentUserId, isAdmin);
        }

        public async Task<bool> CreateTaskAsync(TaskDto taskDto)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return false;

            try
            {
                var task = new AppTask
                {
                    Title = taskDto.Title?.Trim() ?? string.Empty,
                    Description = taskDto.Description?.Trim() ?? string.Empty,
                    DueDate = taskDto.DueDate,
                    Priority = (PriorityLevel)taskDto.Priority,
                    Progress = taskDto.Progress,

                    CreatedById = userId,
                    CreatedAt = DateTime.UtcNow,
                    Status = AppTaskStatus.NotStarted
                };

                _context.Tasks.Add(task);
                var result = await _context.SaveChangesAsync() > 0;

                if (result)
                {
                    taskDto.Id = task.Id;
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating task: {ex.Message}");
                return false;
            }
        }


        public async Task<bool> UpdateTaskAsync(TaskDto taskDto)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return false;

            var existingTask = await _context.Tasks
                .Include(t => t.CreatedBy)
                .Include(t => t.Assignments)
                    .ThenInclude(ta => ta.User)
                .FirstOrDefaultAsync(t => t.Id == taskDto.Id);

            if (existingTask == null) return false;

            bool isCreator = existingTask.CreatedById == userId;
            bool isAdmin = await IsCurrentUserAdminAsync();

            // Check if User is Assigned
            bool isAssigned = existingTask.Assignments.Any(ta => ta.UserId == userId);

            // Allow updates if user is Creator, Admin or Asignee
            if (!isCreator && !isAdmin && !isAssigned) return false;

            // Admin & Normal User can edit Title, Description & Progress
            existingTask.Title = taskDto.Title?.Trim() ?? string.Empty;
            existingTask.Description = taskDto.Description?.Trim() ?? string.Empty;
            existingTask.Progress = taskDto.Progress;

            // Only Admins can change status
            if (isAdmin)
            {
                existingTask.Status = (AppTaskStatus)taskDto.Status;
            }
            else
            {
                taskDto.Status = (int)existingTask.Status;
            }

            // Only admins can change due date and priority
            if (isAdmin)
            {
                existingTask.DueDate = taskDto.DueDate;
                existingTask.Priority = (PriorityLevel)taskDto.Priority;
            }

            _context.Tasks.Update(existingTask);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeleteTaskAsync(int id)
        {
            if (!await IsCurrentUserAdminAsync()) return false;

            var task = await _context.Tasks.FindAsync(id);
            if (task == null) return false;

            _context.Tasks.Remove(task);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<bool> AssignToTaskAsync(int taskId)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return false;

            var existingAssignment = await _context.TaskAssignments
                .FirstOrDefaultAsync(ta => ta.AppTaskId == taskId && ta.UserId == userId);
            if (existingAssignment != null) return true;

            var assignment = new TaskAssignment
            {
                AppTaskId = taskId,
                UserId = userId,
                AssignedAt = DateTime.UtcNow
            };

            _context.TaskAssignments.Add(assignment);

            var task = await _context.Tasks.FindAsync(taskId);
            if (task != null && task.Status == AppTaskStatus.NotStarted)
            {
                task.Status = AppTaskStatus.InProgress;
                _context.Tasks.Update(task);
            }

            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<bool> UnassignFromTaskAsync(int taskId)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return false;

            var assignment = await _context.TaskAssignments
                .FirstOrDefaultAsync(ta => ta.AppTaskId == taskId && ta.UserId == userId);

            if (assignment == null) return true;

            _context.TaskAssignments.Remove(assignment);

            var remainingAssignments = await _context.TaskAssignments
                .Where(ta => ta.AppTaskId == taskId)
                .CountAsync();

            if (remainingAssignments == 0)
            {
                var task = await _context.Tasks.FindAsync(taskId);
                if (task != null)
                {
                    task.Status = AppTaskStatus.NotStarted;
                    _context.Tasks.Update(task);
                }
            }

            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<List<AppUser>> GetAssignedUsersAsync(int taskId)
        {
            return await _context.TaskAssignments
                .Where(ta => ta.AppTaskId == taskId)
                .Include(ta => ta.User)
                .Select(ta => ta.User)
                .ToListAsync();
        }

        public async Task<bool> IsCurrentUserAssignedAsync(int taskId)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return false;

            return await _context.TaskAssignments
                .AnyAsync(ta => ta.AppTaskId == taskId && ta.UserId == userId);
        }

        public async Task<bool> CanCurrentUserEditTaskAsync(int taskId)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return false;

            var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);
            if (task == null) return false;

            bool isCreator = task.CreatedById == userId;
            bool isAdmin = await IsCurrentUserAdminAsync();

            return isCreator || isAdmin;
        }

        private TaskDto MapToDto(AppTask task, string? currentUserId, bool isAdmin)
        {
            return new TaskDto
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                Status = (int)task.Status,
                Priority = (int)task.Priority,
                Progress = task.Progress,
                DueDate = task.DueDate,
                CreatedAt = task.CreatedAt,
                CreatedById = task.CreatedById,
                CreatedByDisplayName = task.CreatedBy?.DisplayName ?? "Unknown",
                IsAssignedToCurrentUser = !string.IsNullOrEmpty(currentUserId) &&
                                         task.Assignments.Any(a => a.UserId == currentUserId),
                CanEdit = !string.IsNullOrEmpty(currentUserId) &&
                         (task.CreatedById == currentUserId || isAdmin),
                AssignedUsers = task.Assignments.Select(a => new UserDto
                {
                    Id = a.User.Id,
                    UserName = a.User.UserName,
                    DisplayName = a.User.DisplayName,
                    Email = a.User.Email
                }).ToList()
            };
        }

        public async Task<bool> AssignUserToTaskAsync(int taskId, string userId)
        {
            if (!await IsCurrentUserAdminAsync()) return false;

            var existingAssignment = await _context.TaskAssignments
                .FirstOrDefaultAsync(ta => ta.AppTaskId == taskId && ta.UserId == userId);
            if (existingAssignment != null) return true;

            var assignment = new TaskAssignment
            {
                AppTaskId = taskId,
                UserId = userId,
                AssignedAt = DateTime.UtcNow
            };

            _context.TaskAssignments.Add(assignment);

            var task = await _context.Tasks.FindAsync(taskId);
            if (task != null && task.Status == AppTaskStatus.NotStarted)
            {
                task.Status = AppTaskStatus.InProgress;
                _context.Tasks.Update(task);
            }

            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<bool> UnassignUserFromTaskAsync(int taskId, string userId)
        {
            if (!await IsCurrentUserAdminAsync()) return false;

            var assignment = await _context.TaskAssignments
                .FirstOrDefaultAsync(ta => ta.AppTaskId == taskId && ta.UserId == userId);

            if (assignment == null) return true;

            _context.TaskAssignments.Remove(assignment);

            var remainingAssignments = await _context.TaskAssignments
                .Where(ta => ta.AppTaskId == taskId)
                .CountAsync();

            if (remainingAssignments == 0)
            {
                var task = await _context.Tasks.FindAsync(taskId);
                if (task != null)
                {
                    task.Status = AppTaskStatus.NotStarted;
                    _context.Tasks.Update(task);
                }
            }

            return await _context.SaveChangesAsync() > 0;
        }


    }
}