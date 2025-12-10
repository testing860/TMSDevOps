using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMS.API.Data.Entities;
using TMS.API.Services;
using TMS.Shared.DTOs;

namespace TMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TasksController : ControllerBase
    {
        private readonly IAppTaskService _taskService;

        public TasksController(IAppTaskService taskService)
        {
            _taskService = taskService;
        }

        // GET: api/tasks
        [HttpGet]
        public async Task<ActionResult<List<TaskDto>>> GetAll()
        {
            var tasks = await _taskService.GetAllTasksAsync();
            return Ok(tasks);
        }

        // GET: api/tasks/me
        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<List<TaskDto>>> GetMyTasks()
        {
            var tasks = await _taskService.GetMyTasksAsync();
            return Ok(tasks);
        }

        // GET: api/tasks/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<TaskDto>> GetTask(int id)
        {
            var task = await _taskService.GetTaskByIdAsync(id);
            if (task == null) return NotFound();
            return Ok(task);
        }

        // POST: api/tasks
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateTask([FromBody] TaskDto task)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var created = await _taskService.CreateTaskAsync(task);
            if (!created) return BadRequest("Failed to create task.");

            if (task.Id > 0)
                return CreatedAtAction(nameof(GetTask), new { id = task.Id }, task);

            return NoContent();
        }

        // PUT: api/tasks/{id}
        [HttpPut("{id:int}")]
        [Authorize]
        public async Task<IActionResult> UpdateTask(int id, [FromBody] TaskDto task)
        {
            if (id != task.Id) return BadRequest("Id mismatch.");
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var updated = await _taskService.UpdateTaskAsync(task);
            if (!updated) return Forbid();

            return NoContent();
        }

        // DELETE: api/tasks/{id}
        [HttpDelete("{id:int}")]
         [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteTask(int id)
        {
            var deleted = await _taskService.DeleteTaskAsync(id);
            if (!deleted) return NotFound();
            return NoContent();
        }

        // ============================================
        // ADMIN ASSIGNMENT ENDPOINTS (WITH USER ID)
        // ============================================

        // POST: api/tasks/{id}/assign/{userId}
        [HttpPost("{id:int}/assign/{userId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AssignUserToTask(int id, string userId)
        {
            var assigned = await _taskService.AssignUserToTaskAsync(id, userId);
            if (!assigned) return BadRequest("Failed to assign user to task.");
            return NoContent();
        }

        // POST: api/tasks/{id}/unassign/{userId}
        [HttpPost("{id:int}/unassign/{userId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UnassignUserFromTask(int id, string userId)
        {
            var ok = await _taskService.UnassignUserFromTaskAsync(id, userId);
            if (!ok) return BadRequest("Failed to unassign user from task.");
            return NoContent();
        }

        // ============================================
        // REGULAR ASSIGNMENT ENDPOINTS (SELF-ASSIGN)
        // ============================================

        // POST: api/tasks/{id}/assign
        [HttpPost("{id:int}/assign")]
        [Authorize]
        public async Task<IActionResult> AssignToTask(int id)
        {
            var assigned = await _taskService.AssignToTaskAsync(id);
            if (!assigned) return BadRequest("Failed to assign to task.");
            return NoContent();
        }

        // POST: api/tasks/{id}/unassign
        [HttpPost("{id:int}/unassign")]
        [Authorize]
        public async Task<IActionResult> UnassignFromTask(int id)
        {
            var ok = await _taskService.UnassignFromTaskAsync(id);
            if (!ok) return BadRequest("Failed to unassign from task.");
            return NoContent();
        }

        // GET: api/tasks/{id}/assigned-users
        [HttpGet("{id:int}/assigned-users")]
        [Authorize]
        public async Task<ActionResult<List<AppUser>>> GetAssignedUsers(int id)
        {
            var users = await _taskService.GetAssignedUsersAsync(id);
            return Ok(users);
        }

        // GET: api/tasks/{id}/can-edit
        [HttpGet("{id:int}/can-edit")]
        [Authorize]
        public async Task<ActionResult<bool>> CanCurrentUserEdit(int id)
        {
            var canEdit = await _taskService.CanCurrentUserEditTaskAsync(id);
            return Ok(canEdit);
        }
    }
}