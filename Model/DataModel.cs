using System.ComponentModel.DataAnnotations;

namespace Model
{
    public class DataModel
    {
        public string TenantId
        {
            get
            {
                return PatientId.Split('#')[0];
            }
        }

        [Required, RegularExpression("^([0-9]+#[^#]+)$")]
        public string PatientId { get; set; }

        [Required]
        public string CaseId { get; set; }

        [Required]
        public string DataAsJson { get; set; }

        [Required]
        public long ExpirationDate { get; set; }
    }
}
