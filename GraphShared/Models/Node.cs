namespace GraphShared.Models
{
    public class Node
    {
        public int Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }

        public Node(int id, double x, double y)
        {
            Id = id;
            X = x;
            Y = y;
        }
    }
}