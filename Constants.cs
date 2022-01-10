using SixLabors.ImageSharp;

namespace DurableFunctionsDemo.DurableOrchestration
{
    public static class Constants
    {
        public static Color[] Colors => new Color[] { Color.Red, Color.Orange, Color.Yellow, Color.Green, Color.Blue, Color.Indigo, Color.Violet };

        public static int ImageHeight => 200;

        public static int ImageWidth => 50;
    }
}