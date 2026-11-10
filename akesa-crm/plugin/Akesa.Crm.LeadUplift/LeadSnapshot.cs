using System;
using System.Collections.Generic;
using System.Globalization;

namespace Akesa.Crm.LeadUplift
{
    public sealed class LeadSnapshot
    {
        public Guid Id { get; set; }
        public string Subject { get; set; }
        public string CompanyName { get; set; }
        public string LeadNumber { get; set; }
        public string Product { get; set; }
        public decimal? Quantity { get; set; }
        public DateTime? LeadTime { get; set; }
        public string SupplierName { get; set; }
        public string SupplierAccountNumber { get; set; }
        public string CustomerName { get; set; }
        public string CustomerAccountNumber { get; set; }
        public string Country { get; set; }
        public string CurrencyIsoCode { get; set; }
        public int? Temperature { get; set; }
        public int? QualificationStatus { get; set; }
        public int? Stage { get; set; }
        public string QualificationNote { get; set; }
        public string NoGoReason { get; set; }
        public Guid? ProjectId { get; set; }
        public Guid? TrancheId { get; set; }

        public int EffectiveStage
        {
            get { return Stage ?? LeadChoices.Intake; }
        }

        public static LeadSnapshot From(Guid id, IDictionary<string, object> attributes)
        {
            var snapshot = new LeadSnapshot { Id = id };
            if (attributes == null)
            {
                return snapshot;
            }

            snapshot.Subject = Text(attributes, LeadFields.Subject);
            snapshot.CompanyName = Text(attributes, LeadFields.CompanyName);
            snapshot.LeadNumber = Text(attributes, LeadFields.LeadNumber);
            snapshot.Product = Text(attributes, LeadFields.Product);
            snapshot.Quantity = Decimal(attributes, LeadFields.Quantity);
            snapshot.LeadTime = Date(attributes, LeadFields.LeadTime);
            snapshot.SupplierName = Text(attributes, LeadFields.SupplierName);
            snapshot.SupplierAccountNumber = Text(attributes, LeadFields.SupplierAccountNumber);
            snapshot.CustomerName = Text(attributes, LeadFields.CustomerName);
            snapshot.CustomerAccountNumber = Text(attributes, LeadFields.CustomerAccountNumber);
            snapshot.Country = Text(attributes, LeadFields.Country);
            snapshot.CurrencyIsoCode = Text(attributes, LeadFields.CurrencyIsoCode);
            snapshot.Temperature = Int(attributes, LeadFields.Temperature);
            snapshot.QualificationStatus = Int(attributes, LeadFields.QualificationStatus);
            snapshot.Stage = Int(attributes, LeadFields.Stage);
            snapshot.QualificationNote = Text(attributes, LeadFields.QualificationNote);
            snapshot.NoGoReason = Text(attributes, LeadFields.NoGoReason);
            snapshot.ProjectId = GuidOf(attributes, LeadFields.Project);
            snapshot.TrancheId = GuidOf(attributes, LeadFields.Tranche);
            return snapshot;
        }

        public LeadSnapshot Apply(IDictionary<string, object> changes)
        {
            var copy = new LeadSnapshot
            {
                Id = Id,
                Subject = Subject,
                CompanyName = CompanyName,
                LeadNumber = LeadNumber,
                Product = Product,
                Quantity = Quantity,
                LeadTime = LeadTime,
                SupplierName = SupplierName,
                SupplierAccountNumber = SupplierAccountNumber,
                CustomerName = CustomerName,
                CustomerAccountNumber = CustomerAccountNumber,
                Country = Country,
                CurrencyIsoCode = CurrencyIsoCode,
                Temperature = Temperature,
                QualificationStatus = QualificationStatus,
                Stage = Stage,
                QualificationNote = QualificationNote,
                NoGoReason = NoGoReason,
                ProjectId = ProjectId,
                TrancheId = TrancheId
            };

            if (changes == null)
            {
                return copy;
            }

            ApplyText(changes, LeadFields.Subject, value => copy.Subject = value);
            ApplyText(changes, LeadFields.CompanyName, value => copy.CompanyName = value);
            ApplyText(changes, LeadFields.LeadNumber, value => copy.LeadNumber = value);
            ApplyText(changes, LeadFields.Product, value => copy.Product = value);
            ApplyDecimal(changes, LeadFields.Quantity, value => copy.Quantity = value);
            ApplyDate(changes, LeadFields.LeadTime, value => copy.LeadTime = value);
            ApplyText(changes, LeadFields.SupplierName, value => copy.SupplierName = value);
            ApplyText(changes, LeadFields.SupplierAccountNumber, value => copy.SupplierAccountNumber = value);
            ApplyText(changes, LeadFields.CustomerName, value => copy.CustomerName = value);
            ApplyText(changes, LeadFields.CustomerAccountNumber, value => copy.CustomerAccountNumber = value);
            ApplyText(changes, LeadFields.Country, value => copy.Country = value);
            ApplyText(changes, LeadFields.CurrencyIsoCode, value => copy.CurrencyIsoCode = value);
            ApplyInt(changes, LeadFields.Temperature, value => copy.Temperature = value);
            ApplyInt(changes, LeadFields.QualificationStatus, value => copy.QualificationStatus = value);
            ApplyInt(changes, LeadFields.Stage, value => copy.Stage = value);
            ApplyText(changes, LeadFields.QualificationNote, value => copy.QualificationNote = value);
            ApplyText(changes, LeadFields.NoGoReason, value => copy.NoGoReason = value);
            ApplyGuid(changes, LeadFields.Project, value => copy.ProjectId = value);
            ApplyGuid(changes, LeadFields.Tranche, value => copy.TrancheId = value);
            return copy;
        }

        private static void ApplyText(IDictionary<string, object> changes, string key, Action<string> set)
        {
            if (changes.ContainsKey(key))
            {
                set(changes[key] as string);
            }
        }

        private static void ApplyInt(IDictionary<string, object> changes, string key, Action<int?> set)
        {
            if (changes.ContainsKey(key))
            {
                set(AsInt(changes[key]));
            }
        }

        private static void ApplyDecimal(IDictionary<string, object> changes, string key, Action<decimal?> set)
        {
            if (!changes.ContainsKey(key))
            {
                return;
            }

            var value = changes[key];
            if (value == null)
            {
                set(null);
                return;
            }

            set(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
        }

        private static void ApplyDate(IDictionary<string, object> changes, string key, Action<DateTime?> set)
        {
            if (!changes.ContainsKey(key))
            {
                return;
            }

            var value = changes[key];
            set(value is DateTime date ? date : (DateTime?)null);
        }

        private static void ApplyGuid(IDictionary<string, object> changes, string key, Action<Guid?> set)
        {
            if (!changes.ContainsKey(key))
            {
                return;
            }

            var value = changes[key];
            if (value is Guid id && id != Guid.Empty)
            {
                set(id);
                return;
            }

            set(null);
        }

        private static string Text(IDictionary<string, object> attributes, string key)
        {
            return attributes.ContainsKey(key) ? attributes[key] as string : null;
        }

        private static int? Int(IDictionary<string, object> attributes, string key)
        {
            return attributes.ContainsKey(key) ? AsInt(attributes[key]) : null;
        }

        private static int? AsInt(object value)
        {
            if (value == null)
            {
                return null;
            }

            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static decimal? Decimal(IDictionary<string, object> attributes, string key)
        {
            if (!attributes.ContainsKey(key) || attributes[key] == null)
            {
                return null;
            }

            return Convert.ToDecimal(attributes[key], CultureInfo.InvariantCulture);
        }

        private static DateTime? Date(IDictionary<string, object> attributes, string key)
        {
            if (!attributes.ContainsKey(key))
            {
                return null;
            }

            return attributes[key] is DateTime date ? date : (DateTime?)null;
        }

        private static Guid? GuidOf(IDictionary<string, object> attributes, string key)
        {
            if (!attributes.ContainsKey(key))
            {
                return null;
            }

            return attributes[key] is Guid id && id != Guid.Empty ? id : (Guid?)null;
        }
    }
}
