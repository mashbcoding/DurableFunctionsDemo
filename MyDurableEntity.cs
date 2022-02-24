using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace DurableFunctionsDemo
{
    public interface IMyDurableEntity
    {
        void Increment();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class MyDurableEntity : IMyDurableEntity
    {
        [JsonProperty("requestNumber")]
        public int RequestNumber { get; set; }

        public void Increment() => this.RequestNumber++;

        [FunctionName(nameof(MyDurableEntity))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx) => ctx.DispatchAsync<MyDurableEntity>();
    }
}