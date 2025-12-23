using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.Entities.CRM
{
    public class CRMEquipoUsuario
    {
        [Key]
        public int EquipoUsuarioId { get; set; }
        public int EquipoId { get; set; }
        public int UsuarioId { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime FechaCreacion { get; set; }

        public CRMEquipo? Equipo { get; set; }
        public CRMUsuario? Usuario { get; set; }
    }
}
