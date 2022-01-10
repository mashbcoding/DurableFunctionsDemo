using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using SendGrid.Helpers.Mail;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using System.Threading.Tasks;

namespace DurableFunctionsDemo.DurableOrchestration
{
    public static class SendGridActivities
    {
        [FunctionName("GenerateUnorderedDataSet")]
        public static async Task GenerateUnorderedDataSet([ActivityTrigger] UnorderedDataFile dataFile,
        IBinder binder,
        ILogger log)
        {
            log.LogInformation($"Preparing unordered data file for {dataFile.OrchestrationId}-{dataFile.FileName}");

            var random = new Random();

            using (var newImage = new Image<Rgba32>(Constants.ImageWidth, Constants.ImageHeight))
            {
                for (int w = 0; w < Constants.ImageWidth; w++)
                    for (int h = 0; h < Constants.ImageHeight; h++)
                        newImage[w,h] = Constants.Colors[random.Next(Constants.Colors.Length)];

                using (var blobStream = await binder.BindAsync<Stream>(new BlobAttribute($"attachments/{dataFile.OrchestrationId}/{dataFile.FileName}", FileAccess.Write)))
                {
                    await newImage.SaveAsync(blobStream, new PngEncoder());
                }
            }
        }
        
        [FunctionName("RequestApproval")]
        public static void RequestApproval([ActivityTrigger] string orchestrationId,
        [SendGrid(ApiKey = "SendGridApiKey")] out SendGridMessage sendGridMessage,
        [Table("Approvals", "AzureWebJobsStorage")] out Approval approval,
        IBinder binder,
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