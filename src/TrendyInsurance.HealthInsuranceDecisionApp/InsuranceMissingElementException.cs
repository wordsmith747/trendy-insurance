using System;

namespace InsuranceDecisionApp
{
    public class InsuranceMissingElementException : Exception
    {
        public string MissingElementName { get; set; }

        public InsuranceMissingElementException(string elementName)
        {
            MissingElementName = elementName;
        }
    }
}