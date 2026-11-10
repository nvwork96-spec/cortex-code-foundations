using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Akesa.Crm.LeadUplift
{
    /// <summary>
    /// Register this class on lead Create (Pre-operation), lead Update (Pre-operation,
    /// pre-image name LeadUpliftPreImage), and QualifyLead (Pre-validation and Post-operation).
    /// </summary>
    public sealed class LeadUpliftPlugin : IPlugin
    {
        public const string PreImageName = "LeadUpliftPreImage";

        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                throw new InvalidPluginExecutionException("serviceProvider");
            }

            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var service = factory.CreateOrganizationService(context.UserId);
            var uplift = new LeadUpliftService(new DataverseLeadData(service));

            try
            {
                if (IsLeadMessage(context, "Create"))
                {
                    OnCreate(context, uplift);
                    return;
                }

                if (IsLeadMessage(context, "Update"))
                {
                    if (context.Depth > 1)
                    {
                        return;
                    }

                    OnUpdate(context, uplift);
                    return;
                }

                if (string.Equals(context.MessageName, "QualifyLead", StringComparison.OrdinalIgnoreCase))
                {
                    if (context.Stage == 10)
                    {
                        OnQualifyPre(context, service, uplift);
                    }
                    else if (context.Stage == 40)
                    {
                        OnQualifyPost(context, service, tracing, uplift);
                    }
                }
            }
            catch (LeadGateException exception)
            {
                throw new InvalidPluginExecutionException(exception.Message);
            }
        }

        private static void OnCreate(IPluginExecutionContext context, LeadUpliftService uplift)
        {
            var target = Target(context);
            Apply(target, uplift.OnCreate(Read(target)));
        }

        private static void OnUpdate(IPluginExecutionContext context, LeadUpliftService uplift)
        {
            if (!context.PreEntityImages.Contains(PreImageName))
            {
                throw new InvalidPluginExecutionException(
                    "Lead update step is missing pre-image " + PreImageName + ".");
            }

            var target = Target(context);
            var before = LeadSnapshot.From(target.Id, Read(context.PreEntityImages[PreImageName]));
            Apply(target, uplift.OnUpdate(target.Id, Read(target), before));
        }

        private static void OnQualifyPre(IPluginExecutionContext context, IOrganizationService service, LeadUpliftService uplift)
        {
            var leadId = LeadId(context);
            var lead = service.Retrieve("lead", leadId, new ColumnSet(LeadColumns()));
            var snapshot = LeadSnapshot.From(leadId, Read(lead));
            var mutations = uplift.OnQualify(snapshot);
            if (!mutations.Stage.HasValue)
            {
                return;
            }

            var update = new Entity("lead", leadId);
            Apply(update, mutations);
            service.Update(update);
        }

        private static void OnQualifyPost(
            IPluginExecutionContext context,
            IOrganizationService service,
            ITracingService tracing,
            LeadUpliftService uplift)
        {
            var opportunityId = CreatedOpportunity(context);
            if (!opportunityId.HasValue)
            {
                tracing.Trace("QualifyLead did not create an opportunity.");
                return;
            }

            var leadId = LeadId(context);
            var data = new DataverseLeadData(service);
            var lead = service.Retrieve("lead", leadId, new ColumnSet(LeadColumns()));
            var snapshot = LeadSnapshot.From(leadId, Read(lead));
            var patch = uplift.OpportunityFields(snapshot);
            var opportunity = new Entity("opportunity", opportunityId.Value);
            Set(opportunity, LeadFields.Product, patch.Product);
            Set(opportunity, LeadFields.SupplierName, patch.SupplierName);
            Set(opportunity, LeadFields.SupplierAccountNumber, patch.SupplierAccountNumber);
            Set(opportunity, LeadFields.LeadNumber, patch.LeadNumber);
            Set(opportunity, LeadFields.Country, patch.Country);
            Set(opportunity, LeadFields.CurrencyIsoCode, patch.CurrencyIsoCode);
            if (patch.Quantity.HasValue)
            {
                opportunity[LeadFields.Quantity] = patch.Quantity.Value;
            }

            if (patch.LeadTime.HasValue)
            {
                opportunity[LeadFields.LeadTime] = patch.LeadTime.Value;
            }

            if (patch.ProjectId.HasValue)
            {
                opportunity[LeadFields.Project] = new EntityReference("akesa_project", patch.ProjectId.Value);
            }

            if (patch.TrancheId.HasValue)
            {
                opportunity[LeadFields.Tranche] = new EntityReference("akesa_tranche", patch.TrancheId.Value);
            }

            service.Update(opportunity);

            if (snapshot.TrancheId.HasValue)
            {
                var tranche = new Entity("akesa_tranche", snapshot.TrancheId.Value);
                tranche["akesa_opportunity"] = new EntityReference("opportunity", opportunityId.Value);
                service.Update(tranche);
            }

            var projectCode = snapshot.ProjectId.HasValue ? data.GetProjectCode(snapshot.ProjectId.Value) : null;
            var trancheCode = snapshot.TrancheId.HasValue ? data.GetTrancheCode(snapshot.TrancheId.Value) : null;
            var note = new Entity("annotation");
            note["subject"] = "Lead converted";
            note["notetext"] = uplift.BuildConversionNote(
                snapshot,
                projectCode,
                trancheCode,
                data.CountRelatedActivities(leadId));
            note["objectid"] = new EntityReference("opportunity", opportunityId.Value);
            service.Create(note);
        }

        private static bool IsLeadMessage(IPluginExecutionContext context, string message)
        {
            return string.Equals(context.MessageName, message, StringComparison.OrdinalIgnoreCase)
                && string.Equals(context.PrimaryEntityName, "lead", StringComparison.OrdinalIgnoreCase);
        }

        private static Entity Target(IPluginExecutionContext context)
        {
            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity target))
            {
                throw new InvalidPluginExecutionException("Lead plugin target is missing.");
            }

            return target;
        }

        private static Guid LeadId(IPluginExecutionContext context)
        {
            if (context.InputParameters.Contains("LeadId") && context.InputParameters["LeadId"] is EntityReference reference)
            {
                return reference.Id;
            }

            throw new InvalidPluginExecutionException("QualifyLead did not include a lead id.");
        }

        private static Guid? CreatedOpportunity(IPluginExecutionContext context)
        {
            if (!context.OutputParameters.Contains("CreatedEntities")
                || !(context.OutputParameters["CreatedEntities"] is EntityReferenceCollection created))
            {
                return null;
            }

            foreach (var reference in created)
            {
                if (string.Equals(reference.LogicalName, "opportunity", StringComparison.OrdinalIgnoreCase))
                {
                    return reference.Id;
                }
            }

            return null;
        }

        private static void Apply(Entity target, LeadMutations mutations)
        {
            if (mutations == null)
            {
                return;
            }

            if (mutations.Stage.HasValue)
            {
                target[LeadFields.Stage] = new OptionSetValue(mutations.Stage.Value);
            }

            if (mutations.ProjectId.HasValue)
            {
                target[LeadFields.Project] = new EntityReference("akesa_project", mutations.ProjectId.Value);
            }

            if (mutations.TrancheId.HasValue)
            {
                target[LeadFields.Tranche] = new EntityReference("akesa_tranche", mutations.TrancheId.Value);
            }

            if (mutations.Subject != null)
            {
                target[LeadFields.Subject] = mutations.Subject;
            }

            if (mutations.CustomerName != null)
            {
                target[LeadFields.CustomerName] = mutations.CustomerName;
            }
        }

        public static Dictionary<string, object> Read(Entity entity)
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (entity == null)
            {
                return values;
            }

            foreach (var attribute in entity.Attributes)
            {
                values[attribute.Key] = Unwrap(attribute.Value);
            }

            return values;
        }

        private static object Unwrap(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is OptionSetValue option)
            {
                return option.Value;
            }

            if (value is EntityReference reference)
            {
                return reference.Id;
            }

            if (value is Money money)
            {
                return money.Value;
            }

            if (value is AliasedValue aliased)
            {
                return Unwrap(aliased.Value);
            }

            return value;
        }

        private static void Set(Entity entity, string attribute, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                entity[attribute] = value;
            }
        }

        private static string[] LeadColumns()
        {
            return new[]
            {
                LeadFields.Subject,
                LeadFields.CompanyName,
                LeadFields.LeadNumber,
                LeadFields.Product,
                LeadFields.Quantity,
                LeadFields.LeadTime,
                LeadFields.SupplierName,
                LeadFields.SupplierAccountNumber,
                LeadFields.CustomerName,
                LeadFields.CustomerAccountNumber,
                LeadFields.Country,
                LeadFields.CurrencyIsoCode,
                LeadFields.Temperature,
                LeadFields.QualificationStatus,
                LeadFields.Stage,
                LeadFields.QualificationNote,
                LeadFields.NoGoReason,
                LeadFields.Project,
                LeadFields.Tranche
            };
        }
    }
}
