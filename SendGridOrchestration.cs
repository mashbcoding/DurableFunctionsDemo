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

            var approvalRequestOrchestration = new ApprovalRequestOrchestration { NumDataFiles = 3, OrchestrationId = context.InstanceId };

            var unorderedDataFiles = await context.CallSubOrchestratorAsync<DataFile[]>("GenerateApprovalRequestOrchestrator", approvalRequestOrchestration);

            await context.CallActivityAsync("RequestApproval", unorderedDataFiles);

            var approvalResult = await context.WaitForExternalEvent<string>("ApprovalResult");

            if (approvalResult == "Approved")
            {
                //do approval suborchestration
                log.LogInformation($"The request for {context.InstanceId} was approved!");

                await context.CallSubOrchestratorAsync<DataFile[]>("CompleteApprovalOrchestrator", approvalRequestOrchestration);
                
                return "Approved";
            }
            else
            {
                //do rejection activity
                log.LogInformation($"The request for {context.InstanceId} was rejected!");
            }

            return "Rejected";
        }

        [FunctionName("GenerateApprovalRequestOrchestrator")]
        public static async Task<DataFile[]> GenerateApprovalRequestOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context
        )
        {
            var approvalRequestOrchestration = context.GetInput<ApprovalRequestOrchestration>();

            var fanOutTasks = new List<Task<DataFile>>();
            for (int i = 1; i <= approvalRequestOrchestration.NumDataFiles; i++)
            {
                var unorderedDataFile = new DataFile { FileName =  $"file-{i}.png", OrchestrationId = approvalRequestOrchestration.OrchestrationId };
                fanOutTasks.Add(context.CallActivityAsync<DataFile>("GenerateUnorderedDataSet", unorderedDataFile));
            }

            var unorderedDataFiles = await Task.WhenAll(fanOutTasks);

            return unorderedDataFiles;
        }

        [FunctionName("CompleteApprovalOrchestrator")]
        public static async Task CompleteApprovalOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context
        )
        {
            var approvalRequestOrchestration = context.GetInput<ApprovalRequestOrchestration>();

            var fanOutTasks = new List<Task<DataFile>>();
            for (int i = 1; i <= approvalRequestOrchestration.NumDataFiles; i++)
            {
                var orderedDataFile = new DataFile { FileName =  $"file-{i}.png", OrchestrationId = approvalRequestOrchestration.OrchestrationId };
                fanOutTasks.Add(context.CallActivityAsync<DataFile>("GenerateOrderedDataSet", orderedDataFile));
            }

            await Task.WhenAll(fanOutTasks);
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