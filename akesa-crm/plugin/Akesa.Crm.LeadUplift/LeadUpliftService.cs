using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Akesa.Crm.LeadUplift
{
    public sealed class LeadMutations
    {
        public int? Stage { get; set; }
        public Guid? ProjectId { get; set; }
        public Guid? TrancheId { get; set; }
        public string Subject { get; set; }
        public string CustomerName { get; set; }
    }

    public sealed class OpportunityPatch
    {
        public string Product { get; set; }
        public decimal? Quantity { get; set; }
        public DateTime? LeadTime { get; set; }
        public string SupplierName { get; set; }
        public string SupplierAccountNumber { get; set; }
        public string LeadNumber { get; set; }
        public string Country { get; set; }
        public string CurrencyIsoCode { get; set; }
        public Guid? ProjectId { get; set; }
        public Guid? TrancheId { get; set; }
    }

    public sealed class LeadGateException : Exception
    {
        public LeadGateException(IReadOnlyList<string> errors)
            : base(string.Join(Environment.NewLine, errors))
        {
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
    }

    /// <summary>
    /// Chapter 1 lead rules. Country and currency are stored and copied forward.
    /// They never open or close a stage gate.
    /// </summary>
    public sealed class LeadUpliftService
    {
        private readonly ILeadData _data;

        public LeadUpliftService(ILeadData data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public LeadMutations OnCreate(IDictionary<string, object> target)
        {
            var mutations = new LeadMutations();
            if (target == null)
            {
                mutations.Stage = LeadChoices.Intake;
                return mutations;
            }

            if (!target.ContainsKey(LeadFields.Stage) || target[LeadFields.Stage] == null)
            {
                mutations.Stage = LeadChoices.Intake;
            }

            var customer = Value(target, LeadFields.CustomerName) as string;
            var company = Value(target, LeadFields.CompanyName) as string;
            if (string.IsNullOrWhiteSpace(customer) && !string.IsNullOrWhiteSpace(company))
            {
                mutations.CustomerName = company.Trim();
                customer = mutations.CustomerName;
            }

            var subject = Value(target, LeadFields.Subject) as string;
            if (string.IsNullOrWhiteSpace(subject))
            {
                var product = Value(target, LeadFields.Product) as string;
                mutations.Subject = BuildSubject(product, customer);
            }

            return mutations;
        }

        public LeadMutations OnUpdate(Guid leadId, IDictionary<string, object> changes, LeadSnapshot before)
        {
            if (before == null)
            {
                throw new ArgumentNullException(nameof(before));
            }

            changes = changes ?? new Dictionary<string, object>();
            if (!TouchesGate(changes))
            {
                return new LeadMutations();
            }

            var after = before.Apply(changes);
            var errors = new List<string>();
            var mutations = new LeadMutations();
            var activityCount = -1;

            int Activities()
            {
                if (activityCount < 0)
                {
                    activityCount = _data.CountRelatedActivities(leadId);
                }

                return activityCount;
            }

            var stageChanged = changes.ContainsKey(LeadFields.Stage) && after.EffectiveStage != before.EffectiveStage;
            if (stageChanged)
            {
                if (after.EffectiveStage == LeadChoices.Converted)
                {
                    errors.Add("Use Qualify to convert a GO lead. The Converted stage is set by qualification.");
                }
                else if (!CanMove(before.EffectiveStage, after.EffectiveStage))
                {
                    errors.Add("The lead cannot move from " + StageName(before.EffectiveStage) + " to " + StageName(after.EffectiveStage) + ".");
                }
                else
                {
                    errors.AddRange(RequirementsFor(after, after.EffectiveStage, Activities));
                }
            }

            var becameGo = changes.ContainsKey(LeadFields.QualificationStatus)
                && after.QualificationStatus == LeadChoices.Go
                && before.QualificationStatus != LeadChoices.Go;
            var becameNoGo = changes.ContainsKey(LeadFields.QualificationStatus)
                && after.QualificationStatus == LeadChoices.NoGo
                && before.QualificationStatus != LeadChoices.NoGo;

            var generateProject = false;
            if (becameGo)
            {
                var destination = stageChanged ? after.EffectiveStage : before.EffectiveStage;
                if (destination != LeadChoices.Validation && destination != LeadChoices.Qualification)
                {
                    errors.Add("Mark the lead GO from Validation or Qualification, after the lead form is complete and an activity is on the timeline.");
                }

                errors.AddRange(IntakeGaps(after));
                if (Activities() < 1)
                {
                    errors.Add("Log a call, email, task, appointment, or note on the lead before marking it GO.");
                }

                if (!after.Temperature.HasValue)
                {
                    errors.Add("Set the lead status to Hot, Warm, or Cold.");
                }
                else if (after.Temperature == LeadChoices.Cold && string.IsNullOrWhiteSpace(after.QualificationNote))
                {
                    errors.Add("A Cold lead marked GO needs a qualification note.");
                }

                generateProject = true;
            }

            if (becameNoGo)
            {
                if (string.IsNullOrWhiteSpace(after.NoGoReason))
                {
                    errors.Add("Enter a NO GO reason. A NO GO lead does not get an Akesa Project Code or a tranche.");
                }
                else
                {
                    mutations.Stage = LeadChoices.Disqualified;
                }
            }

            if (!becameGo && !becameNoGo)
            {
                errors.AddRange(ClearedIntakeFields(changes, after));
            }

            if (errors.Count > 0)
            {
                throw new LeadGateException(errors);
            }

            if (generateProject)
            {
                if (!after.ProjectId.HasValue)
                {
                    var projectId = ResolveProject(after);
                    var sequence = _data.NextTrancheSequence(projectId);
                    var trancheId = _data.CreateTranche(new TrancheDraft
                    {
                        Name = TrancheName(sequence, after.LeadTime),
                        Code = TrancheCode(sequence),
                        Sequence = sequence,
                        ProjectId = projectId,
                        LeadId = leadId,
                        LeadTime = after.LeadTime
                    });
                    mutations.ProjectId = projectId;
                    mutations.TrancheId = trancheId;
                }

                if (after.EffectiveStage != LeadChoices.Qualification)
                {
                    mutations.Stage = LeadChoices.Qualification;
                }
            }

            return mutations;
        }

        public LeadMutations OnQualify(LeadSnapshot lead)
        {
            if (lead == null)
            {
                throw new ArgumentNullException(nameof(lead));
            }

            var errors = new List<string>();
            if (lead.QualificationStatus != LeadChoices.Go)
            {
                errors.Add("Only a GO lead can be qualified into an opportunity.");
            }

            if (!lead.ProjectId.HasValue || !lead.TrancheId.HasValue)
            {
                errors.Add("Mark the lead GO so an Akesa Project Code and tranche exist before qualifying.");
            }

            if (lead.EffectiveStage != LeadChoices.Qualification && lead.EffectiveStage != LeadChoices.Converted)
            {
                errors.Add("Move the lead to Qualification before qualifying it.");
            }

            if (errors.Count > 0)
            {
                throw new LeadGateException(errors);
            }

            return new LeadMutations { Stage = LeadChoices.Converted };
        }

        public OpportunityPatch OpportunityFields(LeadSnapshot lead)
        {
            return new OpportunityPatch
            {
                Product = lead.Product,
                Quantity = lead.Quantity,
                LeadTime = lead.LeadTime,
                SupplierName = lead.SupplierName,
                SupplierAccountNumber = lead.SupplierAccountNumber,
                LeadNumber = lead.LeadNumber,
                Country = lead.Country,
                CurrencyIsoCode = lead.CurrencyIsoCode,
                ProjectId = lead.ProjectId,
                TrancheId = lead.TrancheId
            };
        }

        public string BuildConversionNote(LeadSnapshot lead, string projectCode, string trancheCode, int activityCount)
        {
            var builder = new StringBuilder();
            builder.Append("Lead ").Append(string.IsNullOrWhiteSpace(lead.LeadNumber) ? lead.Id.ToString() : lead.LeadNumber);
            builder.Append(" qualified GO.");
            builder.AppendLine();
            builder.Append("Akesa Project ").Append(string.IsNullOrWhiteSpace(projectCode) ? "pending" : projectCode);
            builder.Append(", tranche ").Append(string.IsNullOrWhiteSpace(trancheCode) ? "pending" : trancheCode).Append(".");
            builder.AppendLine();
            builder.Append("Product ").Append(Blank(lead.Product));
            builder.Append(", quantity ").Append(lead.Quantity.HasValue ? lead.Quantity.Value.ToString("0.##", CultureInfo.InvariantCulture) : "blank");
            builder.Append(", lead time ").Append(lead.LeadTime.HasValue ? lead.LeadTime.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "blank").Append(".");
            builder.AppendLine();
            builder.Append("Customer ").Append(Blank(lead.CustomerName));
            if (!string.IsNullOrWhiteSpace(lead.CustomerAccountNumber))
            {
                builder.Append(" (").Append(lead.CustomerAccountNumber).Append(")");
            }

            builder.Append(". Supplier ").Append(Blank(lead.SupplierName));
            if (!string.IsNullOrWhiteSpace(lead.SupplierAccountNumber))
            {
                builder.Append(" (").Append(lead.SupplierAccountNumber).Append(")");
            }

            builder.Append(".");
            builder.AppendLine();
            builder.Append("Activities logged on the lead before conversion: ").Append(activityCount).Append(".");
            if (!string.IsNullOrWhiteSpace(lead.Country) || !string.IsNullOrWhiteSpace(lead.CurrencyIsoCode))
            {
                builder.AppendLine();
                builder.Append("Recorded");
                if (!string.IsNullOrWhiteSpace(lead.Country))
                {
                    builder.Append(" country ").Append(lead.Country);
                }

                if (!string.IsNullOrWhiteSpace(lead.CurrencyIsoCode))
                {
                    builder.Append(" currency ").Append(lead.CurrencyIsoCode);
                }

                builder.Append(".");
            }

            return builder.ToString();
        }

        public static string TrancheCode(int sequence)
        {
            return "R" + sequence.ToString(CultureInfo.InvariantCulture);
        }

        public static string TrancheName(int sequence, DateTime? leadTime)
        {
            var code = TrancheCode(sequence);
            if (!leadTime.HasValue)
            {
                return code;
            }

            return code + " · " + leadTime.Value.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        }

        private Guid ResolveProject(LeadSnapshot lead)
        {
            if (!string.IsNullOrWhiteSpace(lead.CustomerAccountNumber))
            {
                var existing = _data.FindOpenProjectByCustomerAccount(lead.CustomerAccountNumber.Trim());
                if (existing.HasValue)
                {
                    return existing.Value;
                }
            }

            return _data.CreateProject(new ProjectDraft
            {
                Name = string.IsNullOrWhiteSpace(lead.CustomerName) ? "Akesa Project" : lead.CustomerName.Trim(),
                CustomerName = lead.CustomerName,
                CustomerAccountNumber = string.IsNullOrWhiteSpace(lead.CustomerAccountNumber) ? null : lead.CustomerAccountNumber.Trim(),
                Country = lead.Country,
                CurrencyIsoCode = lead.CurrencyIsoCode
            });
        }

        private static IEnumerable<string> RequirementsFor(LeadSnapshot lead, int stage, Func<int> activities)
        {
            if (stage == LeadChoices.Validation || stage == LeadChoices.Qualification)
            {
                foreach (var gap in IntakeGaps(lead))
                {
                    yield return gap;
                }
            }

            if (stage == LeadChoices.Qualification && activities() < 1)
            {
                yield return "Log a call, email, task, appointment, or note before Qualification.";
            }
        }

        private static IEnumerable<string> IntakeGaps(LeadSnapshot lead)
        {
            if (string.IsNullOrWhiteSpace(lead.Product))
            {
                yield return "Product is required.";
            }

            if (!lead.Quantity.HasValue || lead.Quantity.Value <= 0)
            {
                yield return "Quantity must be greater than zero.";
            }

            if (!lead.LeadTime.HasValue)
            {
                yield return "Lead time is required.";
            }

            if (string.IsNullOrWhiteSpace(lead.SupplierName))
            {
                yield return "Supplier is required.";
            }

            if (string.IsNullOrWhiteSpace(lead.CustomerName))
            {
                yield return "Customer is required.";
            }
        }

        private static IEnumerable<string> ClearedIntakeFields(IDictionary<string, object> changes, LeadSnapshot after)
        {
            if (after.EffectiveStage == LeadChoices.Intake || after.EffectiveStage == LeadChoices.Disqualified)
            {
                yield break;
            }

            foreach (var gap in IntakeGaps(after))
            {
                if (Cleared(changes, gap))
                {
                    yield return gap;
                }
            }
        }

        private static bool Cleared(IDictionary<string, object> changes, string gap)
        {
            if (gap.StartsWith("Product", StringComparison.Ordinal))
            {
                return changes.ContainsKey(LeadFields.Product);
            }

            if (gap.StartsWith("Quantity", StringComparison.Ordinal))
            {
                return changes.ContainsKey(LeadFields.Quantity);
            }

            if (gap.StartsWith("Lead time", StringComparison.Ordinal))
            {
                return changes.ContainsKey(LeadFields.LeadTime);
            }

            if (gap.StartsWith("Supplier", StringComparison.Ordinal))
            {
                return changes.ContainsKey(LeadFields.SupplierName);
            }

            if (gap.StartsWith("Customer", StringComparison.Ordinal))
            {
                return changes.ContainsKey(LeadFields.CustomerName);
            }

            return false;
        }

        private static bool CanMove(int from, int to)
        {
            if (from == LeadChoices.Intake)
            {
                return to == LeadChoices.Validation;
            }

            if (from == LeadChoices.Validation)
            {
                return to == LeadChoices.Qualification;
            }

            if (from == LeadChoices.Qualification)
            {
                return to == LeadChoices.Validation || to == LeadChoices.Disqualified;
            }

            if (from == LeadChoices.Disqualified)
            {
                return to == LeadChoices.Qualification;
            }

            return false;
        }

        private static bool TouchesGate(IDictionary<string, object> changes)
        {
            return changes.ContainsKey(LeadFields.Stage)
                || changes.ContainsKey(LeadFields.QualificationStatus)
                || changes.ContainsKey(LeadFields.Product)
                || changes.ContainsKey(LeadFields.Quantity)
                || changes.ContainsKey(LeadFields.LeadTime)
                || changes.ContainsKey(LeadFields.SupplierName)
                || changes.ContainsKey(LeadFields.CustomerName)
                || changes.ContainsKey(LeadFields.Temperature)
                || changes.ContainsKey(LeadFields.QualificationNote)
                || changes.ContainsKey(LeadFields.NoGoReason);
        }

        private static string StageName(int stage)
        {
            if (stage == LeadChoices.Intake) return "Intake";
            if (stage == LeadChoices.Validation) return "Validation";
            if (stage == LeadChoices.Qualification) return "Qualification";
            if (stage == LeadChoices.Converted) return "Converted";
            if (stage == LeadChoices.Disqualified) return "Disqualified";
            return stage.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildSubject(string product, string customer)
        {
            var hasProduct = !string.IsNullOrWhiteSpace(product);
            var hasCustomer = !string.IsNullOrWhiteSpace(customer);
            if (hasProduct && hasCustomer)
            {
                return product.Trim() + " · " + customer.Trim();
            }

            if (hasCustomer)
            {
                return customer.Trim();
            }

            if (hasProduct)
            {
                return product.Trim();
            }

            return null;
        }

        private static object Value(IDictionary<string, object> attributes, string key)
        {
            return attributes.ContainsKey(key) ? attributes[key] : null;
        }

        private static string Blank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "blank" : value;
        }
    }
}
