using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace DurableFunctionsDemo
{
    public interface IMyDurableEntity
    {
        Task<int> GetRequestNumber();

        void Increment();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class MyDurableEntity : IMyDurableEntity
    {
        [JsonProperty("requestNumber")]
        public int RequestNumber { get; set; }

        public Task<int> GetRequestNumber() => Task.FromResult(this.RequestNumber);

        public void Increment() => this.RequestNumber++;

        [FunctionName(nameof(MyDurableEntity))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx) => ctx.DispatchAsync<MyDurableEntity>();
    }
}