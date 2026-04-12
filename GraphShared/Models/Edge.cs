namespace GraphShared.Models
{
    public class Edge
    {
        public int Target { get; set; }
        public double Weight { get; set; }

        public Edge(int target, double weight)
        {
            Target = target;
            Weight = weight;
        }
    }
}