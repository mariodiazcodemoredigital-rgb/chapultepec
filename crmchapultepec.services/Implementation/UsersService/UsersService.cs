using crmchapultepec.data;
using crmchapultepec.data.Data;
using crmchapultepec.data.Repositories.Users;
using crmchapultepec.services.Interfaces.UsersService;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Implementation.UsersService
{
    public class UsersService : IUsersService
    {
        private readonly UsersRepository _usersRepository;
        public UsersService(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        //Valida Usuario Logueado
        public async Task<Usuario?> ValidarUsuario(string username, string password)
        {
            return await _usersRepository.ValidarUsuario(username, password);
        }

        // ✅ Crear un nuevo rol
        public async Task<bool> CrearRolAsync(string nombreRol)
        {
            return await _usersRepository.CrearRolAsync(nombreRol);
        }

        // ✅ Crear un nuevo usuario y asignarle un rol
        public async Task<IdentityResult> CrearUsuarioAsync(string userName, string fullName, string email, string password, string rolAsignado = "Usuario Sistema")
        {
            return await _usersRepository.CrearUsuarioAsync(userName, fullName, email, password, rolAsignado);
        }

        // ✅ Obtener todos los usuarios
        public async Task<List<ApplicationUser>> ObtenerUsuariosAsync()
        {
            return await _usersRepository.ObtenerUsuariosAsync();
        }

        public async Task<ApplicationUser?> ObtenerUsuarioPorIdAsync(string userId)
            => await _usersRepository.ObtenerUsuarioPorIdAsync(userId);

        public async Task<IdentityResult> ActualizarUsuarioBasicoAsync(string id, string fullName, string userName, string email, string? phoneNumber)
            => await _usersRepository.ActualizarUsuarioBasicoAsync(id, fullName, userName, email, phoneNumber);

        public async Task<bool> EstablecerActivoAsync(string id, bool activo)
            => await _usersRepository.EstablecerActivoAsync(id, activo);


        // ✅ Obtener todos los roles
        public async Task<List<IdentityRole>> ObtenerRolesAsync()
        {
            return await _usersRepository.ObtenerRolesAsync();
        }

        public async Task<List<string>> ObtenerRolesDisponiblesAsync()
        {
            return await _usersRepository.ObtenerRolesDisponiblesAsync();
        }

        // ✅ Asignar un rol a un usuario
        public async Task<bool> AsignarRolAUsuarioAsync(string userId, string rol)
        {
            return await _usersRepository.AsignarRolAUsuarioAsync(userId, rol);
        }

        // ✅ Eliminar un usuario
        public async Task<bool> EliminarUsuarioAsync(string userId)
        {
            return await _usersRepository.EliminarUsuarioAsync(userId);
        }

        // ✅ Cambiar contraseña de un usuario
        public async Task<IdentityResult> CambiarPasswordAsync(string userId, string newPassword)
        {
            return await _usersRepository.CambiarPasswordAsync(userId, newPassword);
        }

        // ✅ Obtener roles de un usuario
        public async Task<IList<string>> ObtenerRolesDeUsuarioAsync(string userId)
        {
            return await _usersRepository.ObtenerRolesDeUsuarioAsync(userId);
        }

        //  Elimina un rol
        public async Task<bool> EliminarRolAsync(string roleName)
        {
            return await _usersRepository.EliminarRolAsync(roleName);
        }

        //  Actualiza un rol
        public async Task<bool> ActualizarRolAsync(string roleId, string nuevoNombre)
        {
            return await _usersRepository.ActualizarRolAsync(roleId, nuevoNombre);
        }

        public async Task<bool> ReemplazarRolDeUsuarioAsync(string userId, string nuevoRol)
        {
            return await _usersRepository.ReemplazarRolDeUsuarioAsync(userId, nuevoRol);
        }



    }
}
