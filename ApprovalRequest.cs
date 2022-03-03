namespace DurableFunctionsDemo.DurableOrchestration
{
    public class ApprovalRequest
    {
        public int NumDataFiles { get; set;}

        public string OrchestrationId { get; set;}

        public int RequestNumber { get; set;}
    }
}