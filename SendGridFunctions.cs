using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace DurableFunctionsDemo.DurableOrchestration
{
    public static class SendGridFunctions
    {
        [FunctionName("ApprovalCallback")]
        public static async Task<IActionResult> RunApprovalCallback(
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

        [FunctionName("IncrementDurableEntity")]
        public static async Task<IActionResult> RunIncrementDurableEntity(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "IncrementDurableEntity")] HttpRequest req,
        [DurableClient] IDurableClient client,
        ILogger log)
        {
            log.LogInformation("IncrementDurableEntity is being processed.");

            //read the current state of the durable entity
            var entityId = new EntityId(nameof(MyDurableEntity), "myDurableEntity");
            var myDurableEntity = await client.ReadEntityStateAsync<MyDurableEntity>(entityId);
            var currentValue = myDurableEntity.EntityState?.RequestNumber ?? 0;

            log.LogInformation($"Current durable entity request number is {currentValue}.");

            //call the increment function on the entity
            await client.SignalEntityAsync<IMyDurableEntity>(entityId, entity => entity.Increment());

            log.LogInformation($"Durable entity value has been incremented.");

            ////unfortunately we can't read the updated entity state (after increment) in the same execution
            //myDurableEntity = await client.ReadEntityStateAsync<MyDurableEntity>(entityId);

            //so this will return the state prior to increment
            return new OkObjectResult(myDurableEntity.EntityState);
        }
    }
}