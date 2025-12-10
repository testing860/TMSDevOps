using TMS.API.Data.Entities;

namespace TMS.API.Data.Entities
{
    public class TaskAssignment
    {
        public int Id { get; set; }
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
        public int AppTaskId { get; set; }
        public virtual AppTask AppTask { get; set; } = null!;
        public string UserId { get; set; } = string.Empty;
        public virtual AppUser User { get; set; } = null!;
    }
}