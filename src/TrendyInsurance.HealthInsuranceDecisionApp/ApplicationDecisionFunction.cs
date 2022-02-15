using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace InsuranceDecisionApp
{
    public class ApplicationDecisionFunction
    {
        private readonly TranslatorOptions _translatorOptions;
        private readonly JsonSerializerOptions _jsonSerializerOptions;

        public ApplicationDecisionFunction(IOptions<TranslatorOptions> translatorOptions, JsonSerializerOptions jsonSerializerOptions)
        {
            _translatorOptions = translatorOptions.Value;
            _jsonSerializerOptions = jsonSerializerOptions;
        }

        [FunctionName("ProcessApplication")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            // get the request body as a string that contains the JSON representation of an insurance application
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            // write a log of the incoming request to Application Insights
            log.LogInformation($"Insurance application request received. => {requestBody}");

            // deserialise with JSON options to allow camel case JSON element names
            var application = JsonSerializer.Deserialize<InsuranceApplicationRequest>(requestBody, _jsonSerializerOptions);

            try
            {
                // validate that required elements of th insurance application are present
                // and reject the request if anything required is missing
                if (string.IsNullOrWhiteSpace(application.FirstName))
                {
                    throw new InsuranceMissingElementException(nameof(application.FirstName));
                }

                if (string.IsNullOrWhiteSpace(application.LastName))
                {
                    throw new InsuranceMissingElementException(nameof(application.LastName));
                }

                if (application.DateOfBirth == DateTime.MinValue) // date was not provided in the request
                {
                    throw new InsuranceMissingElementException(nameof(application.DateOfBirth));
                }

                // interests are not supplied, not even an empty array
                if (application.Interests == null)
                {
                    throw new InsuranceMissingElementException(nameof(application.Interests));
                }

                // do not allow an empty array to be provided in the request
                if (!application.Interests.Any())
                {
                    throw new InsuranceInvalidRequestException($"At least one interest in the {nameof(application.Interests)} array must be supplied.");
                }
            }
            catch (InsuranceMissingElementException ex)
            {
                log.LogInformation($"Required request element is missing: {ex.Message}");

                return new BadRequestObjectResult(
                    new
                    {
                        errorMessage = "A required element is missing",
                        elementName = ex.MissingElementName
                    });
            }
            catch (InsuranceInvalidRequestException ex)
            {
                log.LogInformation($"Input validation caused processing to be stopped: {ex.Message}");

                return new BadRequestObjectResult(
                    new
                    {
                        errorMessage = ex.Message
                    });
            }

            var riskScore = 0;

            if (application.Interests.Any(i => string.Equals(i, "skiing", StringComparison.OrdinalIgnoreCase)))
            {
                // skiing is risky
                riskScore += 50;
            }

            if (application.Interests.Any(i => string.Equals(i, "swimming", StringComparison.OrdinalIgnoreCase)))
            {
                // swimming is very healthy, so risk is reduced
                riskScore -= 20;
            }

            // if a current address is in Australia, do not approve the application
            if (application.HomeAddresses?.Any(a => a.LivedTo == null && string.Equals(a.Country, "australia", StringComparison.OrdinalIgnoreCase)) == true)
            {
                // animals are too dangerous
                riskScore += 45;
            }

            var decision = new InsuranceApplicationDecision
            {
                IsApproved = riskScore < 40
            };

            string textToTranslate;

            if (decision.IsApproved)
            {
                textToTranslate = $"Congratulations <span translate=\"no\">{application.FirstName} {application.LastName}</span>, your application has been approved. Please get in touch with your local branch in order to proceed with becoming a new member. We look forward to build a long-lasting relationship with you for your financial future. You are in good hands.";
            }
            else
            {
                textToTranslate = "At this time we are unfortunately unable to offer you an insurance policy. We wish you all the very best with finding a provider who can support your requirements.";
            }

            string translatedOutcomeText;

            if (application.PreferredLanguage == "en")
            {
                // skip calling the external translation service since the outcome
                // is already written in English
                translatedOutcomeText = textToTranslate;
            }
            else
            {
                try
                {
                    translatedOutcomeText = await TranslateTextAsync(textToTranslate, application.PreferredLanguage);
                }
                catch (Exception ex)
                {
                    translatedOutcomeText = textToTranslate;
                    log.LogError(ex.Message, ex);
                }
            }

            // remove the HTML tags used to mark up content to not be translated
            decision.OutcomeDescription = Regex.Replace(translatedOutcomeText, "<.*?>", string.Empty); ;

            log.LogInformation($"Application approved => {decision.IsApproved}");
            log.LogInformation($"Application outcome full text => {decision.OutcomeDescription}");

            return new OkObjectResult(decision);
        }

        private async Task<string> TranslateTextAsync(string textTotranslate, string targetLanguageSpecifier)
        {
            // https://docs.microsoft.com/en-us/azure/cognitive-services/translator/reference/v3-0-translate

            // the subscription key, endpoint URL and the location are all values that can be copied from the Azure portal
            string subscriptionKey = _translatorOptions.SubscriptionKey;
            string endpoint = _translatorOptions.Endpoint;
            string location = _translatorOptions.Location;

            // set the content of the translation input as "HTML" in order to honour <span translate="no"> tags
            string route = $"/translate?api-version=3.0&from=en&to={targetLanguageSpecifier}&textType=html";

            var body = new object[] { new { Text = textTotranslate } };

            // create the JSON part of the request
            var translationRequestBody = JsonSerializer.Serialize(body);

            using var client = new HttpClient();
            using var request = new HttpRequestMessage();

            request.Method = HttpMethod.Post; // HTTP verb
            request.RequestUri = new Uri(endpoint + route);

            request.Content = new StringContent(translationRequestBody, Encoding.UTF8, MediaTypeNames.Application.Json);

            // add headers as required by the translation service in Azure
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
            request.Headers.Add("Ocp-Apim-Subscription-Region", location);

            // send the request off for translation
            var response = await client.SendAsync(request).ConfigureAwait(false);

            string result;

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var jsonObjects = JsonSerializer.Deserialize<JsonObject[]>(jsonResponse);

                // try to get the first available translation from the results message
                // only one target language is set so only one translation would be provided
                result = jsonObjects[0]["translations"][0]["text"].GetValue<string>();
            }
            else
            {
                throw new InvalidOperationException("The response from the translation service was not OK.");
            }

            return result;
        }
    }
}