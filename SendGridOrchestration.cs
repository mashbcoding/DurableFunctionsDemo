using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace DurableFunctionsDemo.DurableOrchestration
{
    public static class SendGridOrchestration
    {
        [FunctionName("SendGridOrchestration")]
        public static async Task<string> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            log = context.CreateReplaySafeLogger(log);

            var fanOutTasks = new List<Task>();
            for (int i = 1; i <= 3; i++)
            {
                var unorderedDataFile = new UnorderedDataFile { FileName =  $"file-{i}.png", OrchestrationId = context.InstanceId };
                fanOutTasks.Add(context.CallActivityAsync("GenerateUnorderedDataSet", unorderedDataFile));
            }

            await Task.WhenAll(fanOutTasks);

            await context.CallActivityAsync("RequestApproval", context.InstanceId);

            var approvalResult = await context.WaitForExternalEvent<string>("ApprovalResult");

            if (approvalResult == "Approved")
            {
                //do approval suborchestration
                log.LogInformation($"The request for {context.InstanceId} was approved!");
                
                return "Approved";
            }
            else
            {
                //do rejection activity
                log.LogInformation($"The request for {context.InstanceId} was rejected!");
            }

            return "Rejected";
        }

        [FunctionName("StartSendGridOrchestration")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("SendGridOrchestration", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}