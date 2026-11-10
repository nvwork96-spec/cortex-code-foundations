using System;
using System.Collections.Generic;
using Xunit;

namespace Akesa.Crm.LeadUplift.Tests
{
    public class LeadUpliftServiceTests
    {
        private readonly MemoryLeadData _data = new MemoryLeadData();
        private readonly LeadUpliftService _service;

        public LeadUpliftServiceTests()
        {
            _service = new LeadUpliftService(_data);
        }

        [Fact]
        public void Create_sets_intake_and_copies_the_company_onto_the_customer()
        {
            var mutations = _service.OnCreate(new Dictionary<string, object>
            {
                [LeadFields.CompanyName] = "Fisher CH",
                [LeadFields.Product] = "ALIMTA"
            });

            Assert.Equal(LeadChoices.Intake, mutations.Stage);
            Assert.Equal("Fisher CH", mutations.CustomerName);
            Assert.Equal("ALIMTA · Fisher CH", mutations.Subject);
        }

        [Fact]
        public void Intake_cannot_skip_to_qualification()
        {
            var error = Assert.Throws<LeadGateException>(() => _service.OnUpdate(
                Guid.NewGuid(),
                new Dictionary<string, object> { [LeadFields.Stage] = LeadChoices.Qualification },
                new LeadSnapshot()));

            Assert.Contains("cannot move", error.Message);
        }

        [Fact]
        public void Validation_requires_the_fisher_davinci_fields()
        {
            var error = Assert.Throws<LeadGateException>(() => _service.OnUpdate(
                Guid.NewGuid(),
                new Dictionary<string, object> { [LeadFields.Stage] = LeadChoices.Validation },
                new LeadSnapshot { Stage = LeadChoices.Intake, Product = "ALIMTA" }));

            Assert.Contains("Quantity must be greater than zero.", error.Errors);
            Assert.Contains("Lead time is required.", error.Errors);
            Assert.Contains("Supplier is required.", error.Errors);
            Assert.Contains("Customer is required.", error.Errors);
        }

        [Fact]
        public void Qualification_requires_an_activity()
        {
            var leadId = Guid.NewGuid();
            var error = Assert.Throws<LeadGateException>(() => _service.OnUpdate(
                leadId,
                new Dictionary<string, object> { [LeadFields.Stage] = LeadChoices.Qualification },
                ReadyLead(LeadChoices.Validation)));

            Assert.Contains("Log a call, email, task, appointment, or note before Qualification.", error.Errors);
            Assert.Empty(_data.Projects);
        }

        [Fact]
        public void Go_from_intake_does_not_create_a_project()
        {
            var error = Assert.Throws<LeadGateException>(() => _service.OnUpdate(
                Guid.NewGuid(),
                new Dictionary<string, object>
                {
                    [LeadFields.QualificationStatus] = LeadChoices.Go,
                    [LeadFields.Temperature] = LeadChoices.Hot
                },
                ReadyLead(LeadChoices.Intake)));

            Assert.Contains("Mark the lead GO from Validation or Qualification", error.Message);
            Assert.Empty(_data.Projects);
        }

        [Fact]
        public void Go_creates_the_project_and_first_tranche_for_a_hot_lead()
        {
            var leadId = Guid.NewGuid();
            _data.SetActivities(leadId, 1);
            var before = ReadyLead(LeadChoices.Validation);
            before.CustomerName = "Fisher CH";
            before.CustomerAccountNumber = "CUS-001";
            before.Product = "ALIMTA";
            before.Quantity = 500;
            before.LeadTime = new DateTime(2027, 6, 1);
            before.SupplierName = "Eli Lilly";

            var mutations = _service.OnUpdate(
                leadId,
                new Dictionary<string, object>
                {
                    [LeadFields.QualificationStatus] = LeadChoices.Go,
                    [LeadFields.Temperature] = LeadChoices.Hot
                },
                before);

            Assert.Equal(LeadChoices.Qualification, mutations.Stage);
            Assert.NotNull(mutations.ProjectId);
            Assert.NotNull(mutations.TrancheId);
            Assert.Equal("Fisher CH", _data.Projects[0].Name);
            Assert.Equal("CUS-001", _data.Projects[0].CustomerAccountNumber);
            Assert.Equal("R1", _data.Tranches[0].Code);
            Assert.Equal("R1 · Jun 2027", _data.Tranches[0].Name);
            Assert.Equal(leadId, _data.Tranches[0].LeadId);
        }

        [Fact]
        public void Second_go_lead_for_the_same_customer_account_gets_the_next_tranche()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            _data.SetActivities(first, 2);
            _data.SetActivities(second, 1);
            _service.OnUpdate(first, GoHot(), ReadyLead(LeadChoices.Qualification));

            var mutations = _service.OnUpdate(second, GoHot(), ReadyLead(LeadChoices.Qualification));

            Assert.Single(_data.Projects);
            Assert.Equal(2, _data.Tranches.Count);
            Assert.Equal("R2", _data.Tranches[1].Code);
            Assert.Equal("R2 · Dec 2027", _data.Tranches[1].Name);
            Assert.Equal(mutations.ProjectId, _data.Tranches[0].ProjectId);
        }

        [Fact]
        public void Cold_go_requires_a_qualification_note()
        {
            var leadId = Guid.NewGuid();
            _data.SetActivities(leadId, 1);
            var error = Assert.Throws<LeadGateException>(() => _service.OnUpdate(
                leadId,
                new Dictionary<string, object>
                {
                    [LeadFields.QualificationStatus] = LeadChoices.Go,
                    [LeadFields.Temperature] = LeadChoices.Cold
                },
                ReadyLead(LeadChoices.Qualification)));

            Assert.Contains("Cold lead marked GO needs a qualification note.", error.Message);
            Assert.Empty(_data.Projects);
        }

        [Fact]
        public void Cold_go_with_a_note_creates_the_tranche()
        {
            var leadId = Guid.NewGuid();
            _data.SetActivities(leadId, 1);
            var mutations = _service.OnUpdate(
                leadId,
                new Dictionary<string, object>
                {
                    [LeadFields.QualificationStatus] = LeadChoices.Go,
                    [LeadFields.Temperature] = LeadChoices.Cold,
                    [LeadFields.QualificationNote] = "Customer confirmed the June supply window."
                },
                ReadyLead(LeadChoices.Qualification));

            Assert.NotNull(mutations.TrancheId);
        }

        [Fact]
        public void No_go_requires_a_reason_and_does_not_create_a_project()
        {
            var error = Assert.Throws<LeadGateException>(() => _service.OnUpdate(
                Guid.NewGuid(),
                new Dictionary<string, object> { [LeadFields.QualificationStatus] = LeadChoices.NoGo },
                ReadyLead(LeadChoices.Qualification)));

            Assert.Contains("NO GO reason", error.Message);
            Assert.Empty(_data.Projects);
        }

        [Fact]
        public void No_go_with_a_reason_disqualifies_without_a_project_code()
        {
            var mutations = _service.OnUpdate(
                Guid.NewGuid(),
                new Dictionary<string, object>
                {
                    [LeadFields.QualificationStatus] = LeadChoices.NoGo,
                    [LeadFields.NoGoReason] = "Quantity is below the supply minimum."
                },
                ReadyLead(LeadChoices.Validation));

            Assert.Equal(LeadChoices.Disqualified, mutations.Stage);
            Assert.Null(mutations.ProjectId);
            Assert.Empty(_data.Projects);
        }

        [Fact]
        public void Blank_country_and_currency_do_not_block_go()
        {
            var leadId = Guid.NewGuid();
            _data.SetActivities(leadId, 1);
            var before = ReadyLead(LeadChoices.Qualification);
            before.Country = null;
            before.CurrencyIsoCode = null;

            var mutations = _service.OnUpdate(leadId, GoHot(), before);

            Assert.NotNull(mutations.ProjectId);
            Assert.Null(_data.Projects[0].Country);
            Assert.Null(_data.Projects[0].CurrencyIsoCode);
        }

        [Fact]
        public void An_unrelated_change_does_not_recheck_an_incomplete_intake_lead()
        {
            var mutations = _service.OnUpdate(
                Guid.NewGuid(),
                new Dictionary<string, object> { ["emailaddress1"] = "buyer@fisher.example" },
                new LeadSnapshot { Stage = LeadChoices.Intake });

            Assert.Null(mutations.Stage);
            Assert.Empty(_data.Projects);
        }

        [Fact]
        public void Clearing_product_after_intake_is_blocked()
        {
            var error = Assert.Throws<LeadGateException>(() => _service.OnUpdate(
                Guid.NewGuid(),
                new Dictionary<string, object> { [LeadFields.Product] = "" },
                ReadyLead(LeadChoices.Validation)));

            Assert.Contains("Product is required.", error.Errors);
        }

        [Fact]
        public void Qualify_rejects_a_lead_that_is_not_go()
        {
            var error = Assert.Throws<LeadGateException>(() => _service.OnQualify(ReadyLead(LeadChoices.Qualification)));
            Assert.Contains("Only a GO lead", error.Message);
        }

        [Fact]
        public void Qualify_marks_a_go_lead_converted()
        {
            var lead = ReadyLead(LeadChoices.Qualification);
            lead.QualificationStatus = LeadChoices.Go;
            lead.ProjectId = Guid.NewGuid();
            lead.TrancheId = Guid.NewGuid();
            lead.LeadNumber = "LED-001";

            var mutations = _service.OnQualify(lead);

            Assert.Equal(LeadChoices.Converted, mutations.Stage);
        }

        [Fact]
        public void Conversion_note_records_the_lead_the_tranche_and_the_activity_count()
        {
            var lead = ReadyLead(LeadChoices.Qualification);
            lead.LeadNumber = "LED-001";
            lead.Product = "ALIMTA";
            lead.Quantity = 500;
            lead.LeadTime = new DateTime(2027, 6, 1);
            lead.CustomerName = "Fisher CH";
            lead.CustomerAccountNumber = "CUS-001";
            lead.SupplierName = "Eli Lilly";
            lead.SupplierAccountNumber = "SUP-001";

            var note = _service.BuildConversionNote(lead, "APC-001", "R1", 3);

            Assert.Contains("LED-001", note);
            Assert.Contains("APC-001", note);
            Assert.Contains("tranche R1", note);
            Assert.Contains("ALIMTA", note);
            Assert.Contains("500", note);
            Assert.Contains("2027-06-01", note);
            Assert.Contains("Fisher CH (CUS-001)", note);
            Assert.Contains("Eli Lilly (SUP-001)", note);
            Assert.Contains("Activities logged on the lead before conversion: 3", note);
            Assert.DoesNotContain("currency", note);
        }

        private static Dictionary<string, object> GoHot()
        {
            return new Dictionary<string, object>
            {
                [LeadFields.QualificationStatus] = LeadChoices.Go,
                [LeadFields.Temperature] = LeadChoices.Hot
            };
        }

        private static LeadSnapshot ReadyLead(int stage)
        {
            return new LeadSnapshot
            {
                Stage = stage,
                Product = "ALIMTA",
                Quantity = 500,
                LeadTime = new DateTime(2027, 12, 1),
                SupplierName = "Eli Lilly",
                SupplierAccountNumber = "SUP-001",
                CustomerName = "Fisher CH",
                CustomerAccountNumber = "CUS-001"
            };
        }
    }
}
