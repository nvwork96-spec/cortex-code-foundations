using System;

namespace Akesa.Crm.LeadUplift
{
    public interface ILeadData
    {
        int CountRelatedActivities(Guid leadId);

        Guid? FindOpenProjectByCustomerAccount(string customerAccountNumber);

        int NextTrancheSequence(Guid projectId);

        Guid CreateProject(ProjectDraft draft);

        Guid CreateTranche(TrancheDraft draft);

        string GetProjectCode(Guid projectId);

        string GetTrancheCode(Guid trancheId);
    }

    public sealed class ProjectDraft
    {
        public string Name { get; set; }
        public string CustomerName { get; set; }
        public string CustomerAccountNumber { get; set; }
        public string Country { get; set; }
        public string CurrencyIsoCode { get; set; }
    }

    public sealed class TrancheDraft
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public int Sequence { get; set; }
        public Guid ProjectId { get; set; }
        public Guid LeadId { get; set; }
        public DateTime? LeadTime { get; set; }
    }
}
