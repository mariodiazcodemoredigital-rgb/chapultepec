using crmchapultepec.entities.EvolutionWebhook;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.data.Data
{
    public class CrmInboxDbContext : DbContext
    {
        public CrmInboxDbContext(DbContextOptions<CrmInboxDbContext> options) : base(options) { }

        public DbSet<CrmThread> CrmThreads { get; set; } = null!;
        public DbSet<CrmMessage> CrmMessages { get; set; } = null!;
        public DbSet<PipelineHistory> PipelineHistories { get; set; } = null!;
        public DbSet<CrmContact> CrmContactDbs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<CrmThread>()
                .HasIndex(t => t.ThreadId)
                .IsUnique();

            modelBuilder.Entity<CrmMessage>()
                .HasIndex(m => new { m.ExternalId })
                .HasDatabaseName("IX_CrmMessage_ExternalId");

            modelBuilder.Entity<CrmMessage>()
                .HasIndex(m => new { m.RawHash })
                .HasDatabaseName("IX_CrmMessage_RawHash");

            // Relaciones
            modelBuilder.Entity<CrmMessage>()
                .HasOne(m => m.Thread)
                .WithMany(t => t.Messages)
                .HasForeignKey(m => m.ThreadRefId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PipelineHistory>()
                .HasOne(ph => ph.Thread)
                .WithMany()
                .HasForeignKey(ph => ph.ThreadRefId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
