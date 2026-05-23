using Newtonsoft.Json;

namespace SharedLibrary
{
    public class Map
    {
        public int X , Y ;
        public string Name;
        public List<FieldInfo> Grid;

        public Map(int x, int y, string name, List<FieldInfo> grid)
        {
            X = x;
            Y = y;
            Name = name;
            Grid = grid;
        }

        public Map()
        {
            X = 3;
            Y = 3;
            Name = "snow";
            Grid = new List<FieldInfo>();

        }

        public Map(Map original)
        {
            X = original.X;
            Y = original.Y;
            Name = original.Name;
            Grid = original.Grid.Select(field => new FieldInfo(field)).ToList();
        }
        
        public FieldInfo GetFieldAt(Position pos)
        {
            try
            {
                //ovo samo radi ako je generisana mapa
                 var field= Grid[(pos.X * Y) + pos.Y]; 
                 return field;
                 //return Grid.Find(info => info.Position.Equals(pos));
            }
            catch (Exception e)
            {
                return null;
            }
        }
    }
}