using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using SendGrid.Helpers.Mail;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DurableFunctionsDemo.DurableOrchestration
{
    public static class SendGridActivities
    {
        [FunctionName("GenerateUnorderedDataSet")]
        public static async Task<DataFile> GenerateUnorderedDataSet([ActivityTrigger] DataFile dataFile,
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

                using (var blobStream = await binder.BindAsync<Stream>(new BlobAttribute($"attachments/{dataFile.OrchestrationId}/unordered/{dataFile.FileName}", FileAccess.Write)))
                {
                    await newImage.SaveAsync(blobStream, new PngEncoder());
                }
            }

            return dataFile;
        }

        [FunctionName("GenerateOrderedDataSet")]
        public static async Task<DataFile> GenerateOrderedDataSet([ActivityTrigger] DataFile dataFile,
        IBinder binder,
        ILogger log)
        {
            log.LogInformation($"Preparing ordered data file for {dataFile.OrchestrationId}-{dataFile.FileName}");

            var colorCounts = Constants.Colors.ToDictionary(color => color, count => 0);

            using (var unorderedBlobStream = await binder.BindAsync<Stream>(new BlobAttribute($"attachments/{dataFile.OrchestrationId}/unordered/{dataFile.FileName}", FileAccess.Read)))
            {
                using (var distortedImage = Image.Load<Rgba32>(unorderedBlobStream))
                {
                    for (int w = 0; w < distortedImage.Width; w++)
                        for (int h = 0; h < distortedImage.Height; h++)
                            colorCounts[distortedImage[w,h]]++;
                }
            }

            var colorQueue = new Queue<Color>(colorCounts.Select(kvp => Enumerable.Repeat(kvp.Key, kvp.Value)).SelectMany(color => color));

            using (var orderedImage = new Image<Rgba32>(Constants.ImageWidth, Constants.ImageHeight))
            {
                for (int h = 0; h < Constants.ImageHeight; h++)
                    for (int w = 0; w < Constants.ImageWidth; w++)
                        orderedImage[w,h] = colorQueue.Dequeue();

                using (var blobStream = await binder.BindAsync<Stream>(new BlobAttribute($"attachments/{dataFile.OrchestrationId}/ordered/{dataFile.FileName}", FileAccess.Write)))
                {
                    await orderedImage.SaveAsync(blobStream, new PngEncoder());
                }
            }

            return dataFile;
        }
        
        [FunctionName("RequestApproval")]
        public static async Task RequestApproval([ActivityTrigger] DataFile[] dataFiles,
        [SendGrid(ApiKey = "SendGridApiKey")] IAsyncCollector<SendGridMessage> sendGridMessage,
        [Table("Approvals", "AzureWebJobsStorage")] IAsyncCollector<Approval> approval,
        IBinder binder,
        ILogger log)
        {
            var orchestrationId = dataFiles[0].OrchestrationId;

            log.LogInformation($"Preparing approval request for {orchestrationId}.");
            
            var approvalCode = Guid.NewGuid().ToString("N");
            await approval.AddAsync(new Approval
            {
                PartitionKey = "Approval",
                RowKey = approvalCode,
                OrchestrationId = orchestrationId
            });

            var approverEmailAddress = new EmailAddress(Environment.GetEnvironmentVariable("ApproverEmailAddress"));
            var senderEmailAddress = new EmailAddress(Environment.GetEnvironmentVariable("SenderEmailAddress"));
            var sendgridTemplateId = Environment.GetEnvironmentVariable("SendGridTemplateId");

            var hostBaseUrl = Environment.GetEnvironmentVariable("HostBaseUrl");

            var callbackFunctionUrl = $"{hostBaseUrl}/api/ApprovalCallback/{approvalCode}";
            var approvedUrl = callbackFunctionUrl + "?result=Approved";
            var rejectedUrl = callbackFunctionUrl + "?result=Rejected";

            var emailMessage = new SendGridMessage()
            {
                Subject = "A new request is awaiting your approval",
                From = senderEmailAddress,
                TemplateId = sendgridTemplateId
            };

            emailMessage.AddTo(approverEmailAddress);
            emailMessage.SetTemplateData(
                new
                {
                    ApprovedUrl = approvedUrl,
                    RejectedUrl = rejectedUrl
                }
            );

            foreach (var dataFile in dataFiles.OrderBy(f => f.FileName))
            {
                using (var blobStream = await binder.BindAsync<Stream>(new BlobAttribute($"attachments/{dataFile.OrchestrationId}/unordered/{dataFile.FileName}", FileAccess.Read)))
                {
                    await emailMessage.AddAttachmentAsync(dataFile.FileName, blobStream, "image/png", "attachment");
                }
            }

            await sendGridMessage.AddAsync(emailMessage);

            log.LogInformation($"Sent approval request for {orchestrationId}.");
        }
    }
}