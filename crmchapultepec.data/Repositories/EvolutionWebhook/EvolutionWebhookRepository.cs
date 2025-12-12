using crmchapultepec.data.Data;
using crmchapultepec.entities.EvolutionWebhook;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.data.Repositories.EvolutionWebhook
{
    public class EvolutionWebhookRepository
    {
        private readonly IDbContextFactory<CrmInboxDbContext> _dbFactory;

        public EvolutionWebhookRepository(IDbContextFactory<CrmInboxDbContext> dbFactory)
        {
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        }

        public async Task<bool> MessageExistsByRawHashAsync(string rawHash, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await db.CrmMessages.AsNoTracking().AnyAsync(m => m.RawHash == rawHash, ct);
        }

        public async Task<int> CreateOrUpdateThreadAsync(string threadId, string? businessAccountId, long timestamp, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var thread = await db.CrmThreads.SingleOrDefaultAsync(t => t.ThreadId == threadId, ct);
            if (thread == null)
            {
                thread = new CrmThread
                {
                    ThreadId = threadId,
                    BusinessAccountId = businessAccountId,
                    CreatedUtc = DateTime.UtcNow,
                    LastMessageUtc = (timestamp > 0) ? DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime : DateTime.UtcNow
                };
                db.CrmThreads.Add(thread);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                if (timestamp > 0)
                    thread.LastMessageUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
                db.CrmThreads.Update(thread);
                await db.SaveChangesAsync(ct);
            }

            return thread.Id;
        }

        public async Task<int> InsertMessageAsync(CrmMessage message, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.CrmMessages.Add(message);
            await db.SaveChangesAsync(ct);
            return message.Id;
        }

        public async Task InsertPipelineHistoryAsync(PipelineHistory pipeline, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.PipelineHistories.Add(pipeline);
            await db.SaveChangesAsync(ct);
        }

        public async Task SaveDeadLetterAsync(MessageDeadLetter deadLetter, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.MessageDeadLetters.Add(deadLetter);
            await db.SaveChangesAsync(ct);
        }


    }
}
