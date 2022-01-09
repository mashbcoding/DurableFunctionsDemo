using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using SendGrid.Helpers.Mail;
using System;

namespace DurableFunctionsDemo.DurableOrchestration
{
    public static class SendGridActivities
    {
        [FunctionName("RequestApproval")]
        public static void RequestApproval([ActivityTrigger] string orchestrationId,
        [SendGrid(ApiKey = "SendGridApiKey")] out SendGridMessage sendGridMessage,
        [Table("Approvals", "AzureWebJobsStorage")] out Approval approval,
        ILogger log)
        {
            log.LogInformation($"Preparing approval request for {orchestrationId}.");
            
            var approvalCode = Guid.NewGuid().ToString("N");
            approval = new Approval
            {
                PartitionKey = "Approval",
                RowKey = approvalCode,
                OrchestrationId = orchestrationId
            };

            var approverEmailAddress = new EmailAddress(Environment.GetEnvironmentVariable("ApproverEmailAddress"));
            var senderEmailAddress = new EmailAddress(Environment.GetEnvironmentVariable("SenderEmailAddress"));
            var sendgridTemplateId = Environment.GetEnvironmentVariable("SendGridTemplateId");

            var hostBaseUrl = Environment.GetEnvironmentVariable("HostBaseUrl");

            var callbackFunctionUrl = $"{hostBaseUrl}/api/ApprovalCallback/{approvalCode}";
            var approvedUrl = callbackFunctionUrl + "?result=Approved";
            var rejectedUrl = callbackFunctionUrl + "?result=Rejected";

            sendGridMessage = new SendGridMessage()
            {
                Subject = "A new request is awaiting your approval",
                From = senderEmailAddress,
                TemplateId = sendgridTemplateId
            };

            sendGridMessage.AddTo(approverEmailAddress);
            sendGridMessage.SetTemplateData(
                new
                {
                    ApprovedUrl = approvedUrl,
                    RejectedUrl = rejectedUrl
                }
            );

            log.LogInformation($"Sent approval request for {orchestrationId}.");
        }
    }
}