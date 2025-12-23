using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.Entities.CRM
{
    public class CRMEquipo
    {
        [Key]                              // <- Indica PK sin usar OnModelCreating
        public int EquipoId { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; } = true;
        public DateTime FechaCreacion { get; set; }
        public DateTime FechaActualizacion { get; set; }

        public ICollection<CRMEquipoUsuario> Miembros { get; set; } = new List<CRMEquipoUsuario>();

    }
}
