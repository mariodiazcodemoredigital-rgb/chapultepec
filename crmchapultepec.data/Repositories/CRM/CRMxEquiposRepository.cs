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
    public class CRMxEquiposRepository
    {
        private readonly ApplicationDbContext _context;
        public CRMxEquiposRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        #region Equipos


        // ----------------------------
        // LISTAR / OBTENER
        // ----------------------------
        public async Task<List<CRMEquipo>> GetAllAsync(string? search = null, CancellationToken ct = default)
        {
            var q = _context.CRMEquipo.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = $"%{search.Trim()}%";
                q = q.Where(e => EF.Functions.Like(e.Nombre!, term));
            }

            return await q.OrderBy(e => e.Nombre).ToListAsync(ct);
        }

        public async Task<CRMEquipo?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) return null;
            return await _context.CRMEquipo
                                 .AsNoTracking()
                                 .FirstOrDefaultAsync(e => e.EquipoId == id, ct);
        }



        // ----------------------------
        // CREAR
        // ----------------------------
        public async Task<int> CreateAsync(CRMEquipo e, CancellationToken ct = default)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));

            e.Nombre = e.Nombre?.Trim();
            if (string.IsNullOrWhiteSpace(e.Nombre))
                throw new ArgumentException("El nombre del equipo es obligatorio.", nameof(e.Nombre));

            // Unicidad por nombre (case-insensitive):
            if (await ExistsByNombreAsync(e.Nombre, ct))
                throw new InvalidOperationException($"Ya existe un equipo con el nombre '{e.Nombre}'.");

            if (e.FechaCreacion == default)
                e.FechaCreacion = DateTime.UtcNow;

            _context.CRMEquipo.Add(e);
            await _context.SaveChangesAsync(ct);
            return e.EquipoId;
        }

        // ----------------------------
        // ACTUALIZAR
        // ----------------------------
        public async Task UpdateAsync(CRMEquipo e, CancellationToken ct = default)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));
            if (e.EquipoId <= 0) throw new ArgumentException("Id inválido.", nameof(e.EquipoId));

            var db = await _context.CRMEquipo.FirstOrDefaultAsync(x => x.EquipoId == e.EquipoId, ct);
            if (db is null) throw new KeyNotFoundException($"No existe el equipo Id={e.EquipoId}.");

            var nuevoNombre = e.Nombre?.Trim();
            if (string.IsNullOrWhiteSpace(nuevoNombre))
                throw new ArgumentException("El nombre del equipo es obligatorio.", nameof(e.Nombre));

            // Si cambió el nombre, valida unicidad
            if (!string.Equals(db.Nombre, nuevoNombre, StringComparison.Ordinal))
            {
                if (await ExistsByNombreAsync(nuevoNombre!, ct, excludeId: e.EquipoId))
                    throw new InvalidOperationException($"Ya existe otro equipo con el nombre '{nuevoNombre}'.");
                db.Nombre = nuevoNombre!;
            }

            // Otros campos actualizables
            db.Activo = e.Activo;
            // Mantén FechaCreacion tal cual; si quieres permitir editarla, asígnala aquí.

            await _context.SaveChangesAsync(ct);
        }

        // ----------------------------
        // TOGGLE ACTIVO
        // ----------------------------
        public async Task ToggleActivoAsync(int id, bool activo, CancellationToken ct = default)
        {
            var db = await _context.CRMEquipo.FirstOrDefaultAsync(x => x.EquipoId == id, ct);
            if (db is null) throw new KeyNotFoundException($"No existe el equipo Id={id}.");

            db.Activo = activo;
            await _context.SaveChangesAsync(ct);
        }

        // ----------------------------
        // ELIMINAR
        // ----------------------------
        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            var db = await _context.CRMEquipo.FirstOrDefaultAsync(x => x.EquipoId == id, ct);
            if (db is null) return;

            _context.CRMEquipo.Remove(db);
            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException != null)
            {
                // Si hay FKs (p.ej. usuarios-asignaciones al equipo) informamos claramente:
                throw new InvalidOperationException(
                    "No se puede eliminar el equipo porque tiene relaciones asociadas (usuarios, asignaciones, etc.). " +
                    "Desasigne o elimine esas relaciones primero.", ex);
            }
        }

        // ----------------------------
        // HELPERS
        // ----------------------------
        public async Task<bool> ExistsByNombreAsync(string nombre, CancellationToken ct = default, int? excludeId = null)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return false;
            nombre = nombre.Trim();

            var q = _context.CRMEquipo.AsQueryable();

            if (excludeId is not null)
                q = q.Where(e => e.EquipoId != excludeId.Value);

            // Comparación case-insensitive
            return await q.AnyAsync(e => e.Nombre != null &&
                                         e.Nombre.ToLower() == nombre.ToLower(), ct);
            // Alternativa con COLLATE si quieres forzar sin sensibilidad a acentos:
            // return await q.AnyAsync(e => EF.Functions.Collate(e.Nombre!, "SQL_Latin1_General_CP1_CI_AI") 
            //                           == EF.Functions.Collate(nombre, "SQL_Latin1_General_CP1_CI_AI"), ct);
        }

        #endregion

    }
}
