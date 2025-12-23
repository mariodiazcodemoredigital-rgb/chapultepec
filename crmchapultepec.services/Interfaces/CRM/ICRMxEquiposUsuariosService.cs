using crmchapultepec.entities.Entities.CRM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Interfaces.CRM
{
    public interface ICRMxEquiposUsuariosService
    {
        Task<List<CRMEquipoUsuario>> GetByEquipoAsync(int equipoId, bool incluirInactivos = true, CancellationToken ct = default);
        Task<List<CRMEquipoUsuario>> GetByUsuarioAsync(int usuarioId, bool incluirInactivos = true, CancellationToken ct = default);
        Task<bool> ExistsAsync(int equipoId, int usuarioId, CancellationToken ct = default);
        /// <summary>
        /// Asigna un usuario a un equipo. Si ya existe, devuelve el Id existente.
        /// </summary>
        Task<int> AddAsync(int equipoId, int usuarioId, CancellationToken ct = default);
        Task RemoveAsync(int equipoUsuarioId, CancellationToken ct = default);
        Task ToggleActivoAsync(int equipoUsuarioId, bool activo, CancellationToken ct = default);

        /// <summary>
        /// Marca como activo/inactivo todas las relaciones (CRMEquipoUsuario) de un usuario.
        /// </summary>
        Task SetActivoByUsuarioAsync(int usuarioId, bool activo, CancellationToken ct = default);

    }
}
