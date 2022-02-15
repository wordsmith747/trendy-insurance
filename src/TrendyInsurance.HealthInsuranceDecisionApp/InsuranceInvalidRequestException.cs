using System;

namespace InsuranceDecisionApp
{
    public class InsuranceInvalidRequestException : Exception
    {
        public InsuranceInvalidRequestException(string message) : base(message)
        { }
    }
}