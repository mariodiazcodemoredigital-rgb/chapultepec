using crmchapultepec.data;
using crmchapultepec.data.Data;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Interfaces.UsersService
{
    public interface IUsersService
    {
        Task<Usuario?> ValidarUsuario(string username, string password);
        Task<bool> CrearRolAsync(string nombreRol);
        Task<IdentityResult> CrearUsuarioAsync(string userName, string fullName, string email, string password, string rolAsignado = "Usuario Sistema");
        Task<List<ApplicationUser>> ObtenerUsuariosAsync();
        Task<ApplicationUser?> ObtenerUsuarioPorIdAsync(string userId);
        Task<IdentityResult> ActualizarUsuarioBasicoAsync(string id, string fullName, string userName, string email, string? phoneNumber);
        Task<bool> EstablecerActivoAsync(string id, bool activo);
        Task<List<string>> ObtenerRolesDisponiblesAsync();
        Task<List<IdentityRole>> ObtenerRolesAsync();
        Task<bool> AsignarRolAUsuarioAsync(string userId, string rol);
        Task<bool> EliminarUsuarioAsync(string userId);
        Task<IdentityResult> CambiarPasswordAsync(string userId, string newPassword);
        Task<IList<string>> ObtenerRolesDeUsuarioAsync(string userId);
        Task<bool> EliminarRolAsync(string roleName);
        Task<bool> ActualizarRolAsync(string roleId, string nuevoNombre);
        Task<bool> ReemplazarRolDeUsuarioAsync(string userId, string nuevoRol);
    }
}
