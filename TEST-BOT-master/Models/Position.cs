using Newtonsoft.Json;

namespace SharedLibrary
{
    public class Position
    {
        public int X;
        public int Y;

        public Position()
        {
            X = 0;
            Y = 0;
        }

        public Position(int x, int y)
        { 
            X = x;
            Y = y;
        }
        
        public override bool Equals(object obj)
        {
            // If the object is null or not a Position, it's not equal.
            if (obj is not Position other) return false;
            
            // Compare the X and Y coordinates.
            return this.X == other.X && this.Y == other.Y;
        }

        // Must override GetHashCode when overriding Equals
        public override int GetHashCode()
        {
            // Combine the hash codes of X and Y for a unique identifier
            return HashCode.Combine(X, Y);
        }
        
    }
}

