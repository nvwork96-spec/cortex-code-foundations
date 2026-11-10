namespace Akesa.Crm.LeadUplift
{
    /// <summary>
    /// Option values on the Akesa lead columns. They match the solution metadata.
    /// </summary>
    public static class LeadChoices
    {
        public const int Hot = 845200000;
        public const int Warm = 845200001;
        public const int Cold = 845200002;

        public const int Pending = 845200010;
        public const int Go = 845200011;
        public const int NoGo = 845200012;

        public const int Intake = 845200020;
        public const int Validation = 845200021;
        public const int Qualification = 845200022;
        public const int Converted = 845200023;
        public const int Disqualified = 845200024;
    }

    public static class LeadFields
    {
        public const string LeadNumber = "akesa_leadnumber";
        public const string Product = "akesa_product";
        public const string Quantity = "akesa_quantity";
        public const string LeadTime = "akesa_leadtime";
        public const string SupplierName = "akesa_suppliername";
        public const string SupplierAccountNumber = "akesa_supplieraccountnumber";
        public const string CustomerName = "akesa_customername";
        public const string CustomerAccountNumber = "akesa_customeraccountnumber";
        public const string Country = "akesa_country";
        public const string CurrencyIsoCode = "akesa_currencyisocode";
        public const string Temperature = "akesa_temperature";
        public const string QualificationStatus = "akesa_qualificationstatus";
        public const string Stage = "akesa_stage";
        public const string QualificationNote = "akesa_qualificationnote";
        public const string NoGoReason = "akesa_nogoreason";
        public const string Project = "akesa_project";
        public const string Tranche = "akesa_tranche";
        public const string Subject = "subject";
        public const string CompanyName = "companyname";
    }
}
