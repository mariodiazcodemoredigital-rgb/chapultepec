
using crmchapultepec.entities.Entities.CRM;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.data.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<CRMEquipo> CRMEquipo => Set<CRMEquipo>();
        public DbSet<CRMUsuario> CRMUsuario => Set<CRMUsuario>();
        public DbSet<CRMEquipoUsuario> CRMEquipoUsuario => Set<CRMEquipoUsuario>();

    }
}
