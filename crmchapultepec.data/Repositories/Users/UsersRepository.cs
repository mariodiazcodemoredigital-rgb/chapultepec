using crmchapultepec.data.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.data.Repositories.Users
{
    public class UsersRepository
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;
        public UsersRepository(IConfiguration configuration, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ApplicationDbContext context)
        {
            _configuration = configuration;
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
            _context = context;
        }


        public async Task<Usuario?> ValidarUsuario(string username, string password)
        {
            var user = await _context.Usuarios.FirstOrDefaultAsync(u => u.Username == username);

            if (user == null) return null;

            // 🔹 Aquí debes validar el hash de la contraseña
            return user.PasswordHash == password ? user : null;
        }

        // Crear un rol
        public async Task<bool> CrearRolAsync(string nombreRol)
        {
            if (!await _roleManager.RoleExistsAsync(nombreRol))
            {
                var result = await _roleManager.CreateAsync(new IdentityRole(nombreRol));
                return result.Succeeded; // ✅ Devolvemos true/false
            }
            return false;
        }


        // Crear usuario y asignar rol
        public async Task<IdentityResult> CrearUsuarioAsync(string userName, string fullName, string email, string password, string rolPorDefecto = "Usuario Sistema")
        {
            var user = new ApplicationUser
            {
                UserName = userName,  // Para login
                Email = email,
                FullName = fullName
            };

            var result = await _userManager.CreateAsync(user, password);

            if (result.Succeeded)
            {
                // 2) Confirmar email internamente (sin enviar correo)
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var confirmResult = await _userManager.ConfirmEmailAsync(user, token);
                if (!confirmResult.Succeeded)
                    return IdentityResult.Failed(confirmResult.Errors.ToArray());


                if (!await _roleManager.RoleExistsAsync(rolPorDefecto))
                {
                    await _roleManager.CreateAsync(new IdentityRole(rolPorDefecto));
                }

                await _userManager.AddToRoleAsync(user, rolPorDefecto);
            }

            return result;
        }



        // Obtener todos los usuarios
        public async Task<List<ApplicationUser>> ObtenerUsuariosAsync()
        {
            return await Task.FromResult(_userManager.Users.ToList());
        }

        public async Task<ApplicationUser?> ObtenerUsuarioPorIdAsync(string userId)
        {
            return await _userManager.FindByIdAsync(userId);
        }


        public async Task<IdentityResult> ActualizarUsuarioBasicoAsync(string id, string fullName, string userName, string email, string? phoneNumber)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user is null)
                return IdentityResult.Failed(new IdentityError { Code = "UserNotFound", Description = "Usuario no encontrado." });

            // -------- PRECHEQUEO: USERNAME ÚNICO --------
            var normalizedUserName = _userManager.NormalizeName(userName);
            var userNameTomado = await _userManager.Users
                .AsNoTracking()
                .AnyAsync(u => u.NormalizedUserName == normalizedUserName && u.Id != id);

            if (userNameTomado)
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "DuplicateUserName",
                    Description = "El nombre de usuario ya está en uso."
                });

            // -------- (OPCIONAL) PRECHEQUEO: EMAIL ÚNICO, SE CONFIGURA EN PROGRAM OPTIONS.USER.RequireUniqueEmail --------
            if (_userManager.Options.User.RequireUniqueEmail && !string.IsNullOrWhiteSpace(email))
            {
                var normalizedEmail = _userManager.NormalizeEmail(email);
                var emailTomado = await _userManager.Users
                    .AsNoTracking()
                    .AnyAsync(u => u.NormalizedEmail == normalizedEmail && u.Id != id);

                if (emailTomado)
                    return IdentityResult.Failed(new IdentityError
                    {
                        Code = "DuplicateEmail",
                        Description = "El email ya está en uso."
                    });
            }

            // -------- ASIGNAR CAMPOS --------
            user.FullName = fullName;
            user.UserName = userName;
            user.Email = email;
            user.PhoneNumber = phoneNumber;

            // Actualizar normalizados explícitamente (por si tu store no lo hace en UpdateAsync)
            await _userManager.UpdateNormalizedUserNameAsync(user);
            await _userManager.UpdateNormalizedEmailAsync(user);

            try
            {
                // Dispara validadores y persiste
                var res = await _userManager.UpdateAsync(user);
                return res;
            }
            catch (DbUpdateException)
            {
                // Si el prechequeo falló por condición de carrera, regresa un error claro
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "DuplicateUserNameOrEmail",
                    Description = "El usuario o email ya existe."
                });
            }
        }

        public async Task<bool> EstablecerActivoAsync(string id, bool activo)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return false;

            if (activo)
            {
                // Quitar lockout
                var res = await _userManager.SetLockoutEndDateAsync(user, null);
                return res.Succeeded;
            }
            else
            {
                // Bloquear: usa una fecha futura (p. ej. 100 años) o tu propia convención
                var res = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                return res.Succeeded;
            }
        }

        // Obtener roles de un usuario
        public async Task<List<IdentityRole>> ObtenerRolesAsync()
        {
            return await Task.FromResult(_roleManager.Roles.ToList());
        }

        public async Task<List<string>> ObtenerRolesDisponiblesAsync()
        {
            var excluidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Administrador"
            };
            // Si prefieres evitar EF async, usa .ToList() en lugar de .ToListAsync()
            return await _roleManager.Roles
                .Select(r => r.Name!)
                .Where(n => !excluidos.Contains(n))
                .OrderBy(n => n)
                .ToListAsync();
        }

        //Asignar un rol a un usuario
        public async Task<bool> AsignarRolAUsuarioAsync(string userId, string rol)
        {
            // 1️⃣ Buscar el usuario
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return false;

            // 2️⃣ Verificar si el rol existe
            if (!await _roleManager.RoleExistsAsync(rol))
                return false;

            // 3️⃣ Asignar rol
            var result = await _userManager.AddToRoleAsync(user, rol);
            return result.Succeeded;
        }

        //  Eliminar un usuario
        public async Task<bool> EliminarUsuarioAsync(string userId)
        {
            // 1️⃣ Buscar el usuario por ID
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return false; // No existe

            // 2️⃣ Eliminar usuario
            var result = await _userManager.DeleteAsync(user);

            // 3️⃣ Devolver true si se eliminó correctamente
            return result.Succeeded;
        }

        // Cambiar contraseña de un usuario
        public async Task<IdentityResult> CambiarPasswordAsync(string userId, string newPassword)
        {
            // 1️⃣ Buscar el usuario
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return IdentityResult.Failed(new IdentityError { Description = "Usuario no encontrado." });

            // 2️⃣ Eliminar la contraseña actual (si existe)
            var removeResult = await _userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
                return removeResult; // Si falla, devolver el error

            // 3️⃣ Establecer la nueva contraseña
            return await _userManager.AddPasswordAsync(user, newPassword);
        }

        //  Obtener roles de un usuario
        public async Task<IList<string>> ObtenerRolesDeUsuarioAsync(string userId)
        {
            // 1️⃣ Buscar el usuario
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return new List<string>(); // Devuelve lista vacía si no existe

            // 2️⃣ Obtener los roles
            return await _userManager.GetRolesAsync(user);
        }

        //  Elimina un rol
        public async Task<bool> EliminarRolAsync(string roleName)
        {
            var rol = await _roleManager.FindByNameAsync(roleName);
            if (rol == null) return false;

            var result = await _roleManager.DeleteAsync(rol);
            return result.Succeeded;
        }


        //  Actualiza un rol
        public async Task<bool> ActualizarRolAsync(string roleId, string nuevoNombre)
        {
            // Buscar rol por ID
            var rol = await _roleManager.FindByIdAsync(roleId);
            if (rol == null) return false;

            // Actualizar nombre
            rol.Name = nuevoNombre;

            // Guardar cambios
            var result = await _roleManager.UpdateAsync(rol);
            return result.Succeeded;
        }


        public async Task<bool> ReemplazarRolDeUsuarioAsync(string userId, string nuevoRol)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return false;

            var actuales = await _userManager.GetRolesAsync(user);

            // Quitar todos los roles menos el nuevo (por si ya lo tiene)
            var aQuitar = actuales.Where(r => !string.Equals(r, nuevoRol, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (aQuitar.Length > 0)
            {
                var resRemove = await _userManager.RemoveFromRolesAsync(user, aQuitar);
                if (!resRemove.Succeeded) return false;
            }

            // Si no lo tiene, agregarlo
            if (!actuales.Any(r => string.Equals(r, nuevoRol, StringComparison.OrdinalIgnoreCase)))
            {
                var resAdd = await _userManager.AddToRoleAsync(user, nuevoRol);
                if (!resAdd.Succeeded) return false;
            }

            return true;
        }



    }
}
