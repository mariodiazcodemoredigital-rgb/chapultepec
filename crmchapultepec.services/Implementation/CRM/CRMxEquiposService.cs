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
    public class CRMxEquiposService : ICRMxEquiposService
    {
        private readonly CRMxEquiposRepository _CRMxEquiposRepository;
        public CRMxEquiposService(CRMxEquiposRepository crmxEquiposRepository)
        {
            _CRMxEquiposRepository = crmxEquiposRepository;
        }

        public async Task<int> CreateAsync(CRMEquipo e, CancellationToken ct = default)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));

            e.Nombre = e.Nombre?.Trim();
            if (string.IsNullOrWhiteSpace(e.Nombre))
                throw new ArgumentException("El nombre del equipo es obligatorio.", nameof(e.Nombre));

            // Normaliza valores por defecto
            if (e.FechaCreacion == default) e.FechaCreacion = DateTime.UtcNow;
            // Si manejas Activo por defecto en true:
            // if (!e.Activo) e.Activo = true;  // opcional, según tu regla de negocio

            // Regla de unicidad por nombre
            if (await _CRMxEquiposRepository.ExistsByNombreAsync(e.Nombre, ct))
                throw new InvalidOperationException($"Ya existe un equipo con el nombre '{e.Nombre}'.");

            // Persiste y devuelve el Id
            return await _CRMxEquiposRepository.CreateAsync(e, ct);
        }



        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentException("Id inválido.", nameof(id));
            await _CRMxEquiposRepository.DeleteAsync(id, ct);
        }

        public Task<List<CRMEquipo>> GetAllAsync(string? search = null, CancellationToken ct = default)
         => _CRMxEquiposRepository.GetAllAsync(search?.Trim(), ct);

        public Task<CRMEquipo?> GetAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentException("Id inválido.", nameof(id));
            return _CRMxEquiposRepository.GetByIdAsync(id, ct);
        }

        public async Task ToggleActivoAsync(int id, bool activo, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentException("Id inválido.", nameof(id));
            await _CRMxEquiposRepository.ToggleActivoAsync(id, activo, ct);
        }

        public async Task UpdateAsync(CRMEquipo e, CancellationToken ct = default)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));
            if (e.EquipoId <= 0) throw new ArgumentException("Id inválido.", nameof(e.EquipoId));

            e.Nombre = e.Nombre?.Trim();
            if (string.IsNullOrWhiteSpace(e.Nombre))
                throw new ArgumentException("El nombre del equipo es obligatorio.", nameof(e.Nombre));

            // Chequea unicidad excluyendo el propio Id
            if (await _CRMxEquiposRepository.ExistsByNombreAsync(e.Nombre, ct, excludeId: e.EquipoId))
                throw new InvalidOperationException($"Ya existe otro equipo con el nombre '{e.Nombre}'.");

            await _CRMxEquiposRepository.UpdateAsync(e, ct);
        }
    }
}
