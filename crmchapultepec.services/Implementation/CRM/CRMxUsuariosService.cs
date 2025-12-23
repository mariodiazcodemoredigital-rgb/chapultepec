using crmchapultepec.data.Repositories.CRM;
using crmchapultepec.entities.Entities.CRM;
using crmchapultepec.services.Interfaces.CRM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Implementation.CRM
{
    public class CRMxUsuariosService : ICRMxUsuariosService
    {
        private readonly CRMxUsuariosRepository _repo;

        public CRMxUsuariosService(CRMxUsuariosRepository repo)
        {
            _repo = repo;
        }

        public Task<List<CRMUsuario>> GetAllAsync(string? search = null, CancellationToken ct = default)
            => _repo.GetAllAsync(search?.Trim(), ct);

        public Task<CRMUsuario?> GetAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentException("Id inválido.", nameof(id));
            return _repo.GetByIdAsync(id, ct);
        }

        public async Task<int> CreateAsync(CRMUsuario u, CancellationToken ct = default)
        {
            if (u is null) throw new ArgumentNullException(nameof(u));

            u.UserName = u.UserName?.Trim();
            u.Telefono = string.IsNullOrWhiteSpace(u.Telefono) ? null : u.Telefono!.Trim();

            if (string.IsNullOrWhiteSpace(u.UserName))
                throw new ArgumentException("El usuario es obligatorio.", nameof(u.UserName));

            // Unicidad por nombre de usuario
            if (await _repo.ExistsByUserNameAsync(u.UserName!, ct))
                throw new InvalidOperationException($"Ya existe un usuario con el nombre '{u.UserName}'.");

            if (u.FechaCreacion == default)
                u.FechaCreacion = DateTime.UtcNow;

            return await _repo.CreateAsync(u, ct);
        }

        public async Task UpdateAsync(CRMUsuario u, CancellationToken ct = default)
        {
            if (u is null) throw new ArgumentNullException(nameof(u));
            if (u.UsuarioId <= 0) throw new ArgumentException("Id inválido.", nameof(u.UsuarioId));

            u.UserName = u.UserName?.Trim();
            u.Telefono = string.IsNullOrWhiteSpace(u.Telefono) ? null : u.Telefono!.Trim();

            if (string.IsNullOrWhiteSpace(u.UserName))
                throw new ArgumentException("El usuario es obligatorio.", nameof(u.UserName));

            // Unicidad excluyendo el propio Id
            if (await _repo.ExistsByUserNameAsync(u.UserName!, ct, excludeId: u.UsuarioId))
                throw new InvalidOperationException($"Ya existe otro usuario con el nombre '{u.UserName}'.");

            await _repo.UpdateAsync(u, ct);
        }

        public async Task ToggleActivoAsync(int id, bool activo, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentException("Id inválido.", nameof(id));
            await _repo.ToggleActivoAsync(id, activo, ct);
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentException("Id inválido.", nameof(id));
            await _repo.DeleteAsync(id, ct);
        }
    }
}
