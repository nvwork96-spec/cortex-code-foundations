using System;
using System.Collections.Generic;

namespace Akesa.Crm.LeadUplift.Tests
{
    public sealed class MemoryLeadData : ILeadData
    {
        private readonly Dictionary<Guid, int> _activities = new Dictionary<Guid, int>();
        private readonly List<ProjectDraft> _projects = new List<ProjectDraft>();
        private readonly List<Guid> _projectIds = new List<Guid>();
        private readonly Dictionary<Guid, string> _projectCodes = new Dictionary<Guid, string>();
        private readonly List<TrancheDraft> _tranches = new List<TrancheDraft>();
        private readonly List<Guid> _trancheIds = new List<Guid>();

        public IReadOnlyList<ProjectDraft> Projects => _projects;
        public IReadOnlyList<TrancheDraft> Tranches => _tranches;

        public void SetActivities(Guid leadId, int count)
        {
            _activities[leadId] = count;
        }

        public int CountRelatedActivities(Guid leadId)
        {
            return _activities.ContainsKey(leadId) ? _activities[leadId] : 0;
        }

        public Guid? FindOpenProjectByCustomerAccount(string customerAccountNumber)
        {
            for (var i = 0; i < _projects.Count; i++)
            {
                if (string.Equals(_projects[i].CustomerAccountNumber, customerAccountNumber, StringComparison.OrdinalIgnoreCase))
                {
                    return _projectIds[i];
                }
            }

            return null;
        }

        public int NextTrancheSequence(Guid projectId)
        {
            var max = 0;
            foreach (var tranche in _tranches)
            {
                if (tranche.ProjectId == projectId && tranche.Sequence > max)
                {
                    max = tranche.Sequence;
                }
            }

            return max + 1;
        }

        public Guid CreateProject(ProjectDraft draft)
        {
            var id = Guid.NewGuid();
            _projects.Add(draft);
            _projectIds.Add(id);
            _projectCodes[id] = "APC-" + _projects.Count.ToString("000");
            return id;
        }

        public Guid CreateTranche(TrancheDraft draft)
        {
            var id = Guid.NewGuid();
            _tranches.Add(draft);
            _trancheIds.Add(id);
            return id;
        }

        public string GetProjectCode(Guid projectId)
        {
            return _projectCodes[projectId];
        }

        public string GetTrancheCode(Guid trancheId)
        {
            var index = _trancheIds.IndexOf(trancheId);
            return _tranches[index].Code;
        }
    }
}
