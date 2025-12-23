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
    public class CRMxEquiposUsuariosRepository
    {
        private readonly ApplicationDbContext _context;
        public CRMxEquiposUsuariosRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        #region Consultas

        /// <summary>
        /// Lista miembros de un equipo, incluyendo el usuario (ligero).
        /// </summary>
        public Task<List<CRMEquipoUsuario>> GetByEquipoAsync(int equipoId, bool incluirInactivos = true, CancellationToken ct = default)
        {
            if (equipoId <= 0) return Task.FromResult(new List<CRMEquipoUsuario>());

            var q = _context.CRMEquipoUsuario
                .AsNoTracking()
                .Include(x => x.Usuario)
                .Where(x => x.EquipoId == equipoId);

            if (!incluirInactivos)
                q = q.Where(x => x.Activo);

            return q.OrderBy(x => x.Usuario!.UserName).ToListAsync(ct);
        }

        /// <summary>
        /// (Opcional) Lista equipos a los que pertenece un usuario.
        /// </summary>
        public Task<List<CRMEquipoUsuario>> GetByUsuarioAsync(int usuarioId, bool incluirInactivos = true, CancellationToken ct = default)
        {
            if (usuarioId <= 0) return Task.FromResult(new List<CRMEquipoUsuario>());

            var q = _context.CRMEquipoUsuario
                .AsNoTracking()
                .Include(x => x.Equipo)
                .Where(x => x.UsuarioId == usuarioId);

            if (!incluirInactivos)
                q = q.Where(x => x.Activo);

            return q.OrderBy(x => x.Equipo!.Nombre).ToListAsync(ct);
        }

        /// <summary>
        /// Verifica si ya existe la relación Equipo-Usuario (única).
        /// </summary>
        public Task<bool> ExistsAsync(int equipoId, int usuarioId, CancellationToken ct = default)
            => _context.CRMEquipoUsuario
                       .AsNoTracking()
                       .AnyAsync(x => x.EquipoId == equipoId && x.UsuarioId == usuarioId, ct);

        #endregion

        #region Comandos

        /// <summary>
        /// Crea la relación Equipo-Usuario. 
        /// Devuelve 0 si ya existía (manteniendo tu comportamiento actual).
        /// </summary>
        public async Task<int> AddAsync(int equipoId, int usuarioId, CancellationToken ct = default)
        {
            if (equipoId <= 0) throw new ArgumentException("Equipo inválido.", nameof(equipoId));
            if (usuarioId <= 0) throw new ArgumentException("Usuario inválido.", nameof(usuarioId));

            // Valida FKs para dar errores claros
            var teamExists = await _context.CRMEquipo
                                           .AsNoTracking()
                                           .AnyAsync(e => e.EquipoId == equipoId, ct);
            if (!teamExists) throw new KeyNotFoundException($"No existe el equipo Id={equipoId}.");

            var userExists = await _context.CRMUsuario
                                           .AsNoTracking()
                                           .AnyAsync(u => u.UsuarioId == usuarioId, ct);
            if (!userExists) throw new KeyNotFoundException($"No existe el usuario Id={usuarioId}.");

            // Evita duplicados
            var exists = await ExistsAsync(equipoId, usuarioId, ct);
            if (exists) return 0;

            var rel = new CRMEquipoUsuario
            {
                EquipoId = equipoId,
                UsuarioId = usuarioId,
                Activo = true
            };

            _context.CRMEquipoUsuario.Add(rel);
            await _context.SaveChangesAsync(ct);
            return rel.EquipoUsuarioId;
        }

        /// <summary>
        /// Elimina la relación.
        /// </summary>
        public async Task RemoveAsync(int equipoUsuarioId, CancellationToken ct = default)
        {
            if (equipoUsuarioId <= 0) return;

            var rel = await _context.CRMEquipoUsuario
                                    .FirstOrDefaultAsync(r => r.EquipoUsuarioId == equipoUsuarioId, ct);
            if (rel is null) return;

            _context.CRMEquipoUsuario.Remove(rel);
            await _context.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Habilita/Deshabilita una relación (soft toggle).
        /// </summary>
        public async Task ToggleActivoAsync(int equipoUsuarioId, bool activo, CancellationToken ct = default)
        {
            if (equipoUsuarioId <= 0) throw new ArgumentException("Id inválido.", nameof(equipoUsuarioId));

            var rel = await _context.CRMEquipoUsuario
                                    .FirstOrDefaultAsync(r => r.EquipoUsuarioId == equipoUsuarioId, ct);
            if (rel is null) throw new KeyNotFoundException($"No existe la relación Id={equipoUsuarioId}.");

            rel.Activo = activo;
            await _context.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Actualiza el campo Activo de todas las relaciones de un usuario en CRMEquipoUsuario.
        /// </summary>
        public async Task SetActivoByUsuarioAsync(int usuarioId, bool activo, CancellationToken ct = default)
        {
            if (usuarioId <= 0) throw new ArgumentException("Id inválido.", nameof(usuarioId));

            // EF Core 7+ soporta ExecuteUpdateAsync (eficiente, no carga entidades)
            await _context.CRMEquipoUsuario
                .Where(r => r.UsuarioId == usuarioId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.Activo, _ => activo),
                    ct
                );
        }

        #endregion

    }
}
