using Microsoft.AspNetCore.Identity;

namespace TMS.API.Data.Entities
{
    public class AppUser : IdentityUser
    {
        public string DisplayName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public virtual ICollection<TaskAssignment> TaskAssignments { get; set; } = new List<TaskAssignment>();
        public virtual ICollection<AppTask> CreatedTasks { get; set; } = new List<AppTask>();
    }
}
