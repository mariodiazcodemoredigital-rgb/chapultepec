using crmchapultepec.entities.Entities.CRM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Interfaces.CRM
{
    public interface ICRMxUsuariosService
    {
        Task<List<CRMUsuario>> GetAllAsync(string? search = null, CancellationToken ct = default);
        Task<CRMUsuario?> GetAsync(int id, CancellationToken ct = default);
        Task<int> CreateAsync(CRMUsuario e, CancellationToken ct = default);
        Task UpdateAsync(CRMUsuario e, CancellationToken ct = default);
        Task DeleteAsync(int id, CancellationToken ct = default);
        Task ToggleActivoAsync(int id, bool activo, CancellationToken ct = default);
    }
}
