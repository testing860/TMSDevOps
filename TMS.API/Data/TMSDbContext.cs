using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TMS.API.Data.Entities;

namespace TMS.API.Data
{
    public class TMSDbContext : IdentityDbContext<AppUser>
    {
        public TMSDbContext(DbContextOptions<TMSDbContext> options)
            : base(options)
        {
        }

        public DbSet<AppTask> Tasks => Set<AppTask>();
        public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<AppTask>(entity =>
            {
                entity.HasKey(t => t.Id);
                entity.Property(t => t.Title).IsRequired().HasMaxLength(200);
                entity.Property(t => t.Description).HasMaxLength(1000);

                entity.Property(t => t.Progress).IsRequired().HasDefaultValue(0);

                entity.HasOne(t => t.CreatedBy)
                      .WithMany(u => u.CreatedTasks)
                      .HasForeignKey(t => t.CreatedById)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<TaskAssignment>(entity =>
            {
                entity.HasKey(ta => ta.Id);

                entity.HasIndex(ta => new { ta.AppTaskId, ta.UserId });

                entity.HasOne(ta => ta.AppTask)
                      .WithMany(t => t.Assignments)
                      .HasForeignKey(ta => ta.AppTaskId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(ta => ta.User)
                      .WithMany(u => u.TaskAssignments)
                      .HasForeignKey(ta => ta.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}