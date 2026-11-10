# Akesa CRM lead uplift

Chapter 1 of the CRM implementation proposal: the lead phase in Dynamics 365.

The current lead does not carry the Fisher-Davinci fields, and activity history is easy to lose when a lead becomes an opportunity. This solution adds that lead form, the stage gates, and the records a GO decision creates.

## What this adds

Lead columns, on the **Akesa Lead Validation** form:

| Field | Example |
| --- | --- |
| Lead ID | LED-001, assigned automatically |
| Product, quantity, lead time | ALIMTA, 500, June 2027 |
| Supplier and supplier account | Eli Lilly, SUP-001 |
| Customer and customer account | Fisher CH, CUS-001 |
| Lead status | Hot, Warm, or Cold |
| Lead qualification status | Pending, GO, or NO GO |
| Lead stage | Intake, Validation, Qualification, Converted, Disqualified |
| Country and currency | Optional. They are stored and copied forward. They do not open or close a gate. |

A GO decision creates:

- an **Akesa Project** with the next project code (`APC-001`)
- a **Tranche** on that project (`R1`, then `R2` for the next GO lead with the same customer account)

Qualifying the lead copies the product, quantity, lead time, supplier, project, and tranche onto the new opportunity and writes a timeline note there. The note includes how many activities were on the lead. A NO GO decision asks for a reason and does not create a project or a tranche.

The same solution includes the later tables from the process map, without their chapter behavior: sponsor program, work package (supply, service, freight), quote family, and quote version. Sponsor stays optional until that chapter backfills it.

## Stage gates

1. **Intake → Validation** requires product, a quantity above zero, lead time, supplier, and customer.
2. **Validation → Qualification** requires at least one call, email, task, appointment, or note on the lead timeline.
3. **GO** is allowed from Validation or Qualification, with a lead status. A Cold GO also needs a qualification note. GO moves the lead to Qualification and creates the project and tranche.
4. **NO GO** requires a reason and moves the lead to Disqualified.
5. **Qualify** is accepted only for a GO lead that already has a project and a tranche. Qualify sets the stage to Converted.

## Import

This environment is not connected to the Akesa Dynamics 365 tenant, so the solution is packed here and imported there.

1. Install the [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction).
2. From `akesa-crm/AkesaLeadUplift`, pack an unmanaged solution:

   ```bash
   pac solution pack --folder . --zipfile ../AkesaLeadUplift.zip --packagetype Unmanaged
   ```

3. In the Power Apps maker portal, import `AkesaLeadUplift.zip` into the development environment and publish all customizations.
4. On the Lead table, set **Akesa Lead Validation** as the form the sales app uses. On Opportunity, set **Akesa Opportunity**.
5. Build the plugin and register it with the Plug-in Registration Tool:

   ```bash
   dotnet build plugin/Akesa.Crm.LeadUplift.Dataverse/Akesa.Crm.LeadUplift.Dataverse.csproj -c Release
   ```

   Assembly: `plugin/Akesa.Crm.LeadUplift.Dataverse/bin/Release/net462/Akesa.Crm.LeadUplift.Plugins.dll`

   | Message | Entity | Stage | Notes |
   | --- | --- | --- | --- |
   | Create | lead | Pre-operation | Synchronous |
   | Update | lead | Pre-operation | Synchronous. Pre-image name `LeadUpliftPreImage`. Include the `akesa_` lead columns plus `subject` and `companyname`. |
   | QualifyLead | (none) | Pre-validation | Synchronous |
   | QualifyLead | (none) | Post-operation | Synchronous |

   Class: `Akesa.Crm.LeadUplift.LeadUpliftPlugin`

6. Give the sales security roles read and write on Akesa Project, Tranche, Sponsor Program, Work Package, Quote Family, and Quote Version.

## Check

`dotnet test akesa-crm/plugin/Akesa.Crm.LeadUplift.Tests` covers the Fisher-Davinci example: LED-style fields, Hot GO creating `R1`, a second lead on customer account `CUS-001` creating `R2`, Cold GO, NO GO, the activity gate, and a conversion note that does not depend on currency.
