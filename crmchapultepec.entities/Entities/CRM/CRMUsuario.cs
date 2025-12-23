using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.Entities.CRM
{
    public class CRMUsuario
    {
        [Key]
        public int UsuarioId { get; set; }
        public string UserName { get; set; } = "";
        public string? Telefono { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime FechaCreacion { get; set; }
        public DateTime FechaActualizacion { get; set; }

        public ICollection<CRMEquipoUsuario> Equipos { get; set; } = new List<CRMEquipoUsuario>();

    }
}
