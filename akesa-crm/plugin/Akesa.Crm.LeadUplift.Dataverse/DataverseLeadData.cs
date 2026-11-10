using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Akesa.Crm.LeadUplift
{
    public sealed class DataverseLeadData : ILeadData
    {
        private readonly IOrganizationService _service;

        public DataverseLeadData(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public int CountRelatedActivities(Guid leadId)
        {
            return Count("activitypointer", "regardingobjectid", leadId)
                + Count("annotation", "objectid", leadId);
        }

        public Guid? FindOpenProjectByCustomerAccount(string customerAccountNumber)
        {
            var query = new QueryExpression("akesa_project")
            {
                ColumnSet = new ColumnSet("akesa_projectid"),
                TopCount = 1
            };
            query.Criteria.AddCondition("akesa_customeraccountnumber", ConditionOperator.Equal, customerAccountNumber);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.AddOrder("createdon", OrderType.Ascending);
            var results = _service.RetrieveMultiple(query);
            if (results.Entities.Count == 0)
            {
                return null;
            }

            return results.Entities[0].Id;
        }

        public int NextTrancheSequence(Guid projectId)
        {
            var query = new QueryExpression("akesa_tranche")
            {
                ColumnSet = new ColumnSet("akesa_sequencenumber"),
                TopCount = 1
            };
            query.Criteria.AddCondition("akesa_project", ConditionOperator.Equal, projectId);
            query.AddOrder("akesa_sequencenumber", OrderType.Descending);
            var results = _service.RetrieveMultiple(query);
            if (results.Entities.Count == 0 || !results.Entities[0].Contains("akesa_sequencenumber"))
            {
                return 1;
            }

            return results.Entities[0].GetAttributeValue<int>("akesa_sequencenumber") + 1;
        }

        public Guid CreateProject(ProjectDraft draft)
        {
            var project = new Entity("akesa_project");
            project["akesa_name"] = draft.Name;
            Set(project, "akesa_customername", draft.CustomerName);
            Set(project, "akesa_customeraccountnumber", draft.CustomerAccountNumber);
            Set(project, "akesa_country", draft.Country);
            Set(project, "akesa_currencyisocode", draft.CurrencyIsoCode);
            return _service.Create(project);
        }

        public Guid CreateTranche(TrancheDraft draft)
        {
            var tranche = new Entity("akesa_tranche");
            tranche["akesa_name"] = draft.Name;
            tranche["akesa_tranchecode"] = draft.Code;
            tranche["akesa_sequencenumber"] = draft.Sequence;
            tranche["akesa_project"] = new EntityReference("akesa_project", draft.ProjectId);
            tranche["akesa_lead"] = new EntityReference("lead", draft.LeadId);
            if (draft.LeadTime.HasValue)
            {
                tranche["akesa_leadtime"] = draft.LeadTime.Value;
            }

            return _service.Create(tranche);
        }

        public string GetProjectCode(Guid projectId)
        {
            var project = _service.Retrieve("akesa_project", projectId, new ColumnSet("akesa_projectcode"));
            return project.GetAttributeValue<string>("akesa_projectcode");
        }

        public string GetTrancheCode(Guid trancheId)
        {
            var tranche = _service.Retrieve("akesa_tranche", trancheId, new ColumnSet("akesa_tranchecode"));
            return tranche.GetAttributeValue<string>("akesa_tranchecode");
        }

        private int Count(string entityName, string lookup, Guid id)
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(false),
                PageInfo = new PagingInfo
                {
                    Count = 1,
                    PageNumber = 1,
                    ReturnTotalRecordCount = true
                }
            };
            query.Criteria.AddCondition(lookup, ConditionOperator.Equal, id);
            var results = _service.RetrieveMultiple(query);
            if (results.TotalRecordCount >= 0)
            {
                return results.TotalRecordCount;
            }

            return results.Entities.Count;
        }

        private static void Set(Entity entity, string attribute, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                entity[attribute] = value;
            }
        }
    }
}
