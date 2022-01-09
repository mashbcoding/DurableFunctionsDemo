using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace DurableFunctionsDemo.DurableOrchestration
{
    public static class SendGridFunctions
    {
        [FunctionName("ApprovalCallback")]
        public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "ApprovalCallback/{id}")] HttpRequest req,
        [DurableClient] IDurableOrchestrationClient client,
        [Table("Approvals", "Approval", "{id}", Connection = "AzureWebJobsStorage")] Approval approval,
        ILogger log)
        {
            log.LogInformation("ApprovalCallback is being processed.");

            var result = req.GetQueryParameterDictionary()["result"];

            if (result == null)
                return new BadRequestObjectResult("Approval result not found in callback.");

            log.LogInformation($"Sending approval result of {result} to orchestration instance {approval.OrchestrationId}.");

            await client.RaiseEventAsync(approval.OrchestrationId, "ApprovalResult", result);

            return new OkResult();
        }
    }
}