using System;
using System.Collections.Generic;

namespace InsuranceDecisionApp
{
    public class InsuranceApplicationRequest
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        public DateTime DateOfBirth { get; set; }

        public IEnumerable<string> Interests { get; set; }

        public IEnumerable<string> CurrentOccupations { get; set; }

        public IEnumerable<InsuranceApplicantAddress> HomeAddresses { get; set; }

        public string PreferredLanguage { get; set; }
    }

    public class InsuranceApplicantAddress
    {
        public string Line1 { get; set; }

        public string Line2 { get; set; }

        public string Line3 { get; set; }

        public string Postcode { get; set; }

        public string Region { get; set; } // state, province, territory

        public string Country { get; set; }

        public DateTime LivedFrom { get; set; }

        public DateTime? LivedTo { get; set; }
    }
}