using System.ComponentModel.DataAnnotations;
using TMS.API.Data.Entities;

namespace TMS.API.Data.Entities
{
    public class AppTask
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public AppTaskStatus Status { get; set; } = AppTaskStatus.NotStarted;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DueDate { get; set; }
        public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;

        [Range(0, 100)]
        public int Progress { get; set; } = 0;

        public string CreatedById { get; set; } = string.Empty;
        public virtual AppUser? CreatedBy { get; set; }

        // Assignments
        public virtual ICollection<TaskAssignment> Assignments { get; set; } = new List<TaskAssignment>();
    }

    public enum AppTaskStatus
    {
        NotStarted,
        InProgress,
        UnderReview,
        Completed,
        Archived
    }

    public enum PriorityLevel
    {
        Low,
        Medium,
        High,
        Critical
    } 
}