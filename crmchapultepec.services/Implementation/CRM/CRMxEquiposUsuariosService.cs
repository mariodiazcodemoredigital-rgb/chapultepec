using crmchapultepec.data.Repositories.CRM;
using crmchapultepec.entities.Entities.CRM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Implementation.CRM
{
    public class CRMxEquiposUsuariosService
    {
        private readonly CRMxEquiposUsuariosRepository _relRepo;
        private readonly CRMxEquiposRepository _equiposRepo;
        private readonly CRMxUsuariosRepository _usuariosRepo;

        public CRMxEquiposUsuariosService(CRMxEquiposUsuariosRepository relRepo, CRMxEquiposRepository equiposRepo, CRMxUsuariosRepository usuariosRepo)
        {
            _relRepo = relRepo;
            _equiposRepo = equiposRepo;
            _usuariosRepo = usuariosRepo;
        }

        public Task<List<CRMEquipoUsuario>> GetByEquipoAsync(int equipoId, bool incluirInactivos = true, CancellationToken ct = default)
        {
            if (equipoId <= 0) throw new ArgumentException("Id de equipo inválido.", nameof(equipoId));
            return _relRepo.GetByEquipoAsync(equipoId, incluirInactivos, ct);
        }

        public Task<List<CRMEquipoUsuario>> GetByUsuarioAsync(int usuarioId, bool incluirInactivos = true, CancellationToken ct = default)
        {
            if (usuarioId <= 0) throw new ArgumentException("Id de usuario inválido.", nameof(usuarioId));
            return _relRepo.GetByUsuarioAsync(usuarioId, incluirInactivos, ct);
        }

        public Task<bool> ExistsAsync(int equipoId, int usuarioId, CancellationToken ct = default)
        {
            if (equipoId <= 0) throw new ArgumentException("Id de equipo inválido.", nameof(equipoId));
            if (usuarioId <= 0) throw new ArgumentException("Id de usuario inválido.", nameof(usuarioId));
            return _relRepo.ExistsAsync(equipoId, usuarioId, ct);
        }

        public async Task<int> AddAsync(int equipoId, int usuarioId, CancellationToken ct = default)
        {
            if (equipoId <= 0) throw new ArgumentException("Id de equipo inválido.", nameof(equipoId));
            if (usuarioId <= 0) throw new ArgumentException("Id de usuario inválido.", nameof(usuarioId));

            // Reglas de negocio extra: existan y estén activos
            var equipo = await _equiposRepo.GetByIdAsync(equipoId, ct)
                        ?? throw new KeyNotFoundException($"No existe el equipo Id={equipoId}.");
            if (!equipo.Activo)
                throw new InvalidOperationException("El equipo está inactivo.");

            var usuario = await _usuariosRepo.GetByIdAsync(usuarioId, ct)
                         ?? throw new KeyNotFoundException($"No existe el usuario Id={usuarioId}.");
            if (!usuario.Activo)
                throw new InvalidOperationException("El usuario está inactivo.");

            // Si ya existe, regresamos su Id (útil para UI idempotente)
            if (await _relRepo.ExistsAsync(equipoId, usuarioId, ct))
            {
                var existentes = await _relRepo.GetByEquipoAsync(equipoId, true, ct);
                var ya = existentes.FirstOrDefault(r => r.UsuarioId == usuarioId);
                return ya?.EquipoUsuarioId ?? 0;
            }

            return await _relRepo.AddAsync(equipoId, usuarioId, ct);
        }

        public Task RemoveAsync(int equipoUsuarioId, CancellationToken ct = default)
        {
            if (equipoUsuarioId <= 0) throw new ArgumentException("Id inválido.", nameof(equipoUsuarioId));
            return _relRepo.RemoveAsync(equipoUsuarioId, ct);
        }

        public Task ToggleActivoAsync(int equipoUsuarioId, bool activo, CancellationToken ct = default)
        {
            if (equipoUsuarioId <= 0) throw new ArgumentException("Id inválido.", nameof(equipoUsuarioId));
            return _relRepo.ToggleActivoAsync(equipoUsuarioId, activo, ct);
        }

        public Task SetActivoByUsuarioAsync(int usuarioId, bool activo, CancellationToken ct = default)
        {
            if (usuarioId <= 0) throw new ArgumentException("Id de usuario inválido.", nameof(usuarioId));
            return _relRepo.SetActivoByUsuarioAsync(usuarioId, activo, ct);
        }

    }
}
