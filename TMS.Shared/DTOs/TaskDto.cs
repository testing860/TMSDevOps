using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TMS.Shared.DTOs
{
    public class TaskDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Title is required!")]
        [StringLength(200, ErrorMessage = "Title cannot exceed 200 characters!")]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters!")]
        public string Description { get; set; } = string.Empty;

        public int Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? DueDate { get; set; }
        public int Priority { get; set; }
        public int Progress { get; set; }

        public string CreatedById { get; set; } = string.Empty;
        public string CreatedByDisplayName { get; set; } = string.Empty;
        public bool IsAssignedToCurrentUser { get; set; }
        public bool CanEdit { get; set; } 

        public List<UserDto> AssignedUsers { get; set; } = new List<UserDto>();
    }
}