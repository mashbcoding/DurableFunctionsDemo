using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace DurableFunctionsDemo.DurableOrchestration
{
    public static class SendGridOrchestration
    {
        [FunctionName("SendGridOrchestration")]
        public static async Task<string> RunOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            log = context.CreateReplaySafeLogger(log);

            log.LogInformation($"Starting orchestration with instanceId = {context.InstanceId}");

            //increment the request number in the durable entity
            var entityId = new EntityId(nameof(MyDurableEntity), "myDurableEntity");
            var entityProxy = context.CreateEntityProxy<IMyDurableEntity>(entityId);
            entityProxy.Increment();

            //get the new request number value
            var requestNumber = await entityProxy.GetRequestNumber();

            log.LogInformation($"Request number has been incremented to {requestNumber}");

            var approvalRequest = new ApprovalRequest
            {
                NumDataFiles = 3,
                OrchestrationId = context.InstanceId,
                RequestNumber = requestNumber
            };

            var unorderedDataFiles = await context.CallSubOrchestratorAsync<DataFile[]>(nameof(GenerateApprovalRequestOrchestrator), approvalRequest);

            await context.CallActivityAsync(nameof(SendGridActivities.RequestApproval), unorderedDataFiles);

            var approvalResult = await context.WaitForExternalEvent<string>("ApprovalResult");

            if (approvalResult == "Approved")
            {
                log.LogInformation($"The request for {context.InstanceId} was approved!");

                await context.CallSubOrchestratorAsync<DataFile[]>(nameof(CompleteApprovalOrchestrator), approvalRequest);
                
                return "Approved";
            }
            else
            {
                log.LogInformation($"The request for {context.InstanceId} was rejected!");

                var rejection = new Rejection
                {
                    OrchestrationId = context.InstanceId,
                    RequestNumber = requestNumber
                };

                await context.CallActivityAsync(nameof(SendGridActivities.ConfirmRejection), rejection);

                return "Rejected";
            }
        }

        [FunctionName(nameof(GenerateApprovalRequestOrchestrator))]
        public static async Task<DataFile[]> GenerateApprovalRequestOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var approvalRequest = context.GetInput<ApprovalRequest>();

            var fanOutTasks = new List<Task<DataFile>>();
            for (int i = 1; i <= approvalRequest.NumDataFiles; i++)
            {
                var unorderedDataFile = new DataFile
                {
                    FileName =  $"file-{i}.png",
                    OrchestrationId = approvalRequest.OrchestrationId,
                    RequestNumber = approvalRequest.RequestNumber
                };
                
                fanOutTasks.Add(context.CallActivityAsync<DataFile>(nameof(SendGridActivities.GenerateUnorderedDataSet), unorderedDataFile));
            }

            var unorderedDataFiles = await Task.WhenAll(fanOutTasks);

            return unorderedDataFiles;
        }

        [FunctionName(nameof(CompleteApprovalOrchestrator))]
        public static async Task CompleteApprovalOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var approvalRequest = context.GetInput<ApprovalRequest>();

            var fanOutTasks = new List<Task<DataFile>>();
            for (int i = 1; i <= approvalRequest.NumDataFiles; i++)
            {
                var orderedDataFile = new DataFile
                {
                    FileName =  $"file-{i}.png",
                    OrchestrationId = approvalRequest.OrchestrationId,
                    RequestNumber = approvalRequest.RequestNumber
                };

                fanOutTasks.Add(context.CallActivityAsync<DataFile>(nameof(SendGridActivities.GenerateOrderedDataSet), orderedDataFile));
            }

            var orderedDataFiles = await Task.WhenAll(fanOutTasks);

            await context.CallActivityAsync(nameof(SendGridActivities.ConfirmApproval), orderedDataFiles);
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

        [FunctionName("StartSingletonSendGridOrchestration")]
        public static async Task<HttpResponseMessage> HttpStartSingleton(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "Approval/{instanceId}")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            string instanceId,
            ILogger log)
        {
            var existingInstance = await starter.GetStatusAsync(instanceId);

            if (existingInstance == null
            || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Completed
            || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Failed
            || existingInstance.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
            {
                //string instanceId = await starter.StartNewAsync("SendGridOrchestration", null);   //"normally" you would start orchestration and return instanceId
                await starter.StartNewAsync("SendGridOrchestration", instanceId);
                log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

                return starter.CreateCheckStatusResponse(req, instanceId);
            }
            else
            {
                log.LogInformation($"Orchestration with ID '{instanceId}' already exists.");

                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent($"Orchestration with ID '{instanceId}' already exists."),
                };
            }
        }
    }
}