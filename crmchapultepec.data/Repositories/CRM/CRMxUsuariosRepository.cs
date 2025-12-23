using crmchapultepec.data.Data;
using crmchapultepec.entities.Entities.CRM;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.data.Repositories.CRM
{
    public class CRMxUsuariosRepository
    {
        //private readonly ApplicationDbContext _context;
        private readonly IDbContextFactory<ApplicationDbContext> _factory;
        public CRMxUsuariosRepository(IDbContextFactory<ApplicationDbContext> context)
        {
            _factory = context;
        }

        #region Usuarios

        // LISTAR
        public async Task<List<CRMUsuario>> GetAllAsync(string? search = null, CancellationToken ct = default)
        {
            await using var _context = await _factory.CreateDbContextAsync(ct);

            var q = _context.CRMUsuario.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = $"%{search.Trim()}%";
                q = q.Where(u => EF.Functions.Like(u.UserName!, term) ||
                                 EF.Functions.Like(u.Telefono ?? string.Empty, term));
            }

            return await q.OrderBy(u => u.UserName).ToListAsync(ct);
        }

        // OBTENER POR ID
        public async Task<CRMUsuario?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            await using var _context = await _factory.CreateDbContextAsync(ct);
            if (id <= 0) return null;
            return await _context.CRMUsuario
                                 .AsNoTracking()
                                 .FirstOrDefaultAsync(u => u.UsuarioId == id, ct);
        }

        // CREAR
        public async Task<int> CreateAsync(CRMUsuario u, CancellationToken ct = default)
        {
            await using var _context = await _factory.CreateDbContextAsync(ct);

            if (u is null) throw new ArgumentNullException(nameof(u));

            u.UserName = u.UserName?.Trim();
            u.Telefono = string.IsNullOrWhiteSpace(u.Telefono) ? null : u.Telefono!.Trim();

            if (string.IsNullOrWhiteSpace(u.UserName))
                throw new ArgumentException("El usuario es obligatorio.", nameof(u.UserName));

            if (await ExistsByUserNameAsync(u.UserName!, ct))
                throw new InvalidOperationException($"Ya existe un usuario con el nombre '{u.UserName}'.");

            if (u.FechaCreacion == default)
                u.FechaCreacion = DateTime.UtcNow;

            _context.CRMUsuario.Add(u);
            await _context.SaveChangesAsync(ct);
            return u.UsuarioId;
        }

        // ACTUALIZAR
        public async Task UpdateAsync(CRMUsuario u, CancellationToken ct = default)
        {
            await using var _context = await _factory.CreateDbContextAsync(ct);

            if (u is null) throw new ArgumentNullException(nameof(u));
            if (u.UsuarioId <= 0) throw new ArgumentException("Id inválido.", nameof(u.UsuarioId));

            var db = await _context.CRMUsuario.FirstOrDefaultAsync(x => x.UsuarioId == u.UsuarioId, ct);
            if (db is null) throw new KeyNotFoundException($"No existe el usuario Id={u.UsuarioId}.");

            var nuevoUser = u.UserName?.Trim();
            if (string.IsNullOrWhiteSpace(nuevoUser))
                throw new ArgumentException("El usuario es obligatorio.", nameof(u.UserName));

            // Si cambió el UserName, valida unicidad (case-insensitive)
            if (!string.Equals(db.UserName, nuevoUser, StringComparison.Ordinal))
            {
                if (await ExistsByUserNameAsync(nuevoUser!, ct, excludeId: u.UsuarioId))
                    throw new InvalidOperationException($"Ya existe otro usuario con el nombre '{nuevoUser}'.");
                db.UserName = nuevoUser!;
            }

            db.Telefono = string.IsNullOrWhiteSpace(u.Telefono) ? null : u.Telefono!.Trim();
            db.Activo = u.Activo;
            // Mantén FechaCreacion como histórico (no se toca)

            await _context.SaveChangesAsync(ct);
        }

        // TOGGLE ACTIVO
        public async Task ToggleActivoAsync(int id, bool activo, CancellationToken ct = default)
        {
            await using var _context = await _factory.CreateDbContextAsync(ct);

            var db = await _context.CRMUsuario.FirstOrDefaultAsync(x => x.UsuarioId == id, ct);
            if (db is null) throw new KeyNotFoundException($"No existe el usuario Id={id}.");

            db.Activo = activo;
            await _context.SaveChangesAsync(ct);
        }

        // ELIMINAR
        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            await using var _context = await _factory.CreateDbContextAsync(ct);

            var db = await _context.CRMUsuario.FirstOrDefaultAsync(x => x.UsuarioId == id, ct);
            if (db is null) return;

            _context.CRMUsuario.Remove(db);
            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException != null)
            {
                // Si hay relaciones (ej. CRMEquipoUsuario) devolvemos un mensaje claro
                throw new InvalidOperationException(
                    "No se puede eliminar el usuario porque tiene relaciones asociadas (pertenece a uno o más equipos). " +
                    "Quite las asignaciones primero.", ex);
            }
        }

        // HELPERS
        public async Task<bool> ExistsByUserNameAsync(string userName, CancellationToken ct = default, int? excludeId = null)
        {
            await using var _context = await _factory.CreateDbContextAsync(ct);

            if (string.IsNullOrWhiteSpace(userName)) return false;
            userName = userName.Trim();

            var q = _context.CRMUsuario.AsQueryable();
            if (excludeId is not null)
                q = q.Where(u => u.UsuarioId != excludeId.Value);

            // case-insensitive
            return await q.AnyAsync(u => u.UserName != null &&
                                         u.UserName.ToLower() == userName.ToLower(), ct);
            // Alternativa con COLLATE para ignorar acentos:
            // return await q.AnyAsync(u => EF.Functions.Collate(u.UserName!, "SQL_Latin1_General_CP1_CI_AI")
            //                           == EF.Functions.Collate(userName, "SQL_Latin1_General_CP1_CI_AI"), ct);
        }


        #endregion
    }
}
